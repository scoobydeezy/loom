using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;

public class LoomBootstrap : MonoBehaviour
{
    public int packetCount = 1000;

    [Header("Node Type Assignments")]
    public NodeTypeDefinition nodeAType;
    public NodeTypeDefinition nodeBType;
    public NodeTypeDefinition nodeCType;

    void Start()
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;

        var nodeArch = em.CreateArchetype(
            typeof(Node),
            typeof(NodeType),
            typeof(NodeLayout),
            typeof(NodeLane),
            typeof(NodeEdges),
            typeof(NodeTransform)
        );

        var edgeArch         = em.CreateArchetype(typeof(Edge));
        var internalEdgeArch = em.CreateArchetype(typeof(Edge), typeof(InternalEdge));
        var packetArch       = em.CreateArchetype(typeof(Packet));

        Entity A = CreateNode(em, nodeArch, internalEdgeArch, nodeAType, new float3(-5, 0, 0), id: 0);
        Entity B = CreateNode(em, nodeArch, internalEdgeArch, nodeBType, new float3( 5, 0, 0), id: 1);
        Entity C = CreateNode(em, nodeArch, internalEdgeArch, nodeCType, new float3( 0, 0, 6), id: 2);

        Entity AB = CreateEdge(em, edgeArch, A, B, 10f, 50);
        Entity BC = CreateEdge(em, edgeArch, B, C, 10f, 15);
        Entity CA = CreateEdge(em, edgeArch, C, A, 10f, 50);

        em.SetComponentData(A, new NodeEdges { EdgeA = AB, EdgeB = CA });
        em.SetComponentData(B, new NodeEdges { EdgeA = AB, EdgeB = BC });
        em.SetComponentData(C, new NodeEdges { EdgeA = BC, EdgeB = CA });

        for (int i = 0; i < packetCount; i++)
        {
            Entity p = em.CreateEntity(packetArch);

            em.SetComponentData(p, new Packet
            {
                CurrentEdge = AB,
                PathIndex   = 0,
                Progress    = UnityEngine.Random.Range(0f, 10f),
                Speed       = 2f
            });

            var buffer = em.AddBuffer<PacketRoute>(p);
            buffer.Add(new PacketRoute { Edge = AB });
            buffer.Add(new PacketRoute { Edge = BC });
            buffer.Add(new PacketRoute { Edge = CA });
        }
    }

    Entity CreateNode(EntityManager em, EntityArchetype nodeArch, EntityArchetype internalEdgeArch,
                      NodeTypeDefinition typeDef, float3 pos, int id)
    {
        if (typeDef == null)
        {
            Debug.LogWarning($"[LoomBootstrap] Node {id} has no NodeTypeDefinition assigned — " +
                             "drag an asset from Nodes/NodeTypes/ onto the Bootstrap inspector fields. " +
                             "Using fallback defaults.");
            typeDef = ScriptableObject.CreateInstance<NodeTypeDefinition>();
            typeDef.typeName           = "Unknown";
            typeDef.laneCount          = 4;
            typeDef.queueCapacity      = 64;
            typeDef.internalPathLength = 3f;
            typeDef.exitCapacity       = 4;
        }

        Entity node = em.CreateEntity(nodeArch);
        em.SetComponentData(node, new Node { Id = id });
        em.SetComponentData(node, new NodeType { TypeName = typeDef.typeName });
        em.SetComponentData(node, new NodeTransform { Position = pos });

        // Entry edge: queue before workers
        Entity entryEdge = em.CreateEntity(internalEdgeArch);
        em.SetComponentData(entryEdge, new Edge
        {
            FromNode = node, ToNode = node,
            Length   = 1f,
            Capacity = typeDef.queueCapacity,
            Occupancy = 0
        });
        em.SetComponentData(entryEdge, new InternalEdge { Role = InternalEdgeRole.Entry, LaneIndex = 0 });

        // Lane edges: one per worker — collect entities before writing to the node buffer
        // to avoid chunk reallocation invalidating the DynamicBuffer reference mid-loop
        var laneEntities = new NativeArray<Entity>(typeDef.laneCount, Allocator.Temp);
        for (int i = 0; i < typeDef.laneCount; i++)
        {
            laneEntities[i] = em.CreateEntity(internalEdgeArch);
            em.SetComponentData(laneEntities[i], new Edge
            {
                FromNode  = node, ToNode = node,
                Length    = typeDef.internalPathLength,
                Capacity  = 1,
                Occupancy = 0
            });
            em.SetComponentData(laneEntities[i], new InternalEdge { Role = InternalEdgeRole.Lane, LaneIndex = i });
        }

        // Exit edge: dispatch staging
        Entity exitEdge = em.CreateEntity(internalEdgeArch);
        em.SetComponentData(exitEdge, new Edge
        {
            FromNode  = node, ToNode = node,
            Length    = 1f,
            Capacity  = typeDef.exitCapacity,
            Occupancy = 0
        });
        em.SetComponentData(exitEdge, new InternalEdge { Role = InternalEdgeRole.Exit, LaneIndex = 0 });

        // All structural changes done — safe to get the buffer and write lane refs
        var laneBuf = em.GetBuffer<NodeLane>(node);
        for (int i = 0; i < typeDef.laneCount; i++)
            laneBuf.Add(new NodeLane { Edge = laneEntities[i] });
        laneEntities.Dispose();

        em.SetComponentData(node, new NodeLayout
        {
            LaneCount = typeDef.laneCount,
            EntryEdge = entryEdge,
            ExitEdge  = exitEdge
        });

        return node;
    }

    Entity CreateEdge(EntityManager em, EntityArchetype edgeArch, Entity from, Entity to, float length, int capacity)
    {
        Entity edge = em.CreateEntity(edgeArch);
        em.SetComponentData(edge, new Edge
        {
            FromNode  = from, ToNode = to,
            Length    = length,
            Capacity  = capacity,
            Occupancy = 0
        });
        return edge;
    }
}
