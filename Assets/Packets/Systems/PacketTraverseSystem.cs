using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

public partial struct PacketTraverseSystem : ISystem
{
    // Minimum following distance. A packet stops when it would get closer than this to the
    // packet immediately ahead on the same edge. Must match PacketVisualizer.BeadDiameter.
    public const float BeadDiameter = 0.15f;

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

        // Per-edge progress snapshot for this frame's collision checks.
        // WaitingAtNode packets are included: they are physically stopped at edge.Length
        // and must block packets approaching from behind on the same edge.
        // AwaitingRouting packets are excluded: they have been consumed by a mechanism
        // and no longer occupy space on the edge.
        var edgeProgressMap = new NativeParallelMultiHashMap<Entity, float>(512, Allocator.Temp);
        foreach (var packet in SystemAPI.Query<RefRO<Packet>>().WithNone<AwaitingRouting>())
            edgeProgressMap.Add(packet.ValueRO.CurrentEdge, packet.ValueRO.Progress);

        // Pass 1 — move packets along edges and handle arrival at nodes/mechanisms
        foreach (var (packet, entity) in SystemAPI
                 .Query<RefRW<Packet>>()
                 .WithNone<AwaitingRouting>()
                 .WithNone<WaitingAtNode>()
                 .WithEntityAccess())
        {
            var p    = packet.ValueRW;
            var edge = em.GetComponentData<Edge>(p.CurrentEdge);

            // Advance up to BeadDiameter behind the nearest packet ahead on this edge.
            float desiredProgress = p.Progress + p.Speed * dt;
            float minAhead        = MinProgressGreaterThan(edgeProgressMap, p.CurrentEdge, p.Progress);
            float maxAllowed      = minAhead - BeadDiameter;
            p.Progress = math.max(p.Progress, math.min(desiredProgress, maxAllowed));

            if (p.Progress < edge.Length)
            {
                packet.ValueRW = p;
                continue;
            }

            // Edge complete — release occupancy
            edge.Occupancy--;
            em.SetComponentData(p.CurrentEdge, edge);
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
            bool forwarded = TryForward(em, outboundEdge, outboundCount, edgeProgressMap, atNode, ref p);
            packet.ValueRW = p;

            if (!forwarded)
            {
                // Packet has left the inbound edge (occupancy already decremented).
                // Do not restore occupancy — stamp WaitingAtNode and retry next frame.
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

            if (TryForward(em, outboundEdge, outboundCount, edgeProgressMap, atNode, ref p))
            {
                packet.ValueRW = p;
                ecb.RemoveComponent<WaitingAtNode>(entity);
            }
            // Otherwise leave WaitingAtNode — retry next frame, no occupancy changes
        }

        outboundEdge.Dispose();
        outboundCount.Dispose();
        edgeProgressMap.Dispose();
        ecb.Playback(em);
        ecb.Dispose();
    }

    static bool TryForward(
        EntityManager em,
        NativeHashMap<Entity, Entity> outboundEdge,
        NativeHashMap<Entity, int>    outboundCount,
        NativeParallelMultiHashMap<Entity, float> edgeProgressMap,
        Entity atNode, ref Packet p)
    {
        if (!outboundCount.TryGetValue(atNode, out int count) || count != 1)
            return false; // no edge or misconfiguration (count > 1 with no Mechanism)

        if (!outboundEdge.TryGetValue(atNode, out Entity nextEntity))
            return false;

        // Physical entry check: blocked if a stopped packet is already within BeadDiameter
        // of the edge start. This replaces the old Occupancy >= Capacity hard gate.
        float minProg = MinProgressOnEdge(edgeProgressMap, nextEntity);
        if (minProg < BeadDiameter)
            return false;

        var nextEdge = em.GetComponentData<Edge>(nextEntity);
        nextEdge.Occupancy++;
        em.SetComponentData(nextEntity, nextEdge);
        p.CurrentEdge = nextEntity;
        p.Progress    = 0f;
        return true;
    }

    // Smallest progress value on the edge that is strictly greater than minExclusive.
    // Returns float.MaxValue when no packet is ahead (edge is clear).
    static float MinProgressGreaterThan(
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

    // Smallest progress value of any packet on the edge. Returns float.MaxValue when empty.
    static float MinProgressOnEdge(NativeParallelMultiHashMap<Entity, float> map, Entity edge)
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
