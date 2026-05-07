using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PacketTraverseSystem))]
public partial struct MechanismSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        var em  = state.EntityManager;
        var edgeProgressMap = SystemAPI.GetSingleton<EdgeProgressCache>().Map;

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
                case MechanismKind.Filter:
                {
                    // TODO: Filter will add tail-drop admission control in Phase 4.
                    // For now both Route and Filter use the same physical-entry selection.
                    var connections = em.GetBuffer<MechanismConnections>(atNode, true);
                    if (connections.Length == 0)
                        break;

                    // Count physically clear candidates (entry unblocked within BeadDiameter)
                    int clearCount = 0;
                    for (int i = 0; i < connections.Length; i++)
                    {
                        float minProg = EdgeProgressUtil.MinProgressOnEdge(edgeProgressMap, connections[i].Edge);
                        if (minProg >= PacketTraverseSystem.BeadDiameter) clearCount++;
                    }

                    if (clearCount == 0) continue; // all blocked — packet waits, retry next frame

                    // Pick randomly among clear candidates to distribute load
                    int    pick     = UnityEngine.Random.Range(0, clearCount);
                    int    seen     = 0;
                    Entity selected = Entity.Null;
                    for (int i = 0; i < connections.Length; i++)
                    {
                        float minProg = EdgeProgressUtil.MinProgressOnEdge(edgeProgressMap, connections[i].Edge);
                        if (minProg >= PacketTraverseSystem.BeadDiameter)
                        {
                            if (seen == pick) { selected = connections[i].Edge; break; }
                            seen++;
                        }
                    }

                    p.CurrentEdge  = selected;
                    p.Progress     = 0f;
                    packet.ValueRW = p;
                    ecb.RemoveComponent<AwaitingRouting>(entity);
                    break;
                }
                case MechanismKind.RateLimit:
                    // TODO: Implement rate-limit token bucket (Phase 4).
                    break;
            }
        }

        ecb.Playback(em);
        ecb.Dispose();
    }
}
