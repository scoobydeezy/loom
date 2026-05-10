using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Spawn-time recipe assembler. Takes a NodeTypeDefinition and a parent context,
/// produces an ECS entity subgraph stamped with StableIds, and records the
/// resulting root for SpawnNodeCommand.
///
/// The bootstrap files have their own spawners — this is the editor-facing
/// equivalent used at runtime by commands. It mirrors LoomBootstrap.SpawnNode's
/// layout rules so editor-created nodes are indistinguishable from bootstrap ones.
/// </summary>
public static class NodeAssembler
{
    public class AssembledNode
    {
        public Entity   Node;
        public Entity   EntryNode;
        public Entity[] ExitNodes;
        public bool     IsMechanism;
        public StableId RootStableId;
    }

    public static AssembledNode Assemble(EntityManager em, NodeTypeDefinition def, float3 worldPosition, Entity parent)
    {
        var pending = new List<(Entity from, Entity to)>();
        var result  = SpawnRecursive(em, def, worldPosition, parent, pending);

        // Run a localized world-space refresh so MakeEdge derives lengths from current positions.
        RefreshWorldSpaceTransforms(em);

        // Materialize the deferred edges.
        foreach (var (from, to) in pending)
            MakeEdge(em, from, to);

        result.RootStableId = em.GetComponentData<StableId>(result.Node);
        return result;
    }

    static AssembledNode SpawnRecursive(
        EntityManager em, NodeTypeDefinition def, float3 position, Entity parent,
        List<(Entity from, Entity to)> pendingEdges)
    {
        bool isTopLevel = parent == Entity.Null;

        Entity node = isTopLevel
            ? em.CreateEntity(typeof(Node), typeof(NodeType),
                              typeof(NodeTransform), typeof(WorldSpaceTransform),
                              typeof(NodeBounds), typeof(NodeAnchors),
                              typeof(TopologyRoot), typeof(TransformDirty),
                              typeof(StableId))
            : em.CreateEntity(typeof(Node), typeof(NodeParent),
                              typeof(NodeTransform), typeof(WorldSpaceTransform),
                              typeof(NodeBounds), typeof(NodeAnchors),
                              typeof(TopologyRoot), typeof(TransformDirty),
                              typeof(StableId));

        StableIdAllocator.StampAndRegister(em, node);
        em.SetComponentData(node, new Node { Id = -1 });
        em.SetComponentData(node, new NodeTransform { Position = position, Rotation = quaternion.identity });

        if (isTopLevel) em.SetComponentData(node, new NodeType { TypeName = def.typeName });
        else            em.SetComponentData(node, new NodeParent { Parent = parent });

        Entity root = isTopLevel ? node : em.GetComponentData<TopologyRoot>(parent).Root;
        em.SetComponentData(node, new TopologyRoot { Root = root });

        int laneCount = 1;
        if (def.children != null)
            for (int g = 0; g < def.children.Length; g++)
                laneCount = math.max(laneCount, math.max(1, def.children[g].count));
        em.SetComponentData(node, new NodeBounds { Size = new float2(def.frameWidth, laneCount * def.frameHeight) });

        float entryWallX = -def.frameWidth * 0.5f;
        float exitWallX  = +def.frameWidth * 0.5f;

        if (def.children == null || def.children.Length == 0)
        {
            em.SetComponentData(node, new NodeAnchors
            {
                EntryLocal = new float3(entryWallX, 0f, 0f),
                ExitLocal  = new float3(exitWallX,  0f, 0f),
            });
            return new AssembledNode { Node = node, EntryNode = node, ExitNodes = new[] { node } };
        }

        int   numGroups   = def.children.Length;
        float totalHeight = laneCount * def.frameHeight;
        float topY        = totalHeight * 0.5f;

        var groups = new List<List<AssembledNode>>(numGroups);

        for (int g = 0; g < numGroups; g++)
        {
            var entry = def.children[g];
            int count = Mathf.Max(1, entry.count);
            var group = new List<AssembledNode>(count);

            bool isFirst = (g == 0);
            bool isLast  = (g == numGroups - 1);

            float groupX;
            if (numGroups == 1)        groupX = exitWallX;
            else if (isFirst)          groupX = entryWallX;
            else if (isLast)           groupX = exitWallX;
            else                       groupX = math.lerp(entryWallX, exitWallX, (float)g / (numGroups - 1));

            for (int i = 0; i < count; i++)
            {
                float childY = (count == 1) ? 0f : topY - (i + 0.5f) * (totalHeight / count);
                var   childPos = new float3(groupX, childY, 0f);

                AssembledNode r;
                if (entry.childType == ChildType.Mechanism)
                {
                    Entity mech = em.CreateEntity(
                        typeof(Mechanism), typeof(MechanismType), typeof(MechanismConnections),
                        typeof(NodeParent),
                        typeof(NodeTransform), typeof(WorldSpaceTransform),
                        typeof(NodeAnchors),
                        typeof(TopologyRoot), typeof(TransformDirty),
                        typeof(StableId));
                    StableIdAllocator.StampAndRegister(em, mech);
                    em.SetComponentData(mech, new NodeTransform { Position = childPos, Rotation = quaternion.identity });
                    em.SetComponentData(mech, new MechanismType { Kind = entry.mechanismKind });
                    em.SetComponentData(mech, new NodeParent   { Parent = node });
                    em.SetComponentData(mech, new TopologyRoot { Root   = root });
                    em.SetComponentData(mech, new NodeAnchors  { EntryLocal = float3.zero, ExitLocal = float3.zero });
                    r = new AssembledNode { Node = mech, EntryNode = mech, ExitNodes = new[] { mech }, IsMechanism = true };
                }
                else
                {
                    if (entry.definition == null) continue;
                    r = SpawnRecursive(em, entry.definition, childPos, node, pendingEdges);
                }

                if (isFirst) AddIfMissing<FrameEntry>(em, r.Node);
                if (isLast)  AddIfMissing<FrameExit>(em, r.Node);
                group.Add(r);
            }

            groups.Add(group);
        }

        for (int g = 0; g < numGroups - 1; g++)
        {
            var src = groups[g];
            var dst = groups[g + 1];
            for (int s = 0; s < src.Count; s++)
                for (int d = 0; d < dst.Count; d++)
                    foreach (Entity exit in src[s].ExitNodes)
                        pendingEdges.Add((exit, dst[d].EntryNode));
        }

        var lastGroup = groups[numGroups - 1];
        var exits     = new List<Entity>(lastGroup.Count);
        foreach (var r in lastGroup) foreach (var e in r.ExitNodes) exits.Add(e);

        em.SetComponentData(node, new NodeAnchors
        {
            EntryLocal = new float3(entryWallX, 0f, 0f),
            ExitLocal  = new float3(exitWallX,  0f, 0f),
        });

        return new AssembledNode
        {
            Node      = node,
            EntryNode = groups[0].Count > 0 ? groups[0][0].EntryNode : node,
            ExitNodes = exits.Count > 0 ? exits.ToArray() : new[] { node },
        };
    }

    static void AddIfMissing<T>(EntityManager em, Entity e) where T : unmanaged, IComponentData
    {
        if (!em.HasComponent<T>(e)) em.AddComponent<T>(e);
    }

    static void MakeEdge(EntityManager em, Entity from, Entity to)
    {
        var fromPos = em.GetComponentData<WorldSpaceTransform>(from).Position;
        var toPos   = em.GetComponentData<WorldSpaceTransform>(to).Position;

        Entity edge = em.CreateEntity(typeof(Edge), typeof(StableId));
        StableIdAllocator.StampAndRegister(em, edge);
        em.SetComponentData(edge, new Edge { FromNode = from, ToNode = to, Length = math.distance(fromPos, toPos) });

        if (em.HasComponent<Mechanism>(from) && em.HasBuffer<MechanismConnections>(from))
            em.GetBuffer<MechanismConnections>(from).Add(new MechanismConnections { Edge = edge });
    }

    /// <summary>
    /// One-shot BFS world-space refresh — used after spawning so newly created
    /// children have valid WorldSpaceTransform values before MakeEdge measures them.
    /// </summary>
    static void RefreshWorldSpaceTransforms(EntityManager em)
    {
        using var q   = em.CreateEntityQuery(
            ComponentType.ReadOnly<NodeTransform>(),
            ComponentType.ReadOnly<WorldSpaceTransform>());
        using var all = q.ToEntityArray(Unity.Collections.Allocator.Temp);

        var byParent = new Dictionary<Entity, List<Entity>>();
        var roots    = new List<Entity>();
        foreach (var e in all)
        {
            if (em.HasComponent<NodeParent>(e))
            {
                Entity p = em.GetComponentData<NodeParent>(e).Parent;
                if (!byParent.TryGetValue(p, out var list)) byParent[p] = list = new List<Entity>();
                list.Add(e);
            }
            else roots.Add(e);
        }

        var queue = new Queue<Entity>(roots);
        while (queue.Count > 0)
        {
            Entity e  = queue.Dequeue();
            var    lt = em.GetComponentData<NodeTransform>(e);
            float3 wPos; quaternion wRot;
            if (em.HasComponent<NodeParent>(e))
            {
                Entity p  = em.GetComponentData<NodeParent>(e).Parent;
                var    pw = em.GetComponentData<WorldSpaceTransform>(p);
                wPos = pw.Position + math.rotate(pw.Rotation, lt.Position);
                wRot = math.mul(pw.Rotation, lt.Rotation);
            }
            else
            {
                wPos = lt.Position; wRot = lt.Rotation;
            }
            em.SetComponentData(e, new WorldSpaceTransform { Position = wPos, Rotation = wRot });
            if (byParent.TryGetValue(e, out var children))
                foreach (var c in children) queue.Enqueue(c);
        }
    }
}
