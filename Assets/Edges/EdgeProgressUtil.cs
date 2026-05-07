using Unity.Entities;
using Unity.Collections;

/// <summary>
/// Shared helpers for querying the per-frame edge progress snapshot.
/// Used by PacketTraverseSystem and MechanismSystem to perform physical entry checks.
/// </summary>
public static class EdgeProgressUtil
{
    /// <summary>
    /// Smallest progress value on the edge strictly greater than minExclusive.
    /// Returns float.MaxValue when no packet is ahead — edge is clear in that direction.
    /// </summary>
    public static float MinProgressGreaterThan(
        NativeParallelMultiHashMap<Entity, float> map, Entity edge, float minExclusive)
    {
        float min = float.MaxValue;
        if (map.TryGetFirstValue(edge, out float val, out var it))
        {
            do { if (val > minExclusive && val < min) min = val; }
            while (map.TryGetNextValue(out val, ref it));
        }
        return min;
    }

    /// <summary>
    /// Smallest progress value of any packet on the edge.
    /// Returns float.MaxValue when the edge is empty.
    /// A value below BeadDiameter means the entry point is physically blocked.
    /// </summary>
    public static float MinProgressOnEdge(NativeParallelMultiHashMap<Entity, float> map, Entity edge)
    {
        float min = float.MaxValue;
        if (map.TryGetFirstValue(edge, out float val, out var it))
        {
            do { if (val < min) min = val; }
            while (map.TryGetNextValue(out val, ref it));
        }
        return min;
    }
}
