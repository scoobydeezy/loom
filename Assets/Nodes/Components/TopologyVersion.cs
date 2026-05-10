using Unity.Entities;

/// <summary>
/// Singleton. Incremented whenever topology changes — entity added, removed, or reparented.
/// WorldSpaceCacheSystem rebuilds its sorted traversal order on version change.
/// Must be incremented by any operation that modifies the node graph structure.
/// </summary>
public struct TopologyVersion : IComponentData
{
    public int Version;
}
