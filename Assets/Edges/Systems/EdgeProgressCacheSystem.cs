using Unity.Entities;
using Unity.Collections;

/// <summary>
/// Builds the per-frame edge progress snapshot before any traversal or routing runs.
/// Stores it in the EdgeProgressCache singleton so PacketTraverseSystem and
/// MechanismSystem can perform physical entry checks without duplicating the build.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(PacketTraverseSystem))]
public partial struct EdgeProgressCacheSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        var entity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(entity, new EdgeProgressCache
        {
            Map = new NativeParallelMultiHashMap<Entity, float>(512, Allocator.Persistent)
        });
    }

    public void OnUpdate(ref SystemState state)
    {
        var cache = SystemAPI.GetSingletonRW<EdgeProgressCache>();
        cache.ValueRW.Map.Clear();

        // Include WaitingAtNode packets: physically stopped at edge.Length, block packets behind them.
        // Exclude AwaitingRouting packets: consumed by a mechanism, no longer on the edge.
        foreach (var packet in SystemAPI.Query<RefRO<Packet>>())
            cache.ValueRW.Map.Add(packet.ValueRO.CurrentEdge, packet.ValueRO.Progress);
    }

    public void OnDestroy(ref SystemState state)
    {
        SystemAPI.GetSingleton<EdgeProgressCache>().Map.Dispose();
    }
}
