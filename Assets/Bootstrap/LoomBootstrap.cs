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
        var internalEdgeArch = em.CreateArchetype(typeof(Edge), typeof(InternalEdgeTag));
        var packetArch = em.CreateArchetype(typeof(Packet));

        // --- Create Nodes ---
        Entity A = CreateNode(em, nodeArch, internalEdgeArch, new float3(-5, 0, 0));
        Entity B = CreateNode(em, nodeArch, internalEdgeArch, new float3(5, 0, 0));
        Entity C = CreateNode(em, nodeArch, internalEdgeArch, new float3(0, 0, 6));

        // --- Create External Edges ---
        Entity AB = CreateEdge(em, edgeArch, A, B, 10f, 50);
        Entity BC = CreateEdge(em, edgeArch, B, C, 10f, 15);
        Entity CA = CreateEdge(em, edgeArch, C, A, 10f, 50);

        // --- Tell nodes which edges they connect to ---
        em.SetComponentData(A, new NodeEdges { EdgeA = AB, EdgeB = CA });
        em.SetComponentData(B, new NodeEdges { EdgeA = AB, EdgeB = BC });
        em.SetComponentData(C, new NodeEdges { EdgeA = BC, EdgeB = CA });

        // --- Spawn Packets ---
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

    Entity CreateNode(EntityManager em, EntityArchetype nodeArch, EntityArchetype internalEdgeArch, float3 pos)
    {
        Entity node = em.CreateEntity(nodeArch);

        // Create the node's internal edge (belt inside machine)
        Entity internalEdge = em.CreateEntity(internalEdgeArch);

        em.SetComponentData(internalEdge, new Edge
        {
            FromNode = node,
            ToNode = node,
            Length = 5f,     // visible processing distance
            Capacity = 1000,
            Occupancy = 0
        });

        em.SetComponentData(node, new Node
        {
            InternalEdge = internalEdge
        });

        em.SetComponentData(node, new NodeTransform { Position = pos });

        return node;
    }

    Entity CreateEdge(EntityManager em, EntityArchetype edgeArch, Entity from, Entity to, float length, int capacity)
    {
        Entity edge = em.CreateEntity(edgeArch);

        em.SetComponentData(edge, new Edge
        {
            FromNode = from,
            ToNode = to,
            Length = length,
            Capacity = capacity,
            Occupancy = 0
        });

        return edge;
    }
}