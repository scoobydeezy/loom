using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

public class LoomBootstrap : MonoBehaviour
{
    public int packetCount = 1000;

    void Start()
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;

        var nodeArch = em.CreateArchetype(
            typeof(Node),
            typeof(NodeEdges),
            typeof(NodeTransform)
        );
        var edgeArch = em.CreateArchetype(typeof(Edge));
        var packetArch = em.CreateArchetype(typeof(Packet));

        // Create nodes A, B, C
        Entity A = em.CreateEntity(nodeArch);
        Entity B = em.CreateEntity(nodeArch);
        Entity C = em.CreateEntity(nodeArch);

        // Create edges AB, BC, CA
        Entity AB = em.CreateEntity(edgeArch);
        Entity BC = em.CreateEntity(edgeArch);
        Entity CA = em.CreateEntity(edgeArch);

        em.SetComponentData(AB, new Edge {
            FromNode = A,
            ToNode = B,
            Length = 10f,
            Capacity = 50,
            Occupancy = 0
        });
        em.SetComponentData(BC, new Edge {
            FromNode = B,
            ToNode = C,
            Length = 10f,
            Capacity = 15,
            Occupancy = 0
        });
        em.SetComponentData(CA, new Edge {
            FromNode = C,
            ToNode = A,
            Length = 10f,
            Capacity = 50,
            Occupancy = 0
        });

        // Tell nodes which edges they have
        em.SetComponentData(A, new NodeEdges { EdgeA = AB, EdgeB = CA });
        em.SetComponentData(B, new NodeEdges { EdgeA = AB, EdgeB = BC });
        em.SetComponentData(C, new NodeEdges { EdgeA = BC, EdgeB = CA });

        em.SetComponentData(A, new NodeTransform { Position = new float3(-5, 0, 0) });
        em.SetComponentData(B, new NodeTransform { Position = new float3(5, 0, 0) });
        em.SetComponentData(C, new NodeTransform { Position = new float3(0, 0, 6) });

        // Spawn packets starting on AB going to B
        for (int i = 0; i < packetCount; i++)
        {
            Entity p = em.CreateEntity(packetArch);
            em.SetComponentData(p, new Packet
            {
                CurrentEdge = AB,
                PathIndex = 0,
                Progress = UnityEngine.Random.Range(0f, 10f),
                Speed = 2f
            });

            var buffer = em.AddBuffer<PacketRoute>(p);
            buffer.Add(new PacketRoute { Edge = AB });
            buffer.Add(new PacketRoute { Edge = BC });
            buffer.Add(new PacketRoute { Edge = CA });
        }
    }
}