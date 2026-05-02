using Unity.Entities;

public struct NodeEdges : IComponentData
{
    public Entity EdgeA;
    public Entity EdgeB;
}