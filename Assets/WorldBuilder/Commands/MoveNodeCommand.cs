using System;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Sets a node's local-space position. Stamps TransformDirty so WorldSpaceCacheSystem
/// recomputes the world-space cascade. Undo restores the previous position.
///
/// Note: during a drag the InputHandler writes NodeTransform directly each frame
/// and only emits a MoveNodeCommand on mouse-up — so the undo stack contains one
/// step per drag, not one per frame.
/// </summary>
[Serializable]
public class MoveNodeCommand : IEditorCommand
{
    public ulong  TargetStableId;
    public float3 PreviousLocalPosition;
    public float3 NewLocalPosition;

    public MoveNodeCommand() { }
    public MoveNodeCommand(ulong targetStableId, float3 previousLocalPosition, float3 newLocalPosition)
    {
        TargetStableId        = targetStableId;
        PreviousLocalPosition = previousLocalPosition;
        NewLocalPosition      = newLocalPosition;
    }

    public void Execute(EntityManager em, EditorState state, StableIdRegistry registry)
        => Apply(em, NewLocalPosition);

    public void Undo(EntityManager em, EditorState state, StableIdRegistry registry)
        => Apply(em, PreviousLocalPosition);

    void Apply(EntityManager em, float3 pos)
    {
        Entity e = StableIdAllocator.Resolve(em, new StableId { Value = TargetStableId });
        if (e == Entity.Null || !em.HasComponent<NodeTransform>(e)) return;
        var t = em.GetComponentData<NodeTransform>(e);
        t.Position = pos;
        em.SetComponentData(e, t);
        if (!em.HasComponent<TransformDirty>(e)) em.AddComponent<TransformDirty>(e);
    }
}
