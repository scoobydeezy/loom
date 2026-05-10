using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using System.Collections.Generic;

/// <summary>
/// Inspector-driven test scaffold. Spawns a looping chain of nodes —
/// N0 → N1 → ... → Nn → N0 — so packets circulate indefinitely.
/// Edge lengths are derived from world-space node positions automatically.
/// </summary>
public class ScenarioBootstrap : MonoBehaviour
{
    [Header("Nodes")]
    public NodeTypeDefinition[] nodeTypes;

    [Header("Packets")]
    public int   packetCount = 100;
    public float packetSpeed = 2f;

    const float CircleRadius = 6f;

    void Start()
    {
        if (nodeTypes == null || nodeTypes.Length < 2)
        {
            Debug.LogError("[ScenarioBootstrap] Assign at least two node types.");
            return;
        }

        for (int i = 0; i < nodeTypes.Length; i++)
        {
            if (nodeTypes[i] == null)
            {
                Debug.LogError($"[ScenarioBootstrap] nodeTypes[{i}] is null.");
                return;
            }
        }

        var em = World.DefaultGameObjectInjectionWorld.EntityManager;

        var topArch          = em.CreateArchetype(typeof(Node), typeof(NodeType), typeof(NodeTransform));
        var childArch        = em.CreateArchetype(typeof(Node), typeof(NodeTransform), typeof(NodeParent));
        var internalMechArch = em.CreateArchetype(typeof(Mechanism), typeof(MechanismType), typeof(NodeTransform), typeof(MechanismConnections), typeof(NodeParent));
        var edgeArch         = em.CreateArchetype(typeof(Edge));
        var packetArch       = em.CreateArchetype(typeof(Packet), typeof(PacketDestination), typeof(PacketSlot));

        int n = nodeTypes.Length;
        float radius = Mathf.Max(CircleRadius, n * 1.2f);

        // Spawn nodes evenly around a circle
        var results = new SpawnResult[n];
        for (int i = 0; i < n; i++)
        {
            float angle = i * (2f * Mathf.PI / n) - Mathf.PI * 0.5f;
            var pos = new float3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
            results[i] = SpawnNode(em, topArch, childArch, internalMechArch, edgeArch, nodeTypes[i], pos, Entity.Null, id: i);
        }

        // Wire the loop: each node's exits → next node's entry (last → first).
        // Track the first edge of each arc so packets can be distributed around the full loop.
        var loopArcs = new List<(Entity edge, float length)>(n);
        for (int i = 0; i < n; i++)
        {
            int    next     = (i + 1) % n;
            Entity arcFirst = Entity.Null;

            foreach (var exit in results[i].ExitNodes)
            {
                var e = MakeEdge(em, edgeArch, exit, results[next].EntryNode);
                if (arcFirst == Entity.Null) arcFirst = e;
            }
            loopArcs.Add((arcFirst, em.GetComponentData<Edge>(arcFirst).Length));
        }

        // Distribute packets around the full loop at guaranteed minimum spacing so that
        // the follow-until-bumping behavior is immediately observable.
        // Packets start touching when they catch up to one another, not at spawn time.
        float totalLength = 0f;
        foreach (var (_, l) in loopArcs) totalLength += l;

        float minSpacing = PacketTraverseSystem.BeadDiameter * 2f;
        int   maxFit     = Mathf.Max(1, Mathf.FloorToInt(totalLength / minSpacing));
        int   spawnCount = Mathf.Min(packetCount, maxFit);
        if (spawnCount < packetCount)
            Debug.LogWarning($"[ScenarioBootstrap] Spawning {spawnCount} of {packetCount} requested packets — loop is {totalLength:F1} units; reduce packetCount or increase node spacing.");

        float step = totalLength / Mathf.Max(1, spawnCount);
        int   arc  = 0;
        float arcBase = 0f;

        for (int i = 0; i < spawnCount; i++)
        {
            float globalPos = i * step;

            // Advance to the arc that contains this global position
            while (arc < loopArcs.Count - 1 && globalPos >= arcBase + loopArcs[arc].length)
            {
                arcBase += loopArcs[arc].length;
                arc++;
            }

            Entity p = em.CreateEntity(packetArch);
            em.SetComponentData(p, new Packet
            {
                CurrentEdge = loopArcs[arc].edge,
                Progress    = globalPos - arcBase,
                Speed       = packetSpeed
            });
            em.SetComponentData(p, new PacketDestination { Node = results[(arc + 1) % n].Node });
        }
    }

    // -------------------------------------------------------------------------
    // Recursive spawner (mirrors LoomBootstrap.SpawnNode exactly)
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

            float groupX;
            if (numGroups == 1)        groupX = exitWallX;
            else if (isFirstGroup)     groupX = entryWallX;
            else if (isLastGroup)      groupX = exitWallX;
            else                       groupX = math.lerp(entryWallX, exitWallX, (float)g / (numGroups - 1));

            for (int i = 0; i < count; i++)
            {
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
                        Debug.LogWarning($"[ScenarioBootstrap] '{def.typeName}' children[{g}] has no definition — skipping.");
                        continue;
                    }
                    result = SpawnNode(em, topArch, childArch, internalMechArch, edgeArch,
                        childEntry.definition, childPos, node);
                }

                if (isFirstGroup) AddIfMissing<FrameEntry>(em, result.Node);
                if (isLastGroup)  AddIfMissing<FrameExit>(em, result.Node);

                group.Add(result);
            }

            groups.Add(group);
        }

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
