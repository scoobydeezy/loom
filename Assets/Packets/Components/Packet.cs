using Unity.Entities;

public struct Packet : IComponentData
{
    public Entity      CurrentEdge;
    public float       Progress;
    public float       Speed;
    public PacketColor Color;
    public PacketShape Shape;
}
