using System;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Create an edge between two anchors. Length is derived from the world-space
/// distance between the endpoints — never specified manually.
/// The spawned edge's StableId is allocated on first Execute and reused on Redo
/// so undo/redo round-trips preserve stable identity.
/// </summary>
[Serializable]
public class ConnectEdgeCommand : IEditorCommand
{
    public ulong FromAnchorStableId;
    public ulong ToAnchorStableId;
    public ulong SpawnedEdgeStableId; // 0 until first Execute allocates one

    public ConnectEdgeCommand() { }
    public ConnectEdgeCommand(ulong fromAnchorStableId, ulong toAnchorStableId)
    {
        FromAnchorStableId = fromAnchorStableId;
        ToAnchorStableId   = toAnchorStableId;
    }

    public void Execute(EntityManager em, EditorState state, StableIdRegistry registry)
    {
        Entity from = StableIdAllocator.Resolve(em, new StableId { Value = FromAnchorStableId });
        Entity to   = StableIdAllocator.Resolve(em, new StableId { Value = ToAnchorStableId });
        if (from == Entity.Null || to == Entity.Null) return;

        Entity edge = em.CreateEntity(typeof(Edge), typeof(StableId));

        // First Execute allocates; Redo reuses the original allocation.
        if (SpawnedEdgeStableId == 0)
        {
            var id = StableIdAllocator.Allocate(em);
            SpawnedEdgeStableId = id.Value;
        }
        em.SetComponentData(edge, new StableId { Value = SpawnedEdgeStableId });
        StableIdAllocator.Register(em, new StableId { Value = SpawnedEdgeStableId }, edge);

        var fromPos = em.GetComponentData<WorldSpaceTransform>(from).Position;
        var toPos   = em.GetComponentData<WorldSpaceTransform>(to).Position;
        em.SetComponentData(edge, new Edge
        {
            FromNode = from,
            ToNode   = to,
            Length   = math.distance(fromPos, toPos),
        });

        if (em.HasComponent<Mechanism>(from) && em.HasBuffer<MechanismConnections>(from))
            em.GetBuffer<MechanismConnections>(from).Add(new MechanismConnections { Edge = edge });
    }

    public void Undo(EntityManager em, EditorState state, StableIdRegistry registry)
    {
        if (SpawnedEdgeStableId == 0) return;
        Entity edge = StableIdAllocator.Resolve(em, new StableId { Value = SpawnedEdgeStableId });
        if (edge == Entity.Null) return;

        // Strip the edge from any MechanismConnections buffer that referenced it.
        if (em.HasComponent<Edge>(edge))
        {
            var e = em.GetComponentData<Edge>(edge);
            if (em.HasComponent<Mechanism>(e.FromNode) && em.HasBuffer<MechanismConnections>(e.FromNode))
            {
                var buf = em.GetBuffer<MechanismConnections>(e.FromNode);
                for (int i = buf.Length - 1; i >= 0; i--)
                    if (buf[i].Edge == edge) buf.RemoveAt(i);
            }
        }

        StableIdAllocator.Unregister(em, new StableId { Value = SpawnedEdgeStableId });
        em.DestroyEntity(edge);
    }
}
