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
        var packetArch       = em.CreateArchetype(typeof(Packet), typeof(PacketDestination), typeof(PacketSlot));

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
            var e = MakeEdge(em, edgeArch, exit, resultB.EntryNode);
            if (eAtoB == Entity.Null) eAtoB = e;
        }

        // B.Exits → C.Entry
        foreach (var exit in resultB.ExitNodes)
            MakeEdge(em, edgeArch, exit, resultC.EntryNode);

        // C.Exits → A.Entry
        foreach (var exit in resultC.ExitNodes)
            MakeEdge(em, edgeArch, exit, resultA.EntryNode);

        // Packets start on the first A→B edge
        float eatoBLength = em.GetComponentData<Edge>(eAtoB).Length;
        for (int i = 0; i < packetCount; i++)
        {
            Entity p = em.CreateEntity(packetArch);
            em.SetComponentData(p, new Packet
            {
                CurrentEdge = eAtoB,
                Progress    = UnityEngine.Random.Range(0f, eatoBLength),
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

        // laneCount = max child count across groups; sets the vertical extent of the frame.
        int laneCount = 1;
        for (int g = 0; g < numGroups; g++)
            laneCount = math.max(laneCount, math.max(1, def.children[g].count));

        float entryWallX  = position.x - def.frameWidth * 0.5f;
        float exitWallX   = position.x + def.frameWidth * 0.5f;
        float totalHeight = laneCount * def.frameHeight;
        float topY        = position.y + totalHeight * 0.5f;

        var groups = new List<List<SpawnResult>>(numGroups);

        for (int g = 0; g < numGroups; g++)
        {
            var childEntry = def.children[g];
            int count      = Mathf.Max(1, childEntry.count);
            var group      = new List<SpawnResult>(count);

            bool isFirstGroup = (g == 0);
            bool isLastGroup  = (g == numGroups - 1);

            // Group X — entry wall, exit wall, or evenly distributed between.
            // For numGroups == 1 the single group is both entry and exit; place at the exit wall
            // so external lines arriving from the right terminate cleanly. The entry wall is then
            // visually collapsed onto the exit wall, which the visualizer handles gracefully.
            float groupX;
            if (numGroups == 1)        groupX = exitWallX;
            else if (isFirstGroup)     groupX = entryWallX;
            else if (isLastGroup)      groupX = exitWallX;
            else                       groupX = math.lerp(entryWallX, exitWallX, (float)g / (numGroups - 1));

            for (int i = 0; i < count; i++)
            {
                // Distribute children vertically across totalHeight, centered per lane.
                float childY   = (count == 1)
                    ? position.y
                    : topY - (i + 0.5f) * (totalHeight / count);
                var   childPos = new float3(groupX, childY, position.z);

                SpawnResult result;
                if (childEntry.childType == ChildType.Mechanism)
                {
                    Entity mech = em.CreateEntity(internalMechArch);
                    em.SetComponentData(mech, new NodeTransform { Position = childPos });
                    em.SetComponentData(mech, new MechanismType { Kind = childEntry.mechanismKind });
                    em.SetComponentData(mech, new NodeParent { Parent = node });
                    result = new SpawnResult
                    {
                        Node        = mech,
                        EntryNode   = mech,
                        ExitNodes   = new Entity[] { mech },
                        IsMechanism = true,
                    };
                }
                else
                {
                    if (childEntry.definition == null)
                    {
                        Debug.LogWarning($"[LoomBootstrap] '{def.typeName}' children[{g}] has no definition — skipping.");
                        continue;
                    }
                    result = SpawnNode(em, topArch, childArch, internalMechArch, edgeArch,
                        childEntry.definition, childPos, node);
                }

                // Tag the immediate child as the entry/exit of this frame.
                // Inner composites tag their own recursive entry/exit independently — the same entity
                // is never tagged twice by this scope, but a deeper recursion may have already tagged
                // a different inner entity. AddIfMissing keeps stamping idempotent.
                if (isFirstGroup) AddIfMissing<FrameEntry>(em, result.Node);
                if (isLastGroup)  AddIfMissing<FrameExit>(em, result.Node);

                group.Add(result);
            }

            groups.Add(group);
        }

        // Wire adjacent groups.
        // Mechanism sources: add all outbound edges to their MechanismConnections.
        // Node sources: edges are found by PacketTraverseSystem's outbound lookup.
        for (int g = 0; g < numGroups - 1; g++)
        {
            var src = groups[g];
            var dst = groups[g + 1];

            for (int s = 0; s < src.Count; s++)
            {
                for (int d = 0; d < dst.Count; d++)
                {
                    foreach (Entity srcExit in src[s].ExitNodes)
                        MakeEdge(em, edgeArch, srcExit, dst[d].EntryNode);
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

    static void AddIfMissing<T>(EntityManager em, Entity e) where T : unmanaged, IComponentData
    {
        if (!em.HasComponent<T>(e))
            em.AddComponent<T>(e);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    Entity MakeEdge(EntityManager em, EntityArchetype arch, Entity from, Entity to)
    {
        var fromPos = em.GetComponentData<NodeTransform>(from).Position;
        var toPos   = em.GetComponentData<NodeTransform>(to).Position;
        Entity edge = em.CreateEntity(arch);
        em.SetComponentData(edge, new Edge
        {
            FromNode = from,
            ToNode   = to,
            Length   = math.distance(fromPos, toPos)
        });

        if (em.HasComponent<Mechanism>(from) && em.HasBuffer<MechanismConnections>(from))
            em.GetBuffer<MechanismConnections>(from).Add(new MechanismConnections { Edge = edge });

        return edge;
    }
}
