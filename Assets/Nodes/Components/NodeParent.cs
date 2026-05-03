using Unity.Entities;

/// <summary>
/// Marks a node as belonging to the internal graph of a parent node.
/// Child nodes are full Node entities — they participate in packet routing
/// using the same traversal as the global graph.
/// </summary>
public struct NodeParent : IComponentData
{
    public Entity Parent;
}
