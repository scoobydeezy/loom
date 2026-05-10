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

        var topArch = em.CreateArchetype(
            typeof(Node), typeof(NodeType),
            typeof(NodeTransform), typeof(WorldSpaceTransform),
            typeof(NodeBounds), typeof(NodeAnchors),
            typeof(TopologyRoot), typeof(TransformDirty));

        var childArch = em.CreateArchetype(
            typeof(Node), typeof(NodeParent),
            typeof(NodeTransform), typeof(WorldSpaceTransform),
            typeof(NodeBounds), typeof(NodeAnchors),
            typeof(TopologyRoot), typeof(TransformDirty));

        var internalMechArch = em.CreateArchetype(
            typeof(Mechanism), typeof(MechanismType), typeof(MechanismConnections),
            typeof(NodeParent),
            typeof(NodeTransform), typeof(WorldSpaceTransform),
            typeof(NodeAnchors),
            typeof(TopologyRoot), typeof(TransformDirty));

        var edgeArch   = em.CreateArchetype(typeof(Edge));
        var packetArch = em.CreateArchetype(typeof(Packet), typeof(PacketDestination), typeof(PacketSlot));

        if (nodeAType == null || nodeBType == null || nodeCType == null)
        {
            Debug.LogError("[LoomBootstrap] All three NodeTypeDefinition slots must be assigned in the inspector.");
            return;
        }

        var pending = new List<(Entity from, Entity to)>();

        var resultA = SpawnNode(em, topArch, childArch, internalMechArch, nodeAType, new float3(-5, 0, 0), Entity.Null, pending, id: 0);
        var resultB = SpawnNode(em, topArch, childArch, internalMechArch, nodeBType, new float3( 5, 0, 0), Entity.Null, pending, id: 1);
        var resultC = SpawnNode(em, topArch, childArch, internalMechArch, nodeCType, new float3( 0, 0, 6), Entity.Null, pending, id: 2);

        // External wiring — record indices so packet placement can grab the first A→B edge.
        int eAtoBIndex = -1;
        foreach (var exit in resultA.ExitNodes)
        {
            if (eAtoBIndex < 0) eAtoBIndex = pending.Count;
            pending.Add((exit, resultB.EntryNode));
        }
        foreach (var exit in resultB.ExitNodes)
            pending.Add((exit, resultC.EntryNode));
        foreach (var exit in resultC.ExitNodes)
            pending.Add((exit, resultA.EntryNode));

        EnsureTopologyVersion(em);
        InitializeWorldSpaceTransforms(em);

        var edgeEntities = new Entity[pending.Count];
        for (int i = 0; i < pending.Count; i++)
            edgeEntities[i] = MakeEdge(em, edgeArch, pending[i].from, pending[i].to);

        Entity eAtoB = edgeEntities[eAtoBIndex];

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
    // Recursive spawner — positions are LOCAL to the parent node.
    // For root-level nodes (parent == Entity.Null), local space = world space.
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

        em.SetComponentData(node, new Node { Id = id });
        em.SetComponentData(node, new NodeTransform { Position = position, Rotation = quaternion.identity });

        if (isTopLevel)
            em.SetComponentData(node, new NodeType { TypeName = def.typeName });
        else
            em.SetComponentData(node, new NodeParent { Parent = parent });

        // TopologyRoot — root nodes own themselves; children inherit from parent.
        Entity root = isTopLevel
            ? node
            : em.GetComponentData<TopologyRoot>(parent).Root;
        em.SetComponentData(node, new TopologyRoot { Root = root });

        // NodeBounds — laneCount drives vertical extent; leaves default to one lane.
        int laneCount = 1;
        if (def.children != null)
            for (int g = 0; g < def.children.Length; g++)
                laneCount = math.max(laneCount, math.max(1, def.children[g].count));
        em.SetComponentData(node, new NodeBounds
        {
            Size = new float2(def.frameWidth, laneCount * def.frameHeight)
        });

        // Local-space wall coordinates — origin (0,0,0) is the center of this node.
        float entryWallX = -def.frameWidth * 0.5f;
        float exitWallX  = +def.frameWidth * 0.5f;

        // Leaf node — exit anchor is the right wall, entry anchor is the left wall.
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
                float childY = (count == 1)
                    ? 0f
                    : topY - (i + 0.5f) * (totalHeight / count);
                var childPos = new float3(groupX, childY, 0f);

                SpawnResult result;
                if (childEntry.childType == ChildType.Mechanism)
                {
                    Entity mech = em.CreateEntity(internalMechArch);
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
                        Debug.LogWarning($"[LoomBootstrap] '{def.typeName}' children[{g}] has no definition — skipping.");
                        continue;
                    }
                    result = SpawnNode(em, topArch, childArch, internalMechArch,
                        childEntry.definition, childPos, node, pendingEdges);
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

        // Wire adjacent groups. Edge creation is deferred so WorldSpaceTransform can be initialized
        // before any MakeEdge call (MakeEdge derives length from world-space endpoint positions).
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

        // Gather all exit entities from the last group
        var lastGroup = groups[numGroups - 1];
        var exitNodes = new List<Entity>(lastGroup.Count);
        foreach (var r in lastGroup)
            foreach (var e in r.ExitNodes)
                exitNodes.Add(e);

        // NodeAnchors — entry on left wall, exit on right wall.
        // Layout places exit lanes vertically symmetric around y=0, so centroid Y is 0 by construction.
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
    // BFS from roots; same composition logic as WorldSpaceCacheSystem.
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
    // EdgeLengthCacheSystem keeps Length current every frame after bootstrap.
    // -------------------------------------------------------------------------

    Entity MakeEdge(EntityManager em, EntityArchetype arch, Entity from, Entity to)
    {
        var fromPos = em.GetComponentData<WorldSpaceTransform>(from).Position;
        var toPos   = em.GetComponentData<WorldSpaceTransform>(to).Position;
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
