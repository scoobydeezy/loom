using Unity.Burst;
using Unity.Entities;

partial struct PacketMoveSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        foreach (var (packet, entity) in SystemAPI.Query<RefRW<Packet>>().WithEntityAccess())
        {
            var p = packet.ValueRW;

            p.Progress += p.Speed * dt;

            // If reached end of edge, loop back
            if (p.Progress >= 10f)
            {
                p.Progress = 0f;
            }

            packet.ValueRW = p;
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        
    }
}
