using Unity.Entities;

public struct Packet : IComponentData
{
    public Entity CurrentEdge;
    public float Progress;   // 0 → Length
    public float Speed;      // units per second
}
