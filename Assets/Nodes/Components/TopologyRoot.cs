using Unity.Entities;

/// <summary>
/// The top-level root entity that owns this entity.
/// Set once at spawn time and inherited by all children.
/// Enables cheap root lookup without parent-chain traversal.
/// Used for scenario events, cost accounting, failure injection, and save/load.
/// </summary>
public struct TopologyRoot : IComponentData
{
    public Entity Root;
}
