using System;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Destroy an existing edge. The pre-destroy state is captured into an EdgeSnapshot
/// so Undo can rematerialize the edge with its original StableId.
/// </summary>
[Serializable]
public class DisconnectEdgeCommand : IEditorCommand
{
    public ulong        EdgeStableId;
    public EdgeSnapshot Snapshot;

    public DisconnectEdgeCommand() { }
    public DisconnectEdgeCommand(ulong edgeStableId)
    {
        EdgeStableId = edgeStableId;
    }

    public void Execute(EntityManager em, EditorState state, StableIdRegistry registry)
    {
        Entity edge = StableIdAllocator.Resolve(em, new StableId { Value = EdgeStableId });
        if (edge == Entity.Null) return;

        var e = em.GetComponentData<Edge>(edge);
        Snapshot = new EdgeSnapshot
        {
            EdgeStableId = EdgeStableId,
            FromStableId = em.HasComponent<StableId>(e.FromNode) ? em.GetComponentData<StableId>(e.FromNode).Value : 0,
            ToStableId   = em.HasComponent<StableId>(e.ToNode)   ? em.GetComponentData<StableId>(e.ToNode).Value   : 0,
            Length       = e.Length,
        };

        if (em.HasComponent<Mechanism>(e.FromNode) && em.HasBuffer<MechanismConnections>(e.FromNode))
        {
            var buf = em.GetBuffer<MechanismConnections>(e.FromNode);
            for (int i = buf.Length - 1; i >= 0; i--)
                if (buf[i].Edge == edge) buf.RemoveAt(i);
        }

        StableIdAllocator.Unregister(em, new StableId { Value = EdgeStableId });
        em.DestroyEntity(edge);
    }

    public void Undo(EntityManager em, EditorState state, StableIdRegistry registry)
    {
        if (Snapshot == null) return;
        Entity from = StableIdAllocator.Resolve(em, new StableId { Value = Snapshot.FromStableId });
        Entity to   = StableIdAllocator.Resolve(em, new StableId { Value = Snapshot.ToStableId });
        if (from == Entity.Null || to == Entity.Null) return;

        Entity edge = em.CreateEntity(typeof(Edge), typeof(StableId));
        em.SetComponentData(edge, new StableId { Value = Snapshot.EdgeStableId });
        StableIdAllocator.Register(em, new StableId { Value = Snapshot.EdgeStableId }, edge);
        em.SetComponentData(edge, new Edge { FromNode = from, ToNode = to, Length = Snapshot.Length });

        if (em.HasComponent<Mechanism>(from) && em.HasBuffer<MechanismConnections>(from))
            em.GetBuffer<MechanismConnections>(from).Add(new MechanismConnections { Edge = edge });
    }
}
