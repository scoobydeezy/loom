using Unity.Entities;

public struct NodeLayout : IComponentData
{
    public int LaneCount;
    public Entity EntryEdge;
    public Entity ExitEdge;
}
