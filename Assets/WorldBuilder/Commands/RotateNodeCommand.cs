using System;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Sets a node's local-space rotation. Stamps TransformDirty so the world cascade
/// recomputes. Undo restores the previous rotation.
/// </summary>
[Serializable]
public class RotateNodeCommand : IEditorCommand
{
    public ulong      TargetStableId;
    public quaternion PreviousRotation;
    public quaternion NewRotation;

    public RotateNodeCommand() { }
    public RotateNodeCommand(ulong targetStableId, quaternion previousRotation, quaternion newRotation)
    {
        TargetStableId   = targetStableId;
        PreviousRotation = previousRotation;
        NewRotation      = newRotation;
    }

    public void Execute(EntityManager em, EditorState state, StableIdRegistry registry)
        => Apply(em, NewRotation);

    public void Undo(EntityManager em, EditorState state, StableIdRegistry registry)
        => Apply(em, PreviousRotation);

    void Apply(EntityManager em, quaternion r)
    {
        Entity e = StableIdAllocator.Resolve(em, new StableId { Value = TargetStableId });
        if (e == Entity.Null || !em.HasComponent<NodeTransform>(e)) return;
        var t = em.GetComponentData<NodeTransform>(e);
        t.Rotation = r;
        em.SetComponentData(e, t);
        if (!em.HasComponent<TransformDirty>(e)) em.AddComponent<TransformDirty>(e);
    }
}
