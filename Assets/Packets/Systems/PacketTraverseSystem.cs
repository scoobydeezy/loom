using Unity.Burst;
using Unity.Entities;

[BurstCompile]
public partial struct PacketTraverseSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        var em = state.EntityManager;

        var layoutLookup       = SystemAPI.GetComponentLookup<NodeLayout>(true);
        var internalEdgeLookup = SystemAPI.GetComponentLookup<InternalEdge>(true);
        var nodeLaneLookup     = SystemAPI.GetBufferLookup<NodeLane>(true);

        foreach (var (packet, entity) in SystemAPI
                 .Query<RefRW<Packet>>()
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

            // Release occupancy from the finished edge
            edge.Occupancy--;
            em.SetComponentData(p.CurrentEdge, edge);

            bool isInternal = internalEdgeLookup.HasComponent(p.CurrentEdge);

            if (!isInternal)
            {
                // Finished external edge → enter destination node's entry queue
                var layout   = layoutLookup[edge.ToNode];
                var entryData = em.GetComponentData<Edge>(layout.EntryEdge);

                if (entryData.Occupancy < entryData.Capacity)
                {
                    entryData.Occupancy++;
                    em.SetComponentData(layout.EntryEdge, entryData);
                    p.CurrentEdge = layout.EntryEdge;
                    p.Progress    = 0f;
                }
                else
                {
                    // Entry queue full — hold at end of external edge
                    edge.Occupancy++;
                    em.SetComponentData(p.CurrentEdge, edge);
                    p.Progress = edge.Length;
                }
            }
            else
            {
                var internalData = internalEdgeLookup[p.CurrentEdge];
                Entity ownerNode = edge.ToNode;

                switch (internalData.Role)
                {
                    case InternalEdgeRole.Entry:
                    {
                        // Pick the least-occupied lane that still has capacity
                        var lanes    = nodeLaneLookup[ownerNode];
                        Entity best  = Entity.Null;
                        int minOcc   = int.MaxValue;

                        for (int i = 0; i < lanes.Length; i++)
                        {
                            var ld = em.GetComponentData<Edge>(lanes[i].Edge);
                            if (ld.Occupancy < ld.Capacity && ld.Occupancy < minOcc)
                            {
                                minOcc = ld.Occupancy;
                                best   = lanes[i].Edge;
                            }
                        }

                        if (best != Entity.Null)
                        {
                            var ld = em.GetComponentData<Edge>(best);
                            ld.Occupancy++;
                            em.SetComponentData(best, ld);
                            p.CurrentEdge = best;
                            p.Progress    = 0f;
                        }
                        else
                        {
                            // All workers busy — hold at end of entry edge
                            edge.Occupancy++;
                            em.SetComponentData(p.CurrentEdge, edge);
                            p.Progress = edge.Length;
                        }
                        break;
                    }

                    case InternalEdgeRole.Lane:
                    {
                        // Finished processing → enter exit staging edge
                        var layout   = layoutLookup[ownerNode];
                        var exitData = em.GetComponentData<Edge>(layout.ExitEdge);

                        if (exitData.Occupancy < exitData.Capacity)
                        {
                            exitData.Occupancy++;
                            em.SetComponentData(layout.ExitEdge, exitData);
                            p.CurrentEdge = layout.ExitEdge;
                            p.Progress    = 0f;
                        }
                        else
                        {
                            // Exit full — hold at end of lane
                            edge.Occupancy++;
                            em.SetComponentData(p.CurrentEdge, edge);
                            p.Progress = edge.Length;
                        }
                        break;
                    }

                    case InternalEdgeRole.Exit:
                    {
                        // Finished exit staging → advance to next external edge in route
                        var path      = em.GetBuffer<PacketRoute>(entity);
                        int nextIndex = (p.PathIndex + 1) % path.Length;
                        Entity next   = path[nextIndex].Edge;
                        var nextEdge  = em.GetComponentData<Edge>(next);

                        if (nextEdge.Occupancy < nextEdge.Capacity)
                        {
                            nextEdge.Occupancy++;
                            em.SetComponentData(next, nextEdge);
                            p.CurrentEdge = next;
                            p.PathIndex   = nextIndex;
                            p.Progress    = 0f;
                        }
                        else
                        {
                            // External edge full — hold at end of exit edge
                            edge.Occupancy++;
                            em.SetComponentData(p.CurrentEdge, edge);
                            p.Progress = edge.Length;
                        }
                        break;
                    }
                }
            }

            packet.ValueRW = p;
        }
    }
}
