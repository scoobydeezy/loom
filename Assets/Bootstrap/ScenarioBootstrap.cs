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

        StableIdAllocator.EnsureSingletons(em);

        var topArch = em.CreateArchetype(
            typeof(Node), typeof(NodeType),
            typeof(NodeTransform), typeof(WorldSpaceTransform),
            typeof(NodeBounds), typeof(NodeAnchors),
            typeof(TopologyRoot), typeof(TransformDirty),
            typeof(StableId));

        var childArch = em.CreateArchetype(
            typeof(Node), typeof(NodeParent),
            typeof(NodeTransform), typeof(WorldSpaceTransform),
            typeof(NodeBounds), typeof(NodeAnchors),
            typeof(TopologyRoot), typeof(TransformDirty),
            typeof(StableId));

        var internalMechArch = em.CreateArchetype(
            typeof(Mechanism), typeof(MechanismType), typeof(MechanismConnections),
            typeof(NodeParent),
            typeof(NodeTransform), typeof(WorldSpaceTransform),
            typeof(NodeAnchors),
            typeof(TopologyRoot), typeof(TransformDirty),
            typeof(StableId));

        var edgeArch   = em.CreateArchetype(typeof(Edge), typeof(StableId));
        var packetArch = em.CreateArchetype(typeof(Packet), typeof(PacketSlot), typeof(StableId));

        int   n      = nodeTypes.Length;
        float radius = Mathf.Max(CircleRadius, n * 1.2f);

        var pending = new List<(Entity from, Entity to)>();

        // Spawn nodes evenly around a circle
        var results = new SpawnResult[n];
        for (int i = 0; i < n; i++)
        {
            float angle = i * (2f * Mathf.PI / n) - Mathf.PI * 0.5f;
            var   pos   = new float3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
            results[i]  = SpawnNode(em, topArch, childArch, internalMechArch, nodeTypes[i], pos, Entity.Null, pending, id: i);
        }

        // External wiring — each node's exits → next node's entry (last → first).
        // Track the first edge of each arc so packets can be distributed around the full loop.
        var loopArcFirstIndex = new int[n];
        for (int i = 0; i < n; i++)
        {
            int next   = (i + 1) % n;
            int first  = -1;

            foreach (var exit in results[i].ExitNodes)
            {
                if (first < 0) first = pending.Count;
                pending.Add((exit, results[next].EntryNode));
            }
            loopArcFirstIndex[i] = first;
        }

        EnsureTopologyVersion(em);
        InitializeWorldSpaceTransforms(em);

        var edgeEntities = new Entity[pending.Count];
        for (int i = 0; i < pending.Count; i++)
            edgeEntities[i] = MakeEdge(em, edgeArch, pending[i].from, pending[i].to);

        // Resolve loop-arc edge entities and lengths now that MakeEdge has run.
        var loopArcs = new List<(Entity edge, float length)>(n);
        for (int i = 0; i < n; i++)
        {
            Entity arcFirst = edgeEntities[loopArcFirstIndex[i]];
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

        float step    = totalLength / Mathf.Max(1, spawnCount);
        int   arc     = 0;
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
            StableIdAllocator.StampAndRegister(em, p);
            em.SetComponentData(p, new Packet
            {
                CurrentEdge = loopArcs[arc].edge,
                Progress    = globalPos - arcBase,
                Speed       = packetSpeed,
                Color       = PacketColor.White,
                Shape       = PacketShape.Sphere
            });
        }
    }

    // -------------------------------------------------------------------------
    // Recursive spawner (mirrors LoomBootstrap.SpawnNode exactly).
    // Positions are LOCAL to the parent node; for root-level nodes (parent == Entity.Null)
    // local space = world space.
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
        NodeTypeDefinition def, float3 position, Entity parent,
        List<(Entity from, Entity to)> pendingEdges,
        int id = -1)
    {
        bool   isTopLevel = parent == Entity.Null;
        Entity node       = em.CreateEntity(isTopLevel ? topArch : childArch);

        StableIdAllocator.StampAndRegister(em, node);

        em.SetComponentData(node, new Node { Id = id });
        em.SetComponentData(node, new NodeTransform { Position = position, Rotation = quaternion.identity });

        if (isTopLevel)
            em.SetComponentData(node, new NodeType { TypeName = def.typeName });
        else
            em.SetComponentData(node, new NodeParent { Parent = parent });

        Entity root = isTopLevel
            ? node
            : em.GetComponentData<TopologyRoot>(parent).Root;
        em.SetComponentData(node, new TopologyRoot { Root = root });

        int laneCount = 1;
        if (def.children != null)
            for (int g = 0; g < def.children.Length; g++)
                laneCount = math.max(laneCount, math.max(1, def.children[g].count));
        em.SetComponentData(node, new NodeBounds
        {
            Size = new float2(def.frameWidth, laneCount * def.frameHeight)
        });

        float entryWallX = -def.frameWidth * 0.5f;
        float exitWallX  = +def.frameWidth * 0.5f;

        if (def.children == null || def.children.Length == 0)
        {
            em.SetComponentData(node, new NodeAnchors
            {
                EntryLocal = new float3(entryWallX, 0f, 0f),
                ExitLocal  = new float3(exitWallX,  0f, 0f),
            });
            return new SpawnResult { Node = node, EntryNode = node, ExitNodes = new Entity[] { node } };
        }

        int   numGroups   = def.children.Length;
        float totalHeight = laneCount * def.frameHeight;
        float topY        = totalHeight * 0.5f;

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
                float childY = (count == 1)
                    ? 0f
                    : topY - (i + 0.5f) * (totalHeight / count);
                var childPos = new float3(groupX, childY, 0f);

                SpawnResult result;
                if (childEntry.childType == ChildType.Mechanism)
                {
                    Entity mech = em.CreateEntity(internalMechArch);
                    StableIdAllocator.StampAndRegister(em, mech);
                    em.SetComponentData(mech, new NodeTransform { Position = childPos, Rotation = quaternion.identity });
                    em.SetComponentData(mech, new MechanismType { Kind = childEntry.mechanismKind });
                    em.SetComponentData(mech, new NodeParent { Parent = node });
                    em.SetComponentData(mech, new TopologyRoot { Root = root });
                    em.SetComponentData(mech, new NodeAnchors
                    {
                        EntryLocal = float3.zero,
                        ExitLocal  = float3.zero,
                    });
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
                    result = SpawnNode(em, topArch, childArch, internalMechArch,
                        childEntry.definition, childPos, node, pendingEdges);
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
                        pendingEdges.Add((srcExit, dst[d].EntryNode));
                }
            }
        }

        var lastGroup = groups[numGroups - 1];
        var exitNodes = new List<Entity>(lastGroup.Count);
        foreach (var r in lastGroup)
            foreach (var e in r.ExitNodes)
                exitNodes.Add(e);

        em.SetComponentData(node, new NodeAnchors
        {
            EntryLocal = new float3(entryWallX, 0f, 0f),
            ExitLocal  = new float3(exitWallX,  0f, 0f),
        });

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
    // World-space initialization — runs once before any MakeEdge call.
    // -------------------------------------------------------------------------

    static void EnsureTopologyVersion(EntityManager em)
    {
        var q = em.CreateEntityQuery(typeof(TopologyVersion));
        if (q.CalculateEntityCount() == 0)
        {
            Entity e = em.CreateEntity(typeof(TopologyVersion));
            em.SetComponentData(e, new TopologyVersion { Version = 1 });
        }
        else
        {
            var arr = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            Entity e   = arr[0];
            int    cur = em.GetComponentData<TopologyVersion>(e).Version;
            em.SetComponentData(e, new TopologyVersion { Version = cur + 1 });
            arr.Dispose();
        }
    }

    static void InitializeWorldSpaceTransforms(EntityManager em)
    {
        var q   = em.CreateEntityQuery(
            ComponentType.ReadOnly<NodeTransform>(),
            ComponentType.ReadOnly<WorldSpaceTransform>());
        var all = q.ToEntityArray(Unity.Collections.Allocator.Temp);

        var childrenByParent = new Dictionary<Entity, List<Entity>>();
        var roots            = new List<Entity>();

        foreach (var e in all)
        {
            if (em.HasComponent<NodeParent>(e))
            {
                Entity p = em.GetComponentData<NodeParent>(e).Parent;
                if (!childrenByParent.TryGetValue(p, out var list))
                    childrenByParent[p] = list = new List<Entity>();
                list.Add(e);
            }
            else
            {
                roots.Add(e);
            }
        }

        var queue = new Queue<Entity>(roots);
        while (queue.Count > 0)
        {
            Entity e  = queue.Dequeue();
            var    lt = em.GetComponentData<NodeTransform>(e);

            float3     wPos;
            quaternion wRot;
            if (em.HasComponent<NodeParent>(e))
            {
                Entity p  = em.GetComponentData<NodeParent>(e).Parent;
                var    pw = em.GetComponentData<WorldSpaceTransform>(p);
                wPos = pw.Position + math.rotate(pw.Rotation, lt.Position);
                wRot = math.mul(pw.Rotation, lt.Rotation);
            }
            else
            {
                wPos = lt.Position;
                wRot = lt.Rotation;
            }
            em.SetComponentData(e, new WorldSpaceTransform { Position = wPos, Rotation = wRot });

            if (childrenByParent.TryGetValue(e, out var children))
                foreach (var c in children) queue.Enqueue(c);
        }

        all.Dispose();
    }

    // -------------------------------------------------------------------------
    // Edge construction — reads WorldSpaceTransform for the initial length.
    // -------------------------------------------------------------------------

    Entity MakeEdge(EntityManager em, EntityArchetype arch, Entity from, Entity to)
    {
        var fromPos = em.GetComponentData<WorldSpaceTransform>(from).Position;
        var toPos   = em.GetComponentData<WorldSpaceTransform>(to).Position;
        Entity edge = em.CreateEntity(arch);
        StableIdAllocator.StampAndRegister(em, edge);
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
