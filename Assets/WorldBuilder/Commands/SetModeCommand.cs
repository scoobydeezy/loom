using System;
using Unity.Entities;

/// <summary>
/// Transitions editor mode between Edit and Play. Play → Edit clears packets and
/// resets simulation state; Edit → Play enables the SimulationSystemGroup.
/// Play → Edit is treated as a non-undoable boundary — Undo for that transition is a no-op.
/// </summary>
[Serializable]
public class SetModeCommand : IEditorCommand
{
    public LoomMode PreviousMode;
    public LoomMode NewMode;

    public SetModeCommand() { }
    public SetModeCommand(LoomMode previousMode, LoomMode newMode)
    {
        PreviousMode = previousMode;
        NewMode      = newMode;
    }

    public void Execute(EntityManager em, EditorState state, StableIdRegistry registry)
    {
        if (state == null) return;

        // Edit → Play: stamp topology and start the sim. PacketSourceSystem takes it from here —
        // no packets are spawned here, sources emit them.
        // Play → Edit: clear packets and reset source accumulators so the next Play starts clean.
        if (PreviousMode == LoomMode.Edit && NewMode == LoomMode.Play)
        {
            TopologyVersion.Increment(em);
        }
        else if (PreviousMode == LoomMode.Play && NewMode == LoomMode.Edit)
        {
            ClearPackets(em);
            ResetPacketSources(em);
        }

        state.ApplyModeChange(NewMode);
    }

    public void Undo(EntityManager em, EditorState state, StableIdRegistry registry)
    {
        // Play → Edit is irreversible — undo is a no-op for that direction.
        if (PreviousMode == LoomMode.Play && NewMode == LoomMode.Edit) return;
        if (state == null) return;
        state.ApplyModeChange(PreviousMode);
    }

    static void ClearPackets(EntityManager em)
    {
        using var q = em.CreateEntityQuery(typeof(Packet));
        using var all = q.ToEntityArray(Unity.Collections.Allocator.Temp);
        for (int i = 0; i < all.Length; i++)
        {
            if (em.HasComponent<StableId>(all[i]))
                StableIdAllocator.Unregister(em, em.GetComponentData<StableId>(all[i]));
        }
        em.DestroyEntity(q);
    }

    static void ResetPacketSources(EntityManager em)
    {
        using var q = em.CreateEntityQuery(typeof(PacketSource));
        using var all = q.ToEntityArray(Unity.Collections.Allocator.Temp);
        for (int i = 0; i < all.Length; i++)
        {
            var ps = em.GetComponentData<PacketSource>(all[i]);
            ps.Accumulator = 0f;
            em.SetComponentData(all[i], ps);
        }
    }
}
