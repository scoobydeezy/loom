using Unity.Entities;
using Unity.Collections;

/// <summary>
/// Singleton component. Holds the per-frame snapshot of every packet's progress on its edge,
/// keyed by edge entity. Built once per frame by EdgeProgressCacheSystem before any traversal
/// or routing runs. Both PacketTraverseSystem and MechanismSystem read from this map.
/// </summary>
public struct EdgeProgressCache : IComponentData
{
    public NativeParallelMultiHashMap<Entity, float> Map;
}
