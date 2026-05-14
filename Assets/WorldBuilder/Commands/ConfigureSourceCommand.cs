using System;
using Unity.Entities;

/// <summary>
/// Reconfigures a PacketSource node's emit rate, color, and shape. The
/// inspector emits one of these per inspector commit so each change is its
/// own undo step.
///
/// Accumulator is intentionally preserved across reconfiguration — changing
/// rate mid-flight should not reset the source's accumulated emission debt.
/// </summary>
[Serializable]
public class ConfigureSourceCommand : IEditorCommand
{
    public ulong       TargetStableId;
    public float       PreviousEmitRate, NewEmitRate;
    public PacketColor PreviousColor,    NewColor;
    public PacketShape PreviousShape,    NewShape;

    public ConfigureSourceCommand() { }

    public ConfigureSourceCommand(
        ulong targetStableId,
        float previousEmitRate, float newEmitRate,
        PacketColor previousColor, PacketColor newColor,
        PacketShape previousShape, PacketShape newShape)
    {
        TargetStableId   = targetStableId;
        PreviousEmitRate = previousEmitRate;
        NewEmitRate      = newEmitRate;
        PreviousColor    = previousColor;
        NewColor         = newColor;
        PreviousShape    = previousShape;
        NewShape         = newShape;
    }

    public void Execute(EntityManager em, EditorState state, StableIdRegistry registry)
        => Apply(em, NewEmitRate, NewColor, NewShape);

    public void Undo(EntityManager em, EditorState state, StableIdRegistry registry)
        => Apply(em, PreviousEmitRate, PreviousColor, PreviousShape);

    void Apply(EntityManager em, float rate, PacketColor color, PacketShape shape)
    {
        Entity entity = StableIdAllocator.Resolve(em, new StableId { Value = TargetStableId });
        if (entity == Entity.Null || !em.HasComponent<PacketSource>(entity)) return;

        var ps = em.GetComponentData<PacketSource>(entity);
        ps.EmitRate = rate;
        ps.Color    = color;
        ps.Shape    = shape;
        em.SetComponentData(entity, ps);
    }
}
