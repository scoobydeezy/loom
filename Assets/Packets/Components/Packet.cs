using Unity.Entities;

public struct Packet : IComponentData
{
    public Entity CurrentEdge;
    public int PathIndex;
    public float Progress;
    public float Speed;
}