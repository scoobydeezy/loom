using Unity.Entities;

public struct PacketSlot : IComponentData
{
    public int SlotIndex; // -1 = unassigned; set and managed by PacketVisualizer at edge entry
}
