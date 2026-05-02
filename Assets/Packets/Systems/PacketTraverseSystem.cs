using Unity.Burst;
using Unity.Entities;

[BurstCompile]
public partial struct PacketTraverseSystem : ISystem
{
    public readonly void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        var em = state.EntityManager;

        foreach (var (packet, entity) in SystemAPI
                 .Query<RefRW<Packet>>()
                 .WithEntityAccess())
        {
            var p = packet.ValueRW;
            var edge = em.GetComponentData<Edge>(p.CurrentEdge);

            p.Progress += p.Speed * dt;

            if (p.Progress >= edge.Length)
            {
                var path = em.GetBuffer<PacketRoute>(entity);
                int nextIndex = (p.PathIndex + 1) % path.Length;
                Entity nextEdgeEntity = path[nextIndex].Edge;

                var nextEdge = em.GetComponentData<Edge>(nextEdgeEntity);

                // Try to enter next edge
                if (nextEdge.Occupancy < nextEdge.Capacity)
                {
                    // Leave current edge
                    edge.Occupancy--;
                    em.SetComponentData(p.CurrentEdge, edge);

                    // Enter next edge
                    nextEdge.Occupancy++;
                    em.SetComponentData(nextEdgeEntity, nextEdge);

                    p.CurrentEdge = nextEdgeEntity;
                    p.PathIndex = nextIndex;
                    p.Progress = 0f;
                }
                else
                {
                    // Edge full — stay put
                    p.Progress = edge.Length;
                }
            }

            packet.ValueRW = p;
        }
    }
}