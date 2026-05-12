using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using System.Collections.Generic;

/// <summary>
/// Builds physical frame geometry for each composite node, derived from the world-space positions
/// of its FrameEntry and FrameExit children. Anchor positions are derived from each anchor entity's
/// WorldSpaceTransform — ECS is the source of truth — and refreshed in AnchorPositions every frame
/// for the edge / packet visualizers to look up.
///
/// All geometry construction and repositioning happens in Update so that nodes spawned post-startup
/// pick up frame visuals on the next frame and dragged nodes drag their frames with them. No
/// position data is cached — every transform comes from a live WorldSpaceTransform read.
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

    /// <summary>
    /// References to the spawned GameObjects for one composite frame, plus the anchor entities used
    /// to recompute positions each frame. No position fields — every transform read is fresh.
    /// </summary>
    class FrameData
    {
        public GameObject  Container;
        public Transform[] FrameEdgeTransforms; // 0=top, 1=bottom, 2=entry wall, 3=exit wall
        public Material[]  FrameEdgeMaterials;
        public Transform   EntryAnchorTransform; // null if no entry
        public Transform   ExitAnchorTransform;  // null if no exits
        public bool        ExitIsBar;            // true ↦ exit anchor is a bar (multi-lane), false ↦ peg
        public Entity      EntryEntity;          // Entity.Null if no entry
        public Entity[]    ExitEntities;         // length 0 if no exits

        public void Destroy()
        {
            if (Container != null) UnityEngine.Object.Destroy(Container);
        }
    }

    /// <summary>
    /// World-space anchor position per FrameEntry / FrameExit entity, refreshed each frame.
    /// External edges and packets resolve their endpoints through this table; misses fall back
    /// to WorldSpaceTransform (see RenderingUtils.ResolvePosition).
    /// </summary>
    public readonly Dictionary<Entity, Vector3> AnchorPositions = new();

    EntityManager    entityManager;
    EntityQuery      entryQuery;
    EntityQuery      exitQuery;
    EntityQuery      edgeQuery;
    EntityQuery      waitingQuery;
    PacketVisualizer packetVisualizer;

    readonly Dictionary<Entity, FrameData> frameData   = new();
    readonly HashSet<Entity>                builtFrames = new();
    bool queriesReady;

    void EnsureQueries()
    {
        if (queriesReady) return;
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null) return;

        entityManager = world.EntityManager;
        entryQuery    = entityManager.CreateEntityQuery(typeof(FrameEntry), typeof(WorldSpaceTransform), typeof(NodeParent));
        exitQuery     = entityManager.CreateEntityQuery(typeof(FrameExit),  typeof(WorldSpaceTransform), typeof(NodeParent));
        edgeQuery     = entityManager.CreateEntityQuery(typeof(Edge));
        waitingQuery  = entityManager.CreateEntityQuery(typeof(Packet), typeof(WaitingAtNode));
        queriesReady  = true;
    }

    void Update()
    {
        EnsureQueries();
        if (!queriesReady) return;

        if (pegPrefab == null || barPrefab == null || frameEdgePrefab == null)
            return; // Prefab slots unassigned — nothing to build.

        CleanupDestroyedFrames();
        BuildNewFrames();
        UpdateAllFramePositions();
        UpdateFrameStates();
    }

    // -------------------------------------------------------------------------
    // Lifecycle — detect new parent nodes, drop entries for destroyed ones.
    // -------------------------------------------------------------------------

    void CleanupDestroyedFrames()
    {
        List<Entity> toRemove = null;
        foreach (var entity in builtFrames)
        {
            if (entityManager.Exists(entity)) continue;
            (toRemove ??= new List<Entity>()).Add(entity);
        }
        if (toRemove == null) return;

        foreach (var entity in toRemove)
        {
            if (frameData.TryGetValue(entity, out var data)) data.Destroy();
            frameData.Remove(entity);
            builtFrames.Remove(entity);
        }
    }

    void BuildNewFrames()
    {
        // Composite parents are recognized by having FrameEntry children. Group entries
        // and exits by parent so each new parent gets one BuildFrameFor call with the
        // full anchor set already known.
        var entryByParent = new Dictionary<Entity, Entity>();
        var entries = entryQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        foreach (var e in entries)
        {
            var parent = entityManager.GetComponentData<NodeParent>(e).Parent;
            entryByParent[parent] = e;
        }
        entries.Dispose();

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
            if (builtFrames.Contains(parent)) continue;
            entryByParent.TryGetValue(parent, out var entry);
            exitsByParent.TryGetValue(parent, out var exitList);
            BuildFrameFor(parent, entry, exitList);
            builtFrames.Add(parent);
        }
    }

    // -------------------------------------------------------------------------
    // Build — instantiate GameObjects only. Positions are set by UpdateAllFramePositions.
    // -------------------------------------------------------------------------

    void BuildFrameFor(Entity parent, Entity entryEntity, List<Entity> exitEntities)
    {
        bool hasEntry = entryEntity != Entity.Null;
        bool hasExits = exitEntities != null && exitEntities.Count > 0;
        if (!hasEntry && !hasExits) return;

        var container = new GameObject($"Frame_{parent.Index}");
        container.transform.SetParent(transform, worldPositionStays: false);

        var edgeTransforms = new Transform[4];
        var edgeMaterials  = new Material[4];
        for (int i = 0; i < 4; i++)
        {
            var go = Instantiate(frameEdgePrefab, container.transform);
            edgeTransforms[i] = go.transform;
            edgeMaterials[i]  = go.GetComponent<MeshRenderer>().material;
        }

        Transform entryAnchorTransform = null;
        if (hasEntry)
        {
            var go = Instantiate(pegPrefab, container.transform);
            entryAnchorTransform = go.transform;
        }

        Transform exitAnchorTransform = null;
        bool      exitIsBar           = false;
        if (hasExits)
        {
            exitIsBar = exitEntities.Count > 1;
            var prefab = exitIsBar ? barPrefab : pegPrefab;
            var go     = Instantiate(prefab, container.transform);
            exitAnchorTransform = go.transform;
        }

        frameData[parent] = new FrameData
        {
            Container            = container,
            FrameEdgeTransforms  = edgeTransforms,
            FrameEdgeMaterials   = edgeMaterials,
            EntryAnchorTransform = entryAnchorTransform,
            ExitAnchorTransform  = exitAnchorTransform,
            ExitIsBar            = exitIsBar,
            EntryEntity          = hasEntry ? entryEntity : Entity.Null,
            ExitEntities         = hasExits ? exitEntities.ToArray() : System.Array.Empty<Entity>(),
        };
    }

    // -------------------------------------------------------------------------
    // Per-frame positioning — every frame, every frame piece is moved/scaled
    // from live WorldSpaceTransform reads. AnchorPositions is filled here too.
    // -------------------------------------------------------------------------

    void UpdateAllFramePositions()
    {
        AnchorPositions.Clear();

        foreach (var entity in builtFrames)
        {
            if (!entityManager.Exists(entity)) continue;
            if (!frameData.TryGetValue(entity, out var data)) continue;

            bool hasEntry = data.EntryEntity != Entity.Null && entityManager.Exists(data.EntryEntity);
            bool hasExits = false;
            // Re-read live exit positions, dropping any that vanished mid-frame.
            var exitPositions = new List<Vector3>(data.ExitEntities.Length);
            foreach (var ex in data.ExitEntities)
            {
                if (!entityManager.Exists(ex)) continue;
                Vector3 p = (Vector3)entityManager.GetComponentData<WorldSpaceTransform>(ex).Position;
                exitPositions.Add(p);
                AnchorPositions[ex] = p;
                hasExits = true;
            }

            Vector3 entryPos = Vector3.zero;
            if (hasEntry)
            {
                entryPos = (Vector3)entityManager.GetComponentData<WorldSpaceTransform>(data.EntryEntity).Position;
                AnchorPositions[data.EntryEntity] = entryPos;
            }

            if (!hasEntry && !hasExits) continue;

            PositionFrame(data, hasEntry, entryPos, exitPositions);
        }
    }

    void PositionFrame(FrameData data, bool hasEntry, Vector3 entryPos, List<Vector3> exitPositions)
    {
        bool hasExits = exitPositions.Count > 0;

        // Wall X coordinates: entry on the left wall, exits on the right wall.
        float entryWallX = hasEntry ? entryPos.x : exitPositions[0].x;
        float exitWallX  = hasExits ? exitPositions[0].x : entryPos.x;

        // Vertical extent — union of all anchor Y values.
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

        // Single-lane composites collapse vertically — pad to minFrameHeight so the frame is visible.
        if (maxY - minY < minFrameHeight)
        {
            float midY = (minY + maxY) * 0.5f;
            minY = midY - minFrameHeight * 0.5f;
            maxY = midY + minFrameHeight * 0.5f;
        }

        float midZ = hasEntry ? entryPos.z : exitPositions[0].z;

        // Frame edges — 0=top, 1=bottom, 2=entry wall, 3=exit wall.
        SetHorizontalEdge(data.FrameEdgeTransforms[0], entryWallX, exitWallX, maxY, midZ);
        SetHorizontalEdge(data.FrameEdgeTransforms[1], entryWallX, exitWallX, minY, midZ);
        SetVerticalEdge  (data.FrameEdgeTransforms[2], minY, maxY, entryWallX, midZ);
        SetVerticalEdge  (data.FrameEdgeTransforms[3], minY, maxY, exitWallX,  midZ);

        // Entry peg — sticks out from the entry wall.
        if (hasEntry && data.EntryAnchorTransform != null)
        {
            Vector3 pos = entryPos + new Vector3(-barProtrusion, 0, 0);
            SetPegPerpendicularToVerticalWall(data.EntryAnchorTransform, pos);
        }

        // Exit anchor — peg for single lane, bar spanning the lanes otherwise.
        if (hasExits && data.ExitAnchorTransform != null)
        {
            if (!data.ExitIsBar)
            {
                Vector3 pos = exitPositions[0] + new Vector3(barProtrusion, 0, 0);
                SetPegPerpendicularToVerticalWall(data.ExitAnchorTransform, pos);
            }
            else
            {
                float exitMinY = float.MaxValue, exitMaxY = float.MinValue;
                foreach (var p in exitPositions)
                {
                    exitMinY = Mathf.Min(exitMinY, p.y);
                    exitMaxY = Mathf.Max(exitMaxY, p.y);
                }
                float midY       = (exitMinY + exitMaxY) * 0.5f;
                float spanLength = (exitMaxY - exitMinY) + 2f * barDiameter;
                Vector3 pos      = new Vector3(exitWallX + barProtrusion, midY, midZ);
                SetBarAlongVerticalWall(data.ExitAnchorTransform, pos, spanLength);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Geometry transforms — set position+rotation+scale every frame.
    // -------------------------------------------------------------------------

    static void SetHorizontalEdge(Transform t, float minX, float maxX, float y, float z)
    {
        t.position   = new Vector3((minX + maxX) * 0.5f, y, z);
        t.rotation   = Quaternion.identity;
        t.localScale = new Vector3(maxX - minX, FrameEdgeThickness, FrameEdgeThickness);
    }

    static void SetVerticalEdge(Transform t, float minY, float maxY, float x, float z)
    {
        t.position   = new Vector3(x, (minY + maxY) * 0.5f, z);
        t.rotation   = Quaternion.Euler(0, 0, 90);
        t.localScale = new Vector3(maxY - minY, FrameEdgeThickness, FrameEdgeThickness);
    }

    void SetPegPerpendicularToVerticalWall(Transform t, Vector3 pos)
    {
        // Cylinder's local long axis is Y. Rotate 90° on Z so it sticks out along world X
        // (perpendicular to the vertical entry/exit wall). Length = 2 * barProtrusion.
        t.position   = pos;
        t.rotation   = Quaternion.Euler(0, 0, 90);
        t.localScale = new Vector3(pegDiameter, barProtrusion, pegDiameter);
    }

    void SetBarAlongVerticalWall(Transform t, Vector3 pos, float spanLength)
    {
        // Capsule's local long axis is Y. Keep it that way — bar runs along the vertical wall.
        // Default capsule height is 2 units; localScale.y = spanLength / 2 produces a bar of spanLength.
        t.position   = pos;
        t.rotation   = Quaternion.identity;
        t.localScale = new Vector3(barDiameter, spanLength * 0.5f, barDiameter);
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

        foreach (var kv in frameData)
        {
            FrameState state = states.TryGetValue(kv.Key, out var s) ? s : FrameState.Normal;
            Color c = state switch
            {
                FrameState.Pressured => pressuredColor,
                FrameState.Active    => activeColor,
                _                    => normalColor,
            };
            foreach (var mat in kv.Value.FrameEdgeMaterials)
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
