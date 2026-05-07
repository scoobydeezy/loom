using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(EdgeProgressCacheSystem))]
public partial struct PacketTraverseSystem : ISystem
{
    // Entry-spacing constant: a new packet can enter an edge only when the nearest existing
    // packet has cleared at least this distance from the entry point. Controls throughput.
    // Must match the visual bead scale used in PacketVisualizer.
    public const float BeadDiameter = 0.2f;

    EntityQuery _edgeQuery;

    public void OnCreate(ref SystemState state)
    {
        _edgeQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Edge>()
            .Build(ref state);
    }

    public void OnUpdate(ref SystemState state)
    {
        float dt  = SystemAPI.Time.DeltaTime;
        var   ecb = new EntityCommandBuffer(Allocator.Temp);
        var   em  = state.EntityManager;

        // Build node → single-outbound-edge lookup from all Edge entities.
        // Only used for plain nodes (count == 1). Nodes with count > 1 and no Mechanism
        // are a misconfiguration — packets pile up there as correct emergent backpressure.
        var edgeEntities  = _edgeQuery.ToEntityArray(Allocator.Temp);
        var outboundEdge  = new NativeHashMap<Entity, Entity>(edgeEntities.Length, Allocator.Temp);
        var outboundCount = new NativeHashMap<Entity, int>(edgeEntities.Length, Allocator.Temp);

        for (int i = 0; i < edgeEntities.Length; i++)
        {
            Entity from = em.GetComponentData<Edge>(edgeEntities[i]).FromNode;
            if (!outboundCount.TryGetValue(from, out int cnt))
            {
                outboundCount.Add(from, 1);
                outboundEdge.Add(from, edgeEntities[i]);
            }
            else
            {
                outboundCount[from] = cnt + 1;
            }
        }
        edgeEntities.Dispose();

        var edgeProgressMap = SystemAPI.GetSingleton<EdgeProgressCache>().Map;

        // Pass 1 — move packets along edges and handle arrival at nodes/mechanisms
        foreach (var (packet, entity) in SystemAPI
                 .Query<RefRW<Packet>>()
                 .WithNone<AwaitingRouting>()
                 .WithNone<WaitingAtNode>()
                 .WithEntityAccess())
        {
            var p    = packet.ValueRW;
            var edge = em.GetComponentData<Edge>(p.CurrentEdge);

            float desiredProgress = p.Progress + p.Speed * dt;
            float minAhead        = EdgeProgressUtil.MinProgressGreaterThan(edgeProgressMap, p.CurrentEdge, p.Progress);
            float maxAllowed      = minAhead - BeadDiameter;
            p.Progress = math.max(p.Progress, math.min(desiredProgress, maxAllowed));

            if (p.Progress < edge.Length)
            {
                packet.ValueRW = p;
                continue;
            }

            p.Progress = edge.Length;
            Entity atNode = edge.ToNode;

            // Mechanism: hand off routing to MechanismSystem
            if (em.HasComponent<Mechanism>(atNode))
            {
                packet.ValueRW = p;
                ecb.AddComponent<AwaitingRouting>(entity);
                continue;
            }

            // Plain node: forward onto the single outbound edge if physically unblocked
            bool forwarded = TryForward(outboundEdge, outboundCount, edgeProgressMap, atNode, ref p);
            packet.ValueRW = p;

            if (!forwarded)
            {
                // Packet has left the inbound edge. Stamp WaitingAtNode and retry next frame.
                ecb.AddComponent<WaitingAtNode>(entity);
            }
        }

        // Pass 2 — retry forwarding for packets waiting at plain nodes
        foreach (var (packet, entity) in SystemAPI
                 .Query<RefRW<Packet>>()
                 .WithAll<WaitingAtNode>()
                 .WithEntityAccess())
        {
            var p     = packet.ValueRW;
            var edge  = em.GetComponentData<Edge>(p.CurrentEdge);
            Entity atNode = edge.ToNode;

            if (TryForward(outboundEdge, outboundCount, edgeProgressMap, atNode, ref p))
            {
                packet.ValueRW = p;
                ecb.RemoveComponent<WaitingAtNode>(entity);
            }
            // Otherwise leave WaitingAtNode — retry next frame
        }

        outboundEdge.Dispose();
        outboundCount.Dispose();
        ecb.Playback(em);
        ecb.Dispose();
    }

    static bool TryForward(
        NativeHashMap<Entity, Entity> outboundEdge,
        NativeHashMap<Entity, int>    outboundCount,
        NativeParallelMultiHashMap<Entity, float> edgeProgressMap,
        Entity atNode, ref Packet p)
    {
        if (!outboundCount.TryGetValue(atNode, out int count) || count != 1)
            return false; // no edge or misconfiguration (count > 1 with no Mechanism)

        if (!outboundEdge.TryGetValue(atNode, out Entity nextEntity))
            return false;

        if (EdgeProgressUtil.MinProgressOnEdge(edgeProgressMap, nextEntity) < BeadDiameter)
            return false;

        p.CurrentEdge = nextEntity;
        p.Progress    = 0f;
        return true;
    }
}
