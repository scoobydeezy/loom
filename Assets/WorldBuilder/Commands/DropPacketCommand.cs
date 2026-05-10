using System;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Drop a packet at a specific world position. Raised by the simulation
/// (Phase 4 tail-drop) via SimulationEventBus — executed with recordForUndo: false
/// because simulation events are not user-undoable.
///
/// Currently destroys the packet entity. When dropped-packet corpses are
/// implemented, this command will spawn the corpse at <see cref="Position"/>
/// before destroying the original packet.
/// </summary>
[Serializable]
public class DropPacketCommand : IEditorCommand
{
    public ulong  PacketStableId;
    public float3 Position;

    public DropPacketCommand() { }
    public DropPacketCommand(ulong packetStableId, float3 position)
    {
        PacketStableId = packetStableId;
        Position       = position;
    }

    public void Execute(EntityManager em, EditorState state, StableIdRegistry registry)
    {
        Entity packet = StableIdAllocator.Resolve(em, new StableId { Value = PacketStableId });
        if (packet == Entity.Null) return;
        StableIdAllocator.Unregister(em, new StableId { Value = PacketStableId });
        em.DestroyEntity(packet);
    }

    public void Undo(EntityManager em, EditorState state, StableIdRegistry registry)
    {
        // Simulation events are not undoable.
    }
}
