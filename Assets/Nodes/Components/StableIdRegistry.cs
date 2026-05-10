using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Singleton. Maintains a StableId → Entity lookup so commands and the editor
/// can address entities by stable identity rather than runtime <see cref="Entity"/>.
/// Populated at spawn, updated on destroy, owned by the world for its lifetime.
/// </summary>
public struct StableIdRegistry : IComponentData
{
    public NativeHashMap<ulong, Entity> Map;
}
