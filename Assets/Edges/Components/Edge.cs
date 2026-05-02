using Unity.Entities;

public struct Edge : IComponentData
{
    public Entity FromNode;
    public Entity ToNode;
    public float Length;

    public int Capacity;     // max packets allowed
    public int Occupancy;    // current packets on edge
}