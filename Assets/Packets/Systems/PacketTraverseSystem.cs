using Unity.Burst;
using Unity.Entities;

[BurstCompile]
public partial struct PacketTraverseSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        var em = state.EntityManager;

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

            // Release occupancy from the completed edge
            edge.Occupancy--;
            em.SetComponentData(p.CurrentEdge, edge);

            // Advance to the next edge in the route
            var route     = em.GetBuffer<PacketRoute>(entity);
            int nextIndex = (p.PathIndex + 1) % route.Length;
            Entity next   = route[nextIndex].Edge;
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
                // Next edge full — hold at end of current edge
                edge.Occupancy++;
                em.SetComponentData(p.CurrentEdge, edge);
                p.Progress = edge.Length;
            }

            packet.ValueRW = p;
        }
    }
}
