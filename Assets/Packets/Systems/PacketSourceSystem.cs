using Unity.Entities;
using Unity.Collections;

/// <summary>
/// Emits packets from PacketSource nodes at their configured rate.
/// The accumulator tracks fractional packet debt: at EmitRate r, dt seconds add
/// r*dt to the accumulator, and a whole packet is emitted while it exceeds 1.
///
/// Emission is gated by the outbound edge's physical entry — if a packet sits
/// within BeadDiameter of the entry point, the accumulator continues to grow
/// but no packet is created until space clears. This is the spatial expression
/// of backpressure: a saturated source visibly stalls, never drops.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(PacketTraverseSystem))]
public partial struct PacketSourceSystem : ISystem
{
    EntityQuery _edgeQuery;

    public void OnCreate(ref SystemState state)
    {
        _edgeQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Edge>()
            .Build(ref state);
    }

    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        var   em = state.EntityManager;

        // Build node → single-outbound-edge lookup (same pattern as PacketTraverseSystem).
        var edgeEntities = _edgeQuery.ToEntityArray(Allocator.Temp);
        var outboundEdge = new NativeHashMap<Entity, Entity>(edgeEntities.Length, Allocator.Temp);
        for (int i = 0; i < edgeEntities.Length; i++)
        {
            Entity from = em.GetComponentData<Edge>(edgeEntities[i]).FromNode;
            if (!outboundEdge.ContainsKey(from))
                outboundEdge.Add(from, edgeEntities[i]);
        }
        edgeEntities.Dispose();

        var edgeProgressMap = SystemAPI.GetSingleton<EdgeProgressCache>().Map;

        // Collect sources first — TryEmitPacket performs structural changes (CreateEntity)
        // which would invalidate an active SystemAPI.Query iteration.
        var sourceEntities = new NativeList<Entity>(Allocator.Temp);
        foreach (var (_, entity) in SystemAPI
                 .Query<RefRO<PacketSource>>()
                 .WithEntityAccess())
        {
            sourceEntities.Add(entity);
        }

        for (int i = 0; i < sourceEntities.Length; i++)
        {
            Entity sourceEntity = sourceEntities[i];
            var    source       = em.GetComponentData<PacketSource>(sourceEntity);

            source.Accumulator += source.EmitRate * dt;

            while (source.Accumulator >= 1f)
            {
                if (!TryEmitPacket(em, edgeProgressMap, outboundEdge, sourceEntity, source))
                    break; // outbound blocked — accumulator continues to grow until space clears

                source.Accumulator -= 1f;
            }

            em.SetComponentData(sourceEntity, source);
        }

        sourceEntities.Dispose();
        outboundEdge.Dispose();
    }

    static bool TryEmitPacket(
        EntityManager em,
        NativeParallelMultiHashMap<Entity, float> edgeProgressMap,
        NativeHashMap<Entity, Entity> outboundEdge,
        Entity sourceEntity, PacketSource source)
    {
        if (!outboundEdge.TryGetValue(sourceEntity, out Entity outEdge))
            return false; // no outbound — nothing to emit onto

        // Physical entry check — same gate PacketTraverseSystem uses for plain-node forwarding.
        if (EdgeProgressUtil.MinProgressOnEdge(edgeProgressMap, outEdge) < PacketTraverseSystem.BeadDiameter)
            return false;

        Entity packet = em.CreateEntity(typeof(Packet), typeof(PacketSlot), typeof(StableId));
        StableIdAllocator.StampAndRegister(em, packet);
        em.SetComponentData(packet, new Packet
        {
            CurrentEdge = outEdge,
            Progress    = 0f,
            Speed       = 2f,
            Color       = source.Color,
            Shape       = source.Shape,
        });
        em.SetComponentData(packet, new PacketSlot { SlotIndex = -1 });
        return true;
    }
}
