using Unity.Entities;

public struct Edge : IComponentData
{
    public Entity FromNode;
    public Entity ToNode;
    public float Length;   // latency
}
