using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Recomputes Edge.Length every frame from the world-space positions of FromNode and ToNode.
/// Simulation latency = distance traveled; if a node moves, edge length must reflect the change.
/// Runs after WorldSpaceCacheSystem so endpoint world-space positions are fresh,
/// and before EdgeProgressCacheSystem / PacketTraverseSystem so traversal sees current lengths.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(WorldSpaceCacheSystem))]
[UpdateBefore(typeof(EdgeProgressCacheSystem))]
[UpdateBefore(typeof(PacketTraverseSystem))]
public partial struct EdgeLengthCacheSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var em = state.EntityManager;

        foreach (var edge in SystemAPI.Query<RefRW<Edge>>())
        {
            var e = edge.ValueRO;
            if (!em.Exists(e.FromNode) || !em.Exists(e.ToNode)) continue;
            if (!em.HasComponent<WorldSpaceTransform>(e.FromNode)) continue;
            if (!em.HasComponent<WorldSpaceTransform>(e.ToNode))   continue;

            float3 from = em.GetComponentData<WorldSpaceTransform>(e.FromNode).Position;
            float3 to   = em.GetComponentData<WorldSpaceTransform>(e.ToNode).Position;
            edge.ValueRW.Length = math.distance(from, to);
        }
    }
}
