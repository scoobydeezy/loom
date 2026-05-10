using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Singleton. Incremented whenever topology changes — entity added, removed, or reparented.
/// WorldSpaceCacheSystem rebuilds its sorted traversal order on version change.
/// Must be incremented by any operation that modifies the node graph structure.
/// </summary>
public struct TopologyVersion : IComponentData
{
    public int Version;

    /// <summary>
    /// Ensures the singleton exists, then increments its version.
    /// Call after any structural change (spawn, destroy, reparent).
    /// </summary>
    public static void Increment(EntityManager em)
    {
        var q = em.CreateEntityQuery(typeof(TopologyVersion));
        if (q.CalculateEntityCount() == 0)
        {
            Entity e = em.CreateEntity(typeof(TopologyVersion));
            em.SetComponentData(e, new TopologyVersion { Version = 1 });
            return;
        }

        var arr = q.ToEntityArray(Allocator.Temp);
        Entity e0  = arr[0];
        int    cur = em.GetComponentData<TopologyVersion>(e0).Version;
        em.SetComponentData(e0, new TopologyVersion { Version = cur + 1 });
        arr.Dispose();
    }
}
