using Unity.Entities;

/// <summary>
/// Singleton. Monotonic counter used to allocate StableId values.
/// Read-and-increment via <see cref="StableIdAllocator.Allocate"/>.
/// Never reset, never reused — values are unique for the lifetime of the world.
/// </summary>
public struct StableIdCounter : IComponentData
{
    public ulong Next;
}
