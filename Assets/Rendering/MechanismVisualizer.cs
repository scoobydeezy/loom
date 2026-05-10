using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;

/// <summary>
/// Renders a diamond (rotated square) at each Mechanism entity's WorldSpaceTransform position.
/// Color reflects routing pressure: idle, active (has packets awaiting routing),
/// or pressured (active and an inbound edge has blocked traffic).
/// Reads ECS state every frame — never caches positions, so node dragging works automatically.
/// </summary>
[DefaultExecutionOrder(300)]
public class MechanismVisualizer : MonoBehaviour
{
    const float DiamondSize = 0.22f;
    const float LineWidth   = 0.04f;

    [Header("Mechanism Materials")]
    public Material matIdle;
    public Material matActive;
    public Material matPressured;

    readonly Dictionary<Entity, LineRenderer> diamonds = new();

    EntityManager    entityManager;
    EntityQuery      mechanismQuery;
    EntityQuery      awaitingQuery;
    EntityQuery      edgeQuery;
    PacketVisualizer packetVisualizer;

    void Start()
    {
        entityManager  = World.DefaultGameObjectInjectionWorld.EntityManager;
        mechanismQuery = entityManager.CreateEntityQuery(typeof(Mechanism), typeof(WorldSpaceTransform));
        awaitingQuery  = entityManager.CreateEntityQuery(typeof(Packet), typeof(AwaitingRouting));
        edgeQuery      = entityManager.CreateEntityQuery(typeof(Edge));
    }

    void Update()
    {
        if (packetVisualizer == null)
            packetVisualizer = FindAnyObjectByType<PacketVisualizer>();

        // Set of mechanisms that currently have packets awaiting routing
        var activeMechanisms = new HashSet<Entity>();
        var awaitingPackets  = awaitingQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        foreach (var entity in awaitingPackets)
        {
            var packet = entityManager.GetComponentData<Packet>(entity);
            if (!entityManager.Exists(packet.CurrentEdge)) continue;
            var edge = entityManager.GetComponentData<Edge>(packet.CurrentEdge);
            activeMechanisms.Add(edge.ToNode);
        }
        awaitingPackets.Dispose();

        // Set of mechanisms whose inbound edges show stress — only meaningful when active
        var pressuredMechanisms = new HashSet<Entity>();
        if (packetVisualizer != null && activeMechanisms.Count > 0)
        {
            var edges = edgeQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            foreach (var edgeEntity in edges)
            {
                if (!packetVisualizer.EdgeStressMap.TryGetValue(edgeEntity, out var stress)) continue;
                if (stress != EdgeStressLevel.Stressed && stress != EdgeStressLevel.Jammed) continue;

                var edge = entityManager.GetComponentData<Edge>(edgeEntity);
                if (activeMechanisms.Contains(edge.ToNode))
                    pressuredMechanisms.Add(edge.ToNode);
            }
            edges.Dispose();
        }

        var mechanisms = mechanismQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        foreach (var entity in mechanisms)
        {
            if (!diamonds.TryGetValue(entity, out var lr))
            {
                lr = CreateDiamond();
                diamonds[entity] = lr;
            }

            var pos = (Vector3)entityManager.GetComponentData<WorldSpaceTransform>(entity).Position;
            SetDiamondPositions(lr, pos);

            lr.material = pressuredMechanisms.Contains(entity) ? matPressured :
                          activeMechanisms.Contains(entity)    ? matActive    :
                                                                 matIdle;
        }

        // Disable diamonds for mechanisms that no longer exist
        foreach (var kv in diamonds)
        {
            if (!entityManager.Exists(kv.Key))
                kv.Value.enabled = false;
        }

        mechanisms.Dispose();
    }

    static void SetDiamondPositions(LineRenderer lr, Vector3 center)
    {
        float s = DiamondSize;
        lr.SetPosition(0, center + new Vector3( 0,  s, 0));
        lr.SetPosition(1, center + new Vector3( s,  0, 0));
        lr.SetPosition(2, center + new Vector3( 0, -s, 0));
        lr.SetPosition(3, center + new Vector3(-s,  0, 0));
        lr.SetPosition(4, center + new Vector3( 0,  s, 0));
    }

    LineRenderer CreateDiamond()
    {
        var go = new GameObject("Mechanism");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 5;
        lr.loop          = false;
        lr.useWorldSpace = true;
        lr.startWidth    = LineWidth;
        lr.endWidth      = LineWidth;
        lr.material      = matIdle;
        return lr;
    }
}
