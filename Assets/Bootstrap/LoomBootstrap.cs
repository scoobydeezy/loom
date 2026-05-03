using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using System.Collections.Generic;

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

        var topArch          = em.CreateArchetype(typeof(Node), typeof(NodeType), typeof(NodeTransform));
        var childArch        = em.CreateArchetype(typeof(Node), typeof(NodeTransform), typeof(NodeParent));
        var internalMechArch = em.CreateArchetype(typeof(Mechanism), typeof(MechanismType), typeof(NodeTransform), typeof(MechanismConnections), typeof(NodeParent));
        var edgeArch         = em.CreateArchetype(typeof(Edge));
        var packetArch       = em.CreateArchetype(typeof(Packet), typeof(PacketDestination));

        if (nodeAType == null || nodeBType == null || nodeCType == null)
        {
            Debug.LogError("[LoomBootstrap] All three NodeTypeDefinition slots must be assigned in the inspector.");
            return;
        }

        var resultA = SpawnNode(em, topArch, childArch, internalMechArch, edgeArch, nodeAType, new float3(-5, 0, 0), Entity.Null, id: 0);
        var resultB = SpawnNode(em, topArch, childArch, internalMechArch, edgeArch, nodeBType, new float3( 5, 0, 0), Entity.Null, id: 1);
        var resultC = SpawnNode(em, topArch, childArch, internalMechArch, edgeArch, nodeCType, new float3( 0, 0, 6), Entity.Null, id: 2);

        // A.Exits → B.Entry (direct — exit lanes are plain nodes, PacketTraverseSystem forwards them)
        Entity eAtoB = Entity.Null;
        foreach (var exit in resultA.ExitNodes)
        {
            var e = MakeEdge(em, edgeArch, exit, resultB.EntryNode, 5f, 50);
            if (eAtoB == Entity.Null) eAtoB = e;
        }

        // B.Exits → C.Entry
        foreach (var exit in resultB.ExitNodes)
            MakeEdge(em, edgeArch, exit, resultC.EntryNode, 5f, 15);

        // C.Exits → A.Entry
        foreach (var exit in resultC.ExitNodes)
            MakeEdge(em, edgeArch, exit, resultA.EntryNode, 5f, 50);

        // Packets start on the first A→B edge
        for (int i = 0; i < packetCount; i++)
        {
            Entity p = em.CreateEntity(packetArch);
            em.SetComponentData(p, new Packet
            {
                CurrentEdge = eAtoB,
                Progress    = UnityEngine.Random.Range(0f, 5f),
                Speed       = 2f
            });
            em.SetComponentData(p, new PacketDestination { Node = resultB.Node });
        }
    }

    // -------------------------------------------------------------------------
    // Recursive spawner
    // -------------------------------------------------------------------------

    class SpawnResult
    {
        public Entity   Node;
        public Entity   EntryNode;
        public Entity[] ExitNodes;
        public bool     IsMechanism;
    }

    SpawnResult SpawnNode(
        EntityManager em,
        EntityArchetype topArch, EntityArchetype childArch,
        EntityArchetype internalMechArch,
        EntityArchetype edgeArch,
        NodeTypeDefinition def, float3 position, Entity parent, int id = -1)
    {
        bool isTopLevel = parent == Entity.Null;
        Entity node = em.CreateEntity(isTopLevel ? topArch : childArch);
        em.SetComponentData(node, new Node { Id = id });
        em.SetComponentData(node, new NodeTransform { Position = position });

        if (isTopLevel)
            em.SetComponentData(node, new NodeType { TypeName = def.typeName });
        else
            em.SetComponentData(node, new NodeParent { Parent = parent });

        if (def.children == null || def.children.Length == 0)
            return new SpawnResult { Node = node, EntryNode = node, ExitNodes = new Entity[] { node } };

        int numGroups = def.children.Length;
        const float groupSpacing = 1.5f;
        const float nodeSpacing  = 0.8f;
        float totalHeight = (numGroups - 1) * groupSpacing;

        var groups = new List<List<SpawnResult>>(numGroups);

        for (int g = 0; g < numGroups; g++)
        {
            var childEntry = def.children[g];
            int count      = Mathf.Max(1, childEntry.count);
            float groupY   = position.y - totalHeight * 0.5f + g * groupSpacing;
            var group      = new List<SpawnResult>(count);

            for (int i = 0; i < count; i++)
            {
                float childX   = position.x + (i - (count - 1) * 0.5f) * nodeSpacing;
                var   childPos = new float3(childX, groupY, position.z);

                if (childEntry.childType == ChildType.Mechanism)
                {
                    Entity mech = em.CreateEntity(internalMechArch);
                    em.SetComponentData(mech, new NodeTransform { Position = childPos });
                    em.SetComponentData(mech, new MechanismType { Kind = childEntry.mechanismKind });
                    em.SetComponentData(mech, new NodeParent { Parent = node });
                    group.Add(new SpawnResult
                    {
                        Node        = mech,
                        EntryNode   = mech,
                        ExitNodes   = new Entity[] { mech },
                        IsMechanism = true
                    });
                }
                else
                {
                    if (childEntry.definition == null)
                    {
                        Debug.LogWarning($"[LoomBootstrap] '{def.typeName}' children[{g}] has no definition — skipping.");
                        continue;
                    }
                    group.Add(SpawnNode(em, topArch, childArch, internalMechArch, edgeArch,
                        childEntry.definition, childPos, node));
                }
            }

            groups.Add(group);
        }

        // Wire adjacent groups.
        // Mechanism sources: add all outbound edges to their MechanismConnections.
        // Node sources: edges are found by PacketTraverseSystem's outbound lookup.
        for (int g = 0; g < numGroups - 1; g++)
        {
            var src      = groups[g];
            var dst      = groups[g + 1];
            var srcEntry = def.children[g];

            float edgeLen = srcEntry.childType == ChildType.Node
                ? srcEntry.definition.internalPathLength
                : srcEntry.outEdgeLength;
            int edgeCap = srcEntry.childType == ChildType.Node
                ? srcEntry.definition.edgeCapacity
                : srcEntry.outEdgeCapacity;

            for (int s = 0; s < src.Count; s++)
            {
                for (int d = 0; d < dst.Count; d++)
                {
                    foreach (Entity srcExit in src[s].ExitNodes)
                    {
                        Entity e = MakeEdge(em, edgeArch, srcExit, dst[d].EntryNode, edgeLen, edgeCap);
                        if (em.HasComponent<Mechanism>(srcExit))
                            em.GetBuffer<MechanismConnections>(srcExit)
                              .Add(new MechanismConnections { Edge = e });
                    }
                }
            }
        }

        // Gather all exit entities from the last group
        var lastGroup = groups[numGroups - 1];
        var exitNodes = new List<Entity>(lastGroup.Count);
        foreach (var r in lastGroup)
            foreach (var e in r.ExitNodes)
                exitNodes.Add(e);

        return new SpawnResult
        {
            Node      = node,
            EntryNode = groups[0].Count > 0 ? groups[0][0].EntryNode : node,
            ExitNodes = exitNodes.Count > 0 ? exitNodes.ToArray() : new Entity[] { node },
        };
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    Entity MakeEdge(EntityManager em, EntityArchetype arch, Entity from, Entity to, float length, int capacity)
    {
        Entity edge = em.CreateEntity(arch);
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
