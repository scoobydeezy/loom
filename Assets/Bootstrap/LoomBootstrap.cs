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

    // -------------------------------------------------------------------------
    // Start
    // -------------------------------------------------------------------------

    void Start()
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;

        var topArch    = em.CreateArchetype(typeof(Node), typeof(NodeType), typeof(NodeTransform));
        var childArch  = em.CreateArchetype(typeof(Node), typeof(NodeTransform), typeof(NodeParent));
        var edgeArch   = em.CreateArchetype(typeof(Edge));
        var packetArch = em.CreateArchetype(typeof(Packet));

        if (nodeAType == null || nodeBType == null || nodeCType == null)
        {
            Debug.LogError("[LoomBootstrap] All three NodeTypeDefinition slots must be assigned in the inspector.");
            return;
        }

        // Each node's internal graph is determined entirely by its NodeTypeDefinition recipe.
        var resultA = SpawnNode(em, topArch, childArch, edgeArch, nodeAType, new float3(-5, 0, 0), Entity.Null, id: 0);
        var resultB = SpawnNode(em, topArch, childArch, edgeArch, nodeBType, new float3( 5, 0, 0), Entity.Null, id: 1);
        var resultC = SpawnNode(em, topArch, childArch, edgeArch, nodeCType, new float3( 0, 0, 6), Entity.Null, id: 2);

        // Global edges wire between each node's exit boundary and the next node's entry boundary.
        Entity edgeAB = MakeEdge(em, edgeArch, resultA.ExitNode, resultB.EntryNode, length: 10f, capacity: 50);
        Entity edgeBC = MakeEdge(em, edgeArch, resultB.ExitNode, resultC.EntryNode, length: 10f, capacity: 15);
        Entity edgeCA = MakeEdge(em, edgeArch, resultC.ExitNode, resultA.EntryNode, length: 10f, capacity: 50);

        // Round-robin across all lane combinations through A, B, and C.
        int pathsA = Mathf.Max(1, resultA.Paths.Count);
        int pathsB = Mathf.Max(1, resultB.Paths.Count);
        int pathsC = Mathf.Max(1, resultC.Paths.Count);
        int totalVariants = pathsA * pathsB * pathsC;

        for (int i = 0; i < packetCount; i++)
        {
            int variant = i % totalVariants;
            int laneB   = variant % pathsB;
            int laneC   = variant / pathsB % pathsC;
            int laneA   = variant / (pathsB * pathsC) % pathsA;

            Entity p = em.CreateEntity(packetArch);
            em.SetComponentData(p, new Packet
            {
                CurrentEdge = edgeAB,
                PathIndex   = 0,
                Progress    = UnityEngine.Random.Range(0f, 10f),
                Speed       = 2f
            });

            // Route is the full cycle: A→B (global) → through B → B→C (global)
            // → through C → C→A (global) → through A → wraps back to A→B.
            var route = em.AddBuffer<PacketRoute>(p);
            route.Add(new PacketRoute { Edge = edgeAB });
            foreach (var edge in resultB.Paths[laneB])
                route.Add(new PacketRoute { Edge = edge });
            route.Add(new PacketRoute { Edge = edgeBC });
            foreach (var edge in resultC.Paths[laneC])
                route.Add(new PacketRoute { Edge = edge });
            route.Add(new PacketRoute { Edge = edgeCA });
            foreach (var edge in resultA.Paths[laneA])
                route.Add(new PacketRoute { Edge = edge });
        }
    }

    // -------------------------------------------------------------------------
    // Recursive spawner
    // -------------------------------------------------------------------------

    // Returned by SpawnNode — gives the bootstrap enough information to wire
    // global edges and build flat packet routes through any node's internals.
    class SpawnResult
    {
        public Entity       Node;       // root entity of this spawn
        public Entity       EntryNode;  // global edges wire TO here
        public Entity       ExitNode;   // global edges wire FROM here
        public List<Entity[]> Paths;    // all distinct edge sequences from entry to exit
    }

    SpawnResult SpawnNode(
        EntityManager em,
        EntityArchetype topArch, EntityArchetype childArch, EntityArchetype edgeArch,
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

        // Leaf node — no children; returns itself as entry and exit with one empty path.
        if (def.children == null || def.children.Length == 0)
        {
            return new SpawnResult
            {
                Node      = node,
                EntryNode = node,
                ExitNode  = node,
                Paths     = new List<Entity[]> { new Entity[0] }
            };
        }

        // Container node — spawn each child group and wire them together.
        int numGroups = def.children.Length;
        const float groupSpacing = 1.5f;
        const float nodeSpacing  = 0.8f;
        float totalHeight = (numGroups - 1) * groupSpacing;

        // groups[g] = spawned results for all nodes in children[g]
        var groups = new List<List<SpawnResult>>(numGroups);

        for (int g = 0; g < numGroups; g++)
        {
            var childEntry = def.children[g];
            if (childEntry?.definition == null)
            {
                Debug.LogWarning($"[LoomBootstrap] '{def.typeName}' children[{g}] has no definition — skipping.");
                groups.Add(new List<SpawnResult>());
                continue;
            }

            int count    = Mathf.Max(1, childEntry.count);
            float groupY = position.y - totalHeight * 0.5f + g * groupSpacing;

            var group = new List<SpawnResult>(count);
            for (int i = 0; i < count; i++)
            {
                float childX = position.x + (i - (count - 1) * 0.5f) * nodeSpacing;
                group.Add(SpawnNode(em, topArch, childArch, edgeArch,
                    childEntry.definition, new float3(childX, groupY, position.z), node));
            }
            groups.Add(group);
        }

        // Wire adjacent groups: every node in group[g] connects to every node in group[g+1].
        // Edge length = children[g].definition.internalPathLength
        // Edge capacity = children[g].definition.edgeCapacity
        var boundaries = new List<List<(Entity Edge, int Src, int Dst)>>(numGroups - 1);

        for (int g = 0; g < numGroups - 1; g++)
        {
            var src    = groups[g];
            var dst    = groups[g + 1];
            var srcDef = def.children[g].definition;

            var boundary = new List<(Entity, int, int)>(src.Count * dst.Count);
            for (int s = 0; s < src.Count; s++)
            {
                for (int d = 0; d < dst.Count; d++)
                {
                    Entity e = MakeEdge(em, edgeArch,
                        src[s].ExitNode, dst[d].EntryNode,
                        srcDef.internalPathLength, srcDef.edgeCapacity);
                    boundary.Add((e, s, d));
                }
            }
            boundaries.Add(boundary);
        }

        // Enumerate all traversal paths from the first group to the last.
        var paths = new List<Entity[]>();
        for (int i = 0; i < groups[0].Count; i++)
            EnumeratePaths(groups, boundaries, groupIdx: 0, nodeIdx: i, new List<Entity>(), paths);

        return new SpawnResult
        {
            Node      = node,
            EntryNode = groups[0].Count > 0 ? groups[0][0].EntryNode : node,
            ExitNode  = groups[numGroups - 1].Count > 0 ? groups[numGroups - 1][0].ExitNode : node,
            Paths     = paths.Count > 0 ? paths : new List<Entity[]> { new Entity[0] }
        };
    }

    // DFS through the group graph — appends one complete edge sequence per unique path.
    void EnumeratePaths(
        List<List<SpawnResult>> groups,
        List<List<(Entity Edge, int Src, int Dst)>> boundaries,
        int groupIdx, int nodeIdx,
        List<Entity> current,
        List<Entity[]> result)
    {
        var nodeResult = groups[groupIdx][nodeIdx];

        if (groupIdx == groups.Count - 1)
        {
            // Reached the last group — combine current prefix with each sub-path of this node.
            foreach (var sub in nodeResult.Paths)
            {
                var full = new Entity[current.Count + sub.Length];
                current.CopyTo(full, 0);
                sub.CopyTo(full, current.Count);
                result.Add(full);
            }
            return;
        }

        var boundary = boundaries[groupIdx];
        foreach (var (edge, src, dst) in boundary)
        {
            if (src != nodeIdx) continue;

            foreach (var sub in nodeResult.Paths)
            {
                // Build: current edges + this node's sub-path + connecting edge, then recurse.
                var next = new List<Entity>(current.Count + sub.Length + 1);
                next.AddRange(current);
                next.AddRange(sub);
                next.Add(edge);
                EnumeratePaths(groups, boundaries, groupIdx + 1, dst, next, result);
            }
        }
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
