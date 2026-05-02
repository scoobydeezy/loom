using Unity.Entities;

public struct Node : IComponentData
{
    public int Id;
    public Entity InternalEdge;
}
