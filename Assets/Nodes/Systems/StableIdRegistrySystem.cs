using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Owns the lifecycle of the <see cref="StableIdCounter"/> and
/// <see cref="StableIdRegistry"/> singletons. Creates them in <c>OnCreate</c>
/// so they exist before any bootstrap or world-builder code runs, and disposes
/// the registry's native map in <c>OnDestroy</c> so persistent allocations
/// do not leak on play-mode exit or domain reload.
/// </summary>
public partial struct StableIdRegistrySystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        if (state.EntityManager.CreateEntityQuery(typeof(StableIdCounter)).CalculateEntityCount() == 0)
        {
            Entity counterEntity = state.EntityManager.CreateEntity(typeof(StableIdCounter));
            state.EntityManager.SetComponentData(counterEntity, new StableIdCounter { Next = 1 });
        }

        if (state.EntityManager.CreateEntityQuery(typeof(StableIdRegistry)).CalculateEntityCount() == 0)
        {
            Entity registryEntity = state.EntityManager.CreateEntity(typeof(StableIdRegistry));
            state.EntityManager.SetComponentData(registryEntity, new StableIdRegistry
            {
                Map = new NativeHashMap<ulong, Entity>(1024, Allocator.Persistent),
            });
        }
    }

    public void OnUpdate(ref SystemState state) { }

    public void OnDestroy(ref SystemState state)
    {
        var query = state.EntityManager.CreateEntityQuery(typeof(StableIdRegistry));
        if (query.CalculateEntityCount() == 0) return;
        var registry = query.GetSingleton<StableIdRegistry>();
        if (registry.Map.IsCreated) registry.Map.Dispose();
    }
}
