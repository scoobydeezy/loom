using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using System.Collections.Generic;

/// <summary>
/// Builds physical frame geometry for each composite node, derived from the world-space positions
/// of its FrameEntry and FrameExit children. Anchor positions are derived from the entity's
/// WorldSpaceTransform — ECS is the source of truth — and cached in AnchorPositions for the
/// edge / packet visualizers to look up.
/// </summary>
[DefaultExecutionOrder(50)]
public class NodeFrameVisualizer : MonoBehaviour
{
    const float FrameEdgeThickness = 0.05f;

    [Header("Prefabs")]
    public GameObject pegPrefab;
    public GameObject barPrefab;
    public GameObject frameEdgePrefab;

    [Header("Settings")]
    public float barProtrusion = 0.15f;
    public float pegDiameter   = 0.15f;
    public float barDiameter   = 0.15f;

    [Tooltip("Minimum vertical extent for frames whose entry and exit anchors collapse to one point (single-lane composites).")]
    public float minFrameHeight = 0.6f;

    [Header("Frame State Colors")]
    public Color normalColor    = new Color(0.18f, 0.18f, 0.18f, 1f);
    public Color activeColor    = new Color(0f, 1f, 1f, 1f);
    public Color pressuredColor = new Color(1f, 0.5f, 0f, 1f);

    enum FrameState { Normal, Active, Pressured }

    class FrameData
    {
        public Material[] EdgeMaterials;
    }

    /// <summary>
    /// World-space anchor position per FrameEntry / FrameExit entity, refreshed each frame.
    /// External edges and packets resolve their endpoints through this table; misses fall back
    /// to WorldSpaceTransform (see RenderingUtils.ResolvePosition).
    /// </summary>
    public readonly Dictionary<Entity, Vector3> AnchorPositions = new();

    EntityManager     entityManager;
    EntityQuery       nodeQuery;
    EntityQuery       entryQuery;
    EntityQuery       exitQuery;
    EntityQuery       edgeQuery;
    EntityQuery       waitingQuery;
    PacketVisualizer  packetVisualizer;

    readonly Dictionary<Entity, FrameData> frames = new();
    bool initialized;

    void Start()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        nodeQuery     = entityManager.CreateEntityQuery(typeof(Node), typeof(WorldSpaceTransform));
        entryQuery    = entityManager.CreateEntityQuery(typeof(FrameEntry), typeof(WorldSpaceTransform), typeof(NodeParent));
        exitQuery     = entityManager.CreateEntityQuery(typeof(FrameExit),  typeof(WorldSpaceTransform), typeof(NodeParent));
        edgeQuery     = entityManager.CreateEntityQuery(typeof(Edge));
        waitingQuery  = entityManager.CreateEntityQuery(typeof(Packet), typeof(WaitingAtNode));
    }

    void Update()
    {
        if (!initialized)
        {
            if (nodeQuery.CalculateEntityCount() == 0) return;
            BuildAllFrames();
            initialized = true;
        }
        UpdateAnchorPositions();
        UpdateFrameStates();
    }

    // -------------------------------------------------------------------------
    // AnchorPositions — refreshed each frame so node movement propagates.
    // -------------------------------------------------------------------------

    void UpdateAnchorPositions()
    {
        AnchorPositions.Clear();
        AccumulateAnchors(entryQuery);
        AccumulateAnchors(exitQuery);
    }

    void AccumulateAnchors(EntityQuery q)
    {
        var arr = q.ToEntityArray(Unity.Collections.Allocator.Temp);
        foreach (var e in arr)
            AnchorPositions[e] = (Vector3)entityManager.GetComponentData<WorldSpaceTransform>(e).Position;
        arr.Dispose();
    }

    // -------------------------------------------------------------------------
    // One-shot build: derive each composite's frame from its FrameEntry/FrameExit positions.
    // -------------------------------------------------------------------------

    void BuildAllFrames()
    {
        if (pegPrefab == null || barPrefab == null || frameEdgePrefab == null)
        {
            Debug.LogError("[NodeFrameVisualizer] Prefab slots are unassigned — cannot build frames.");
            return;
        }

        // FrameEntry per parent (recipes always have exactly one entry entity per scope).
        var entryByParent = new Dictionary<Entity, Entity>();
        var entries = entryQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        foreach (var e in entries)
        {
            var parent = entityManager.GetComponentData<NodeParent>(e).Parent;
            entryByParent[parent] = e;
        }
        entries.Dispose();

        // FrameExit per parent — typically multiple (one per exit lane).
        var exitsByParent = new Dictionary<Entity, List<Entity>>();
        var exits = exitQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        foreach (var e in exits)
        {
            var parent = entityManager.GetComponentData<NodeParent>(e).Parent;
            if (!exitsByParent.TryGetValue(parent, out var list))
                exitsByParent[parent] = list = new List<Entity>();
            list.Add(e);
        }
        exits.Dispose();

        var parents = new HashSet<Entity>();
        foreach (var kv in entryByParent) parents.Add(kv.Key);
        foreach (var kv in exitsByParent) parents.Add(kv.Key);

        foreach (var parent in parents)
        {
            entryByParent.TryGetValue(parent, out var entry);
            exitsByParent.TryGetValue(parent, out var exitList);
            BuildFrame(parent, entry, exitList);
        }
    }

    void BuildFrame(Entity parent, Entity entryEntity, List<Entity> exitEntities)
    {
        bool hasEntry = entryEntity != Entity.Null;
        bool hasExits = exitEntities != null && exitEntities.Count > 0;
        if (!hasEntry && !hasExits) return;

        Vector3 entryPos = hasEntry
            ? (Vector3)entityManager.GetComponentData<WorldSpaceTransform>(entryEntity).Position
            : Vector3.zero;

        var exitPositions = new List<Vector3>(hasExits ? exitEntities.Count : 0);
        if (hasExits)
        {
            foreach (var e in exitEntities)
                exitPositions.Add((Vector3)entityManager.GetComponentData<WorldSpaceTransform>(e).Position);
        }

        // Wall X coordinates from anchor positions (entries on left wall, exits on right wall).
        float entryWallX = hasEntry ? entryPos.x : exitPositions[0].x;
        float exitWallX  = hasExits ? exitPositions[0].x : entryPos.x;

        // Vertical extent = union of all anchor Y values.
        float minY = hasEntry ? entryPos.y : exitPositions[0].y;
        float maxY = minY;
        if (hasEntry)
        {
            minY = Mathf.Min(minY, entryPos.y);
            maxY = Mathf.Max(maxY, entryPos.y);
        }
        foreach (var p in exitPositions)
        {
            minY = Mathf.Min(minY, p.y);
            maxY = Mathf.Max(maxY, p.y);
        }

        // Single-lane composites collapse vertically — give them a minimum visible height.
        if (maxY - minY < minFrameHeight)
        {
            float midY = (minY + maxY) * 0.5f;
            minY = midY - minFrameHeight * 0.5f;
            maxY = midY + minFrameHeight * 0.5f;
        }

        float midZ = hasEntry ? entryPos.z : exitPositions[0].z;

        var container = new GameObject($"Frame_{parent.Index}");
        container.transform.SetParent(transform, worldPositionStays: false);

        var mats = new Material[4];
        mats[0] = SpawnFrameEdgeHorizontal(container.transform, entryWallX, exitWallX, maxY, midZ);
        mats[1] = SpawnFrameEdgeHorizontal(container.transform, entryWallX, exitWallX, minY, midZ);
        mats[2] = SpawnFrameEdgeVertical  (container.transform, minY, maxY, entryWallX, midZ);
        mats[3] = SpawnFrameEdgeVertical  (container.transform, minY, maxY, exitWallX,  midZ);

        // Entry anchor — always a peg (recipes guarantee a single entry entity per frame).
        if (hasEntry)
        {
            Vector3 pos = entryPos + new Vector3(-barProtrusion, 0, 0);
            SpawnPegPerpendicularToVerticalWall(container.transform, pos);
        }

        // Exit anchor — peg if 1 lane, bar spanning the lanes if more.
        if (hasExits)
        {
            if (exitPositions.Count == 1)
            {
                Vector3 pos = exitPositions[0] + new Vector3(barProtrusion, 0, 0);
                SpawnPegPerpendicularToVerticalWall(container.transform, pos);
            }
            else
            {
                float exitMinY = float.MaxValue, exitMaxY = float.MinValue;
                foreach (var p in exitPositions)
                {
                    exitMinY = Mathf.Min(exitMinY, p.y);
                    exitMaxY = Mathf.Max(exitMaxY, p.y);
                }
                float midY      = (exitMinY + exitMaxY) * 0.5f;
                float spanLength = (exitMaxY - exitMinY) + 2f * barDiameter;
                Vector3 pos     = new Vector3(exitWallX + barProtrusion, midY, midZ);
                SpawnBarAlongVerticalWall(container.transform, pos, spanLength);
            }
        }

        frames[parent] = new FrameData { EdgeMaterials = mats };
    }

    // -------------------------------------------------------------------------
    // Geometry helpers
    // -------------------------------------------------------------------------

    Material SpawnFrameEdgeHorizontal(Transform parent, float minX, float maxX, float y, float z)
    {
        var obj = Instantiate(frameEdgePrefab, parent);
        obj.transform.position   = new Vector3((minX + maxX) * 0.5f, y, z);
        obj.transform.rotation   = Quaternion.identity;
        obj.transform.localScale = new Vector3(maxX - minX, FrameEdgeThickness, FrameEdgeThickness);
        return obj.GetComponent<MeshRenderer>().material;
    }

    Material SpawnFrameEdgeVertical(Transform parent, float minY, float maxY, float x, float z)
    {
        var obj = Instantiate(frameEdgePrefab, parent);
        obj.transform.position   = new Vector3(x, (minY + maxY) * 0.5f, z);
        obj.transform.rotation   = Quaternion.Euler(0, 0, 90);
        obj.transform.localScale = new Vector3(maxY - minY, FrameEdgeThickness, FrameEdgeThickness);
        return obj.GetComponent<MeshRenderer>().material;
    }

    void SpawnPegPerpendicularToVerticalWall(Transform parent, Vector3 pos)
    {
        // Cylinder's local long axis is Y. Rotate 90° on Z so it sticks out along world X
        // (perpendicular to the vertical entry/exit wall). Length = 2 * barProtrusion.
        var obj = Instantiate(pegPrefab, parent);
        obj.transform.position   = pos;
        obj.transform.rotation   = Quaternion.Euler(0, 0, 90);
        obj.transform.localScale = new Vector3(pegDiameter, barProtrusion, pegDiameter);
    }

    void SpawnBarAlongVerticalWall(Transform parent, Vector3 pos, float spanLength)
    {
        // Capsule's local long axis is Y. Keep it that way — bar runs along the vertical wall.
        // Default capsule height is 2 units; localScale.y = spanLength / 2 produces a bar of spanLength.
        var obj = Instantiate(barPrefab, parent);
        obj.transform.position   = pos;
        obj.transform.rotation   = Quaternion.identity;
        obj.transform.localScale = new Vector3(barDiameter, spanLength * 0.5f, barDiameter);
    }

    // -------------------------------------------------------------------------
    // Per-frame state coloring (composite frames only).
    // -------------------------------------------------------------------------

    void UpdateFrameStates()
    {
        if (packetVisualizer == null)
            packetVisualizer = FindAnyObjectByType<PacketVisualizer>();

        var states = new Dictionary<Entity, FrameState>();

        var edges = edgeQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        foreach (var ee in edges)
        {
            var ed = entityManager.GetComponentData<Edge>(ee);
            if (!entityManager.HasComponent<NodeParent>(ed.FromNode)) continue;
            Entity parent = entityManager.GetComponentData<NodeParent>(ed.FromNode).Parent;

            EdgeStressLevel stress = EdgeStressLevel.Free;
            if (packetVisualizer != null)
                packetVisualizer.EdgeStressMap.TryGetValue(ee, out stress);
            if (stress == EdgeStressLevel.Free) continue;

            FrameState newState = (stress == EdgeStressLevel.Stressed || stress == EdgeStressLevel.Jammed)
                ? FrameState.Pressured
                : FrameState.Active;
            Escalate(states, parent, newState);
        }
        edges.Dispose();

        var waitingPackets = waitingQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        foreach (var entity in waitingPackets)
        {
            var packet = entityManager.GetComponentData<Packet>(entity);
            if (!entityManager.Exists(packet.CurrentEdge)) continue;
            var edge = entityManager.GetComponentData<Edge>(packet.CurrentEdge);
            if (!entityManager.HasComponent<NodeParent>(edge.ToNode)) continue;
            Entity parent = entityManager.GetComponentData<NodeParent>(edge.ToNode).Parent;
            Escalate(states, parent, FrameState.Pressured);
        }
        waitingPackets.Dispose();

        foreach (var kv in frames)
        {
            FrameState state = states.TryGetValue(kv.Key, out var s) ? s : FrameState.Normal;
            Color c = state switch
            {
                FrameState.Pressured => pressuredColor,
                FrameState.Active    => activeColor,
                _                    => normalColor,
            };
            foreach (var mat in kv.Value.EdgeMaterials)
                if (mat != null)
                    RenderingUtils.ApplyColor(mat, c);
        }
    }

    static void Escalate(Dictionary<Entity, FrameState> states, Entity parent, FrameState newState)
    {
        if (!states.TryGetValue(parent, out var current) || newState > current)
            states[parent] = newState;
    }
}
