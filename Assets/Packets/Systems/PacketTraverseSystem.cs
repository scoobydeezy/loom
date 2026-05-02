using Unity.Burst;
using Unity.Entities;

[BurstCompile]
public partial struct PacketTraverseSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        var em = state.EntityManager;

        var edgeLookup = SystemAPI.GetComponentLookup<Edge>(true);
        var nodeLookup = SystemAPI.GetComponentLookup<Node>(true);
        var internalTagLookup = SystemAPI.GetComponentLookup<InternalEdgeTag>(true);

        foreach (var (packet, entity) in SystemAPI
                 .Query<RefRW<Packet>>()
                 .WithEntityAccess())
        {
            var p = packet.ValueRW;
            var edge = em.GetComponentData<Edge>(p.CurrentEdge);

            p.Progress += p.Speed * dt;

            if (p.Progress >= edge.Length)
            {
                bool isInternal = internalTagLookup.HasComponent(p.CurrentEdge);

                // Release occupancy from current edge
                edge.Occupancy--;
                em.SetComponentData(p.CurrentEdge, edge);

                if (!isInternal)
                {
                    // --- Finished EXTERNAL edge → go to node's internal edge ---

                    // Determine which node we arrived at
                    Entity arrivedNode = edge.ToNode;

                    var node = nodeLookup[arrivedNode];
                    Entity internalEdge = node.InternalEdge;

                    var nextEdge = em.GetComponentData<Edge>(internalEdge);

                    if (nextEdge.Occupancy < nextEdge.Capacity)
                    {
                        nextEdge.Occupancy++;
                        em.SetComponentData(internalEdge, nextEdge);

                        p.CurrentEdge = internalEdge;
                        p.Progress = 0f;
                    }
                    else
                    {
                        // Internal lane full — wait at end of external edge
                        p.Progress = edge.Length;
                        edge.Occupancy++;
                        em.SetComponentData(p.CurrentEdge, edge);
                    }
                }
                else
                {
                    // --- Finished INTERNAL edge → go to next external edge in route ---

                    var path = em.GetBuffer<PacketRoute>(entity);
                    int nextIndex = (p.PathIndex + 1) % path.Length;
                    Entity nextEdgeEntity = path[nextIndex].Edge;

                    var nextEdge = em.GetComponentData<Edge>(nextEdgeEntity);

                    if (nextEdge.Occupancy < nextEdge.Capacity)
                    {
                        nextEdge.Occupancy++;
                        em.SetComponentData(nextEdgeEntity, nextEdge);

                        p.CurrentEdge = nextEdgeEntity;
                        p.PathIndex = nextIndex;
                        p.Progress = 0f;
                    }
                    else
                    {
                        // Next edge full — wait at end of internal edge
                        p.Progress = edge.Length;
                        edge.Occupancy++;
                        em.SetComponentData(p.CurrentEdge, edge);
                    }
                }
            }

            packet.ValueRW = p;
        }
    }
}