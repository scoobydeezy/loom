using Unity.Entities;

[UpdateAfter(typeof(PacketTraverseSystem))]
public partial struct MechanismSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        var em  = state.EntityManager;

        foreach (var (packet, entity) in SystemAPI
                 .Query<RefRW<Packet>>()
                 .WithAll<AwaitingRouting>()
                 .WithEntityAccess())
        {
            var p     = packet.ValueRW;
            var edge  = em.GetComponentData<Edge>(p.CurrentEdge);
            Entity atNode = edge.ToNode;

            // Only Mechanism entities are processed here — plain nodes are handled by PacketTraverseSystem
            if (!em.HasComponent<Mechanism>(atNode))
                continue;

            if (!em.HasBuffer<MechanismConnections>(atNode))
                continue;

            var kind = em.GetComponentData<MechanismType>(atNode).Kind;
            switch (kind)
            {
                case MechanismKind.Route:
                {
                    var connections = em.GetBuffer<MechanismConnections>(atNode, true);

                    // Pass 1 — find best headroom
                    int bestRoom = 0;
                    for (int i = 0; i < connections.Length; i++)
                    {
                        var next     = em.GetComponentData<Edge>(connections[i].Edge);
                        int headroom = next.Capacity - next.Occupancy;
                        if (headroom > bestRoom) bestRoom = headroom;
                    }

                    if (bestRoom == 0) continue; // all saturated — packet waits

                    // Pass 2 — count ties, pick randomly among them
                    int tieCount = 0;
                    for (int i = 0; i < connections.Length; i++)
                    {
                        var next = em.GetComponentData<Edge>(connections[i].Edge);
                        if (next.Capacity - next.Occupancy == bestRoom) tieCount++;
                    }

                    int    pick     = UnityEngine.Random.Range(0, tieCount);
                    int    seen     = 0;
                    Entity selected = Entity.Null;
                    for (int i = 0; i < connections.Length; i++)
                    {
                        var next = em.GetComponentData<Edge>(connections[i].Edge);
                        if (next.Capacity - next.Occupancy == bestRoom)
                        {
                            if (seen == pick) { selected = connections[i].Edge; break; }
                            seen++;
                        }
                    }

                    var sel = em.GetComponentData<Edge>(selected);
                    sel.Occupancy++;
                    em.SetComponentData(selected, sel);

                    p.CurrentEdge  = selected;
                    p.Progress     = 0f;
                    packet.ValueRW = p;
                    ecb.RemoveComponent<AwaitingRouting>(entity);
                    break;
                }
                case MechanismKind.Filter:
                case MechanismKind.RateLimit:
                    // stub — no behavior yet
                    break;
            }
        }

        ecb.Playback(em);
        ecb.Dispose();
    }
}
