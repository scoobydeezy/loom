using Unity.Entities;
using Unity.Collections;

public partial struct PacketTraverseSystem : ISystem
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

        // Pass 1 — move packets along edges and handle arrival at nodes/mechanisms
        foreach (var (packet, entity) in SystemAPI
                 .Query<RefRW<Packet>>()
                 .WithNone<AwaitingRouting>()
                 .WithNone<WaitingAtNode>()
                 .WithEntityAccess())
        {
            var p    = packet.ValueRW;
            var edge = em.GetComponentData<Edge>(p.CurrentEdge);

            p.Progress += p.Speed * dt;

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

            // Plain node: forward onto the single outbound edge if capacity allows
            bool forwarded = TryForward(em, outboundEdge, outboundCount, atNode, ref p);
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

            if (TryForward(em, outboundEdge, outboundCount, atNode, ref p))
            {
                packet.ValueRW = p;
                ecb.RemoveComponent<WaitingAtNode>(entity);
            }
            // Otherwise leave WaitingAtNode — retry next frame, no occupancy changes
        }

        outboundEdge.Dispose();
        outboundCount.Dispose();
        ecb.Playback(em);
        ecb.Dispose();
    }

    static bool TryForward(
        EntityManager em,
        NativeHashMap<Entity, Entity> outboundEdge,
        NativeHashMap<Entity, int>    outboundCount,
        Entity atNode, ref Packet p)
    {
        if (!outboundCount.TryGetValue(atNode, out int count) || count != 1)
            return false; // no edge or misconfiguration (count > 1 with no Mechanism)

        if (!outboundEdge.TryGetValue(atNode, out Entity nextEntity))
            return false;

        var nextEdge = em.GetComponentData<Edge>(nextEntity);
        if (nextEdge.Occupancy >= nextEdge.Capacity)
            return false;

        nextEdge.Occupancy++;
        em.SetComponentData(nextEntity, nextEdge);
        p.CurrentEdge = nextEntity;
        p.Progress    = 0f;
        return true;
    }
}
