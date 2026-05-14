using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;

/// <summary>
/// Positions packet visuals along edges and sets per-packet material based on
/// simulation state. Also computes EdgeStressMap each frame for use by
/// EdgeVisualizer, MechanismVisualizer, and NodeBoundsVisualizer.
///
/// State coloring is a debug aid — it will be toggleable in a future milestone.
/// Packet identity (Packet.Color, Packet.Shape) is intentionally NOT rendered here yet;
/// those fields exist as routing-level data stubs and will be visualized in a later milestone.
///
/// Must execute before all other visualizers — enforced via DefaultExecutionOrder.
/// </summary>
[DefaultExecutionOrder(100)]
public class PacketVisualizer : MonoBehaviour
{
    public GameObject packetPrefab;

    /// <summary>Per-edge stress level computed each frame from observed packet behavior.</summary>
    public readonly Dictionary<Entity, EdgeStressLevel> EdgeStressMap = new();

    [Header("Packet Materials")]
    public Material matTraveling;
    public Material matBlocked;
    public Material matAwaiting;
    public Material matWaiting;

    readonly Dictionary<Entity, GameObject>   visuals      = new();
    readonly Dictionary<Entity, MeshRenderer> renderers    = new();
    readonly Dictionary<Entity, float>        lastProgress = new();
    readonly Dictionary<Entity, Entity>       lastEdge     = new();

    EntityManager        entityManager;
    EntityQuery          packetQuery;

    void Start()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        packetQuery   = entityManager.CreateEntityQuery(typeof(Packet));
    }

    void Update()
    {
        var packets = packetQuery.ToEntityArray(Unity.Collections.Allocator.Temp);

        // Clean up visuals for destroyed packets
        var toRemove = new List<Entity>();
        foreach (var known in visuals.Keys)
            if (!entityManager.Exists(known))
                toRemove.Add(known);
        foreach (var gone in toRemove)
        {
            Destroy(visuals[gone]);
            visuals.Remove(gone);
            renderers.Remove(gone);
            lastProgress.Remove(gone);
            lastEdge.Remove(gone);
        }

        EdgeStressMap.Clear();
        var edgeTotal   = new Dictionary<Entity, int>();
        var edgeBlocked = new Dictionary<Entity, int>();

        foreach (var entity in packets)
        {
            if (!visuals.ContainsKey(entity))
            {
                var go = Instantiate(packetPrefab);
                visuals[entity]   = go;
                renderers[entity] = go.GetComponent<MeshRenderer>();
            }

            var packet = entityManager.GetComponentData<Packet>(entity);
            var edge   = entityManager.GetComponentData<Edge>(packet.CurrentEdge);

            // Internal edges lerp between child centers; external edges lerp between the FROM
            // node's exit wall and the TO node's entry wall — matches EdgeVisualizer endpoints.
            bool isInternal = RenderingUtils.IsInternalEdge(entityManager, edge);
            Vector3 fromPos = isInternal
                ? (Vector3)entityManager.GetComponentData<WorldSpaceTransform>(edge.FromNode).Position
                : AnchorVisualizer.GetAnchorWorldPosition(entityManager, edge.FromNode, isExit: true);
            Vector3 toPos = isInternal
                ? (Vector3)entityManager.GetComponentData<WorldSpaceTransform>(edge.ToNode).Position
                : AnchorVisualizer.GetAnchorWorldPosition(entityManager, edge.ToNode,   isExit: false);

            if (float.IsNaN(fromPos.x) || float.IsNaN(toPos.x))
            {
                Debug.LogError($"[PacketVisualizer] NaN position — edge {packet.CurrentEdge.Index} " +
                    $"from {edge.FromNode.Index} pos={fromPos} to {edge.ToNode.Index} pos={toPos}");
                continue;
            }

            bool isAwaiting = entityManager.HasComponent<AwaitingRouting>(entity);
            bool isWaiting  = entityManager.HasComponent<WaitingAtNode>(entity);

            lastProgress.TryGetValue(entity, out float prevProgress);
            lastEdge.TryGetValue(entity, out Entity prevEdge);
            bool edgeChanged = prevEdge != packet.CurrentEdge;

            // Blocked: wanted to move but progress did not advance, and the edge hasn't changed
            bool isBlocked = !isAwaiting && !isWaiting && !edgeChanged &&
                             packet.Speed > 0f && packet.Progress <= prevProgress;

            lastProgress[entity] = packet.Progress;
            lastEdge[entity]     = packet.CurrentEdge;

            // Accumulate per-edge totals for stress map
            Entity edgeEnt = packet.CurrentEdge;
            if (!edgeTotal.TryAdd(edgeEnt, 1))
                edgeTotal[edgeEnt]++;
            if (isBlocked)
            {
                if (!edgeBlocked.TryAdd(edgeEnt, 1))
                    edgeBlocked[edgeEnt]++;
            }

            // Position visual
            Vector3 packetPos = edge.Length <= 0.0001f
                ? toPos
                : Vector3.Lerp(fromPos, toPos, packet.Progress / edge.Length);

            visuals[entity].transform.position = packetPos;

            // Color via shared material — cached renderer avoids per-frame GetComponent
            MeshRenderer r = renderers[entity];
            if (r != null)
            {
                r.sharedMaterial = isAwaiting ? matAwaiting :
                                   isWaiting  ? matWaiting  :
                                   isBlocked  ? matBlocked  : matTraveling;
            }
        }

        // Build edge stress map from per-edge counts
        foreach (var kv in edgeTotal)
        {
            Entity e    = kv.Key;
            int total   = kv.Value;
            edgeBlocked.TryGetValue(e, out int blocked);

            EdgeStressMap[e] = blocked == 0    ? EdgeStressLevel.Flowing  :
                               blocked < total ? EdgeStressLevel.Stressed :
                                                 EdgeStressLevel.Jammed;
        }

        packets.Dispose();
    }
}
