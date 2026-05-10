using System;
using Unity.Entities;

/// <summary>
/// Destroy a node and its descendants. EcsSnapshot.Capture is run at Execute time
/// and stored on the command so Undo can rematerialize the full subgraph with
/// original StableIds.
/// </summary>
[Serializable]
public class DestroyNodeCommand : IEditorCommand
{
    public ulong        TargetStableId;
    public NodeSnapshot Snapshot;

    public DestroyNodeCommand() { }
    public DestroyNodeCommand(ulong targetStableId)
    {
        TargetStableId = targetStableId;
    }

    public void Execute(EntityManager em, EditorState state, StableIdRegistry registry)
    {
        Entity entity = StableIdAllocator.Resolve(em, new StableId { Value = TargetStableId });
        if (entity == Entity.Null) return;

        Snapshot = EcsSnapshot.Capture(entity, em);
        EcsSnapshot.DestroyCaptured(Snapshot, em);
    }

    public void Undo(EntityManager em, EditorState state, StableIdRegistry registry)
    {
        if (Snapshot == null) return;
        EcsSnapshot.Restore(Snapshot, em);
    }
}
