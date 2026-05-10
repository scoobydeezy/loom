using UnityEngine;
using Unity.Entities;

/// <summary>
/// Helpers for allocating, registering, and looking up StableIds.
/// All bootstrap, command, and editor code goes through this — never read or mutate
/// <see cref="StableIdCounter"/> / <see cref="StableIdRegistry"/> directly.
///
/// Allocation of the registry's native map is owned exclusively by
/// <see cref="StableIdRegistrySystem"/> — this class never allocates native
/// containers. Doing so once leaked when the system later ran <c>OnCreate</c>,
/// saw the existing singleton, and never took ownership of the map to dispose it.
/// </summary>
public static class StableIdAllocator
{
    /// <summary>
    /// Non-allocating defensive check. <see cref="StableIdRegistrySystem"/> owns
    /// singleton creation; this method just verifies the system has run and logs
    /// a clear error if the bootstrap fired before world initialization completed.
    /// </summary>
    public static void EnsureSingletons(EntityManager em)
    {
        bool hasCounter  = em.CreateEntityQuery(typeof(StableIdCounter)).CalculateEntityCount()  > 0;
        bool hasRegistry = em.CreateEntityQuery(typeof(StableIdRegistry)).CalculateEntityCount() > 0;
        if (!hasCounter || !hasRegistry)
        {
            Debug.LogError("[StableIdAllocator] StableId singletons missing — StableIdRegistrySystem did not run before this call. Check default-world initialization and system registration.");
        }
    }

    /// <summary>
    /// Allocates a fresh StableId by reading and incrementing the counter.
    /// </summary>
    public static StableId Allocate(EntityManager em)
    {
        var q       = em.CreateEntityQuery(typeof(StableIdCounter));
        var counter = q.GetSingleton<StableIdCounter>();
        ulong value = counter.Next;
        counter.Next++;
        q.SetSingleton(counter);
        return new StableId { Value = value };
    }

    /// <summary>
    /// Stamps the given entity with a freshly allocated StableId and registers it.
    /// </summary>
    public static StableId StampAndRegister(EntityManager em, Entity entity)
    {
        var id = Allocate(em);
        em.SetComponentData(entity, id);
        Register(em, id, entity);
        return id;
    }

    /// <summary>
    /// Adds a (StableId, Entity) mapping to the registry.
    /// </summary>
    public static void Register(EntityManager em, StableId id, Entity entity)
    {
        var registry = em.CreateEntityQuery(typeof(StableIdRegistry)).GetSingleton<StableIdRegistry>();
        if (registry.Map.IsCreated)
            registry.Map[id.Value] = entity;
    }

    /// <summary>
    /// Removes a StableId mapping from the registry. Called on destroy.
    /// </summary>
    public static void Unregister(EntityManager em, StableId id)
    {
        var registry = em.CreateEntityQuery(typeof(StableIdRegistry)).GetSingleton<StableIdRegistry>();
        if (registry.Map.IsCreated)
            registry.Map.Remove(id.Value);
    }

    /// <summary>
    /// Resolves a StableId to its current runtime <see cref="Entity"/>.
    /// Returns <see cref="Entity.Null"/> if no mapping exists.
    /// </summary>
    public static Entity Resolve(EntityManager em, StableId id)
    {
        if (id.Value == 0) return Entity.Null;
        var registry = em.CreateEntityQuery(typeof(StableIdRegistry)).GetSingleton<StableIdRegistry>();
        if (!registry.Map.IsCreated) return Entity.Null;
        return registry.Map.TryGetValue(id.Value, out var e) ? e : Entity.Null;
    }
}
