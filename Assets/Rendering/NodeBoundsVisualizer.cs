using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;

/// <summary>
/// Draws an axis-aligned bounding box around each composite node's children every frame.
/// Outline color reflects internal-graph health: normal, active (packets inside),
/// or pressured (packets inside are blocked or waiting).
/// Reads NodeParent and WorldSpaceTransform from ECS — never caches positions.
/// </summary>
[DefaultExecutionOrder(400)]
public class NodeBoundsVisualizer : MonoBehaviour
{
    const float Padding   = 0.4f;
    const float LineWidth = 0.04f;

    enum BoundsState { Normal, Active, Pressured }

    [Header("Node Bounds Materials")]
    public Material matNormal;
    public Material matActive;
    public Material matPressured;

    [Header("Debug")]
    public bool showDebugBounds = true;

    readonly Dictionary<Entity, LineRenderer> boxes = new();

    EntityManager    entityManager;
    EntityQuery      childQuery;
    EntityQuery      edgeQuery;
    EntityQuery      waitingQuery;
    PacketVisualizer packetVisualizer;

    void Start()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        childQuery    = entityManager.CreateEntityQuery(typeof(NodeParent), typeof(WorldSpaceTransform));
        edgeQuery     = entityManager.CreateEntityQuery(typeof(Edge));
        waitingQuery  = entityManager.CreateEntityQuery(typeof(Packet), typeof(WaitingAtNode));
    }

    void Update()
    {
        if (!showDebugBounds)
        {
            foreach (var kv in boxes)
                kv.Value.enabled = false;
            return;
        }

        if (packetVisualizer == null)
            packetVisualizer = FindAnyObjectByType<PacketVisualizer>();

        // Collect children grouped by parent — bounds + per-parent state
        var children = childQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        var parentBounds = new Dictionary<Entity, (Vector3 min, Vector3 max)>();

        foreach (var child in children)
        {
            var parent = entityManager.GetComponentData<NodeParent>(child).Parent;
            var pos    = (Vector3)entityManager.GetComponentData<WorldSpaceTransform>(child).Position;

            if (parentBounds.TryGetValue(parent, out var b))
                parentBounds[parent] = (Vector3.Min(b.min, pos), Vector3.Max(b.max, pos));
            else
                parentBounds[parent] = (pos, pos);
        }
        children.Dispose();

        // Per-parent state — start at Normal, escalate based on observed packet behavior
        var parentStates = new Dictionary<Entity, BoundsState>();

        // Walk all edges; for any internal edge (FromNode is a child), check stress on it
        var edges = edgeQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        foreach (var edgeEntity in edges)
        {
            var edge = entityManager.GetComponentData<Edge>(edgeEntity);
            if (!entityManager.HasComponent<NodeParent>(edge.FromNode)) continue;

            Entity parent = entityManager.GetComponentData<NodeParent>(edge.FromNode).Parent;

            EdgeStressLevel stress = EdgeStressLevel.Free;
            if (packetVisualizer != null)
                packetVisualizer.EdgeStressMap.TryGetValue(edgeEntity, out stress);

            if (stress == EdgeStressLevel.Free) continue;

            BoundsState newState = (stress == EdgeStressLevel.Stressed || stress == EdgeStressLevel.Jammed)
                ? BoundsState.Pressured
                : BoundsState.Active;

            Escalate(parentStates, parent, newState);
        }
        edges.Dispose();

        // WaitingAtNode packets at child nodes count as pressured for that parent
        var waitingPackets = waitingQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        foreach (var entity in waitingPackets)
        {
            var packet = entityManager.GetComponentData<Packet>(entity);
            if (!entityManager.Exists(packet.CurrentEdge)) continue;

            var edge = entityManager.GetComponentData<Edge>(packet.CurrentEdge);
            if (!entityManager.HasComponent<NodeParent>(edge.ToNode)) continue;

            Entity parent = entityManager.GetComponentData<NodeParent>(edge.ToNode).Parent;
            Escalate(parentStates, parent, BoundsState.Pressured);
        }
        waitingPackets.Dispose();

        // Draw or update a box for each composite parent
        foreach (var kv in parentBounds)
        {
            Entity parent = kv.Key;
            var    min    = kv.Value.min - Vector3.one * Padding;
            var    max    = kv.Value.max + Vector3.one * Padding;

            if (!boxes.TryGetValue(parent, out var lr))
            {
                lr = CreateBoxLine();
                boxes[parent] = lr;
            }

            lr.SetPosition(0, new Vector3(min.x, min.y, min.z));
            lr.SetPosition(1, new Vector3(max.x, min.y, min.z));
            lr.SetPosition(2, new Vector3(max.x, max.y, min.z));
            lr.SetPosition(3, new Vector3(min.x, max.y, min.z));
            lr.SetPosition(4, new Vector3(min.x, min.y, min.z));

            BoundsState state = parentStates.TryGetValue(parent, out var s) ? s : BoundsState.Normal;
            lr.material = state switch
            {
                BoundsState.Pressured => matPressured,
                BoundsState.Active    => matActive,
                _                     => matNormal,
            };
        }

        // Hide boxes for parents that no longer have children
        foreach (var kv in boxes)
            kv.Value.enabled = parentBounds.ContainsKey(kv.Key);
    }

    static void Escalate(Dictionary<Entity, BoundsState> states, Entity parent, BoundsState newState)
    {
        if (!states.TryGetValue(parent, out var current) || newState > current)
            states[parent] = newState;
    }

    LineRenderer CreateBoxLine()
    {
        var go = new GameObject("NodeBounds");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 5;
        lr.loop          = false;
        lr.useWorldSpace = true;
        lr.startWidth    = LineWidth;
        lr.endWidth      = LineWidth;
        lr.material      = matNormal;
        return lr;
    }
}
