using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>
/// Composes world-space transforms from local-space NodeTransform values up the NodeParent chain.
/// Rebuilds the parent-before-child traversal order only when TopologyVersion changes.
/// Runs before all simulation reads of position so cached values are fresh.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(EdgeProgressCacheSystem))]
[UpdateBefore(typeof(PacketTraverseSystem))]
public partial struct WorldSpaceCacheSystem : ISystem
{
    NativeList<Entity> _sorted;
    int                _lastVersion;

    public void OnCreate(ref SystemState state)
    {
        _sorted      = new NativeList<Entity>(64, Allocator.Persistent);
        _lastVersion = int.MinValue;
    }

    public void OnDestroy(ref SystemState state)
    {
        if (_sorted.IsCreated) _sorted.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<TopologyVersion>()) return;

        int version = SystemAPI.GetSingleton<TopologyVersion>().Version;
        if (version != _lastVersion)
        {
            RebuildSort(ref state);
            _lastVersion = version;
        }

        var em = state.EntityManager;
        for (int i = 0; i < _sorted.Length; i++)
        {
            Entity e = _sorted[i];
            if (!em.Exists(e)) continue;
            if (!em.HasComponent<NodeTransform>(e) || !em.HasComponent<WorldSpaceTransform>(e)) continue;

            var lt = em.GetComponentData<NodeTransform>(e);

            float3     worldPos;
            quaternion worldRot;

            if (em.HasComponent<NodeParent>(e))
            {
                Entity parent = em.GetComponentData<NodeParent>(e).Parent;
                if (!em.Exists(parent) || !em.HasComponent<WorldSpaceTransform>(parent))
                    continue; // defensive: skip orphans whose parent is gone

                var pw = em.GetComponentData<WorldSpaceTransform>(parent);
                worldPos = pw.Position + math.rotate(pw.Rotation, lt.Position);
                worldRot = math.mul(pw.Rotation, lt.Rotation);
            }
            else
            {
                worldPos = lt.Position;
                worldRot = lt.Rotation;
            }

            em.SetComponentData(e, new WorldSpaceTransform { Position = worldPos, Rotation = worldRot });
        }
    }

    void RebuildSort(ref SystemState state)
    {
        var em = state.EntityManager;
        _sorted.Clear();

        var query = em.CreateEntityQuery(
            ComponentType.ReadOnly<NodeTransform>(),
            ComponentType.ReadOnly<WorldSpaceTransform>());
        var all = query.ToEntityArray(Allocator.Temp);

        var children = new NativeParallelMultiHashMap<Entity, Entity>(all.Length, Allocator.Temp);
        var roots    = new NativeList<Entity>(all.Length, Allocator.Temp);

        for (int i = 0; i < all.Length; i++)
        {
            Entity e = all[i];
            if (em.HasComponent<NodeParent>(e))
            {
                Entity p = em.GetComponentData<NodeParent>(e).Parent;
                if (em.Exists(p)) children.Add(p, e);
                else              roots.Add(e); // parent gone — treat as root
            }
            else
            {
                roots.Add(e);
            }
        }

        var queue = new NativeQueue<Entity>(Allocator.Temp);
        for (int i = 0; i < roots.Length; i++) queue.Enqueue(roots[i]);

        while (queue.Count > 0)
        {
            Entity e = queue.Dequeue();
            _sorted.Add(e);

            if (children.TryGetFirstValue(e, out var c, out var it))
            {
                do { queue.Enqueue(c); }
                while (children.TryGetNextValue(out c, ref it));
            }
        }

        all.Dispose();
        children.Dispose();
        roots.Dispose();
        queue.Dispose();
    }
}
