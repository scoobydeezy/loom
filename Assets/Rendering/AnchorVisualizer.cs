using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Renders a small translucent circle at every FrameEntry and FrameExit anchor in the
/// current canvas context. Circles are the visible hit zone for the connect gesture —
/// <see cref="AnchorHitRadius"/> drives both the visual size and the InputHandler hit
/// test, so they are guaranteed to match.
///
/// Colors come from <see cref="DebugColors"/>:
///   - default (not connecting): dim white
///   - connect source:           bright cyan
///   - valid FrameEntry target:  green
///   - invalid FrameEntry target / FrameExit that isn't the source: dim
///
/// Self-frame leaves (top-level entities with FrameEntry+FrameExit on the same entity)
/// get two circles offset to the entry and exit walls. Composite children sit at their
/// own world position, which is already on the parent's wall.
/// </summary>
[DefaultExecutionOrder(250)]
public class AnchorVisualizer : MonoBehaviour
{
    /// <summary>
    /// Radius of the anchor hit zone in world units.
    /// InputHandler uses this same value for click detection — change here to resize
    /// both the visual and the hit area simultaneously.
    /// </summary>
    public const float AnchorHitRadius = 0.3f;

    const float LineWidth = 0.03f;
    const int   Segments  = 24;

    [Header("Visibility")]
    public bool showAnchorsInEditMode = true;
    public bool showAnchorsInPlayMode = false;

    [Header("Material")]
    [Tooltip("AnchorCircle.mat — URP Lit (or Unlit) with Surface Type set to Transparent.")]
    public Material anchorMaterial;

    readonly Dictionary<Entity, LineRenderer> entryCircles = new();
    readonly Dictionary<Entity, LineRenderer> exitCircles  = new();

    EntityManager entityManager;
    EntityQuery   entryQuery;
    EntityQuery   exitQuery;
    EntityQuery   parentQuery;

    bool queriesReady;

    void EnsureQueries()
    {
        if (queriesReady) return;
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null) return;

        entityManager = world.EntityManager;
        // NodeParent is NOT required — top-level leaves carry the tags on themselves.
        entryQuery  = entityManager.CreateEntityQuery(typeof(FrameEntry), typeof(WorldSpaceTransform));
        exitQuery   = entityManager.CreateEntityQuery(typeof(FrameExit),  typeof(WorldSpaceTransform));
        parentQuery = entityManager.CreateEntityQuery(typeof(NodeParent));
        queriesReady = true;
    }

    void Update()
    {
        EnsureQueries();
        if (!queriesReady) return;
        if (anchorMaterial == null) return;

        var state = EditorState.Instance;
        bool show = state == null
            ? showAnchorsInEditMode
            : (state.Mode == LoomMode.Edit ? showAnchorsInEditMode : showAnchorsInPlayMode);

        // hasChildren — same discriminator used by NodeFrameVisualizer to decide whether
        // a top-level entity's self-tags drive a self-frame (no children) or are ignored
        // in favor of its composite children (has children).
        var children = parentQuery.ToEntityArray(Allocator.Temp);
        var hasChildren = new HashSet<Entity>();
        for (int i = 0; i < children.Length; i++)
            hasChildren.Add(entityManager.GetComponentData<NodeParent>(children[i]).Parent);
        children.Dispose();

        CleanupDestroyed(entryCircles);
        CleanupDestroyed(exitCircles);

        var entries = entryQuery.ToEntityArray(Allocator.Temp);
        var exits   = exitQuery.ToEntityArray(Allocator.Temp);

        UpdateAnchors(entries, isEntry: true,  visible: show, hasChildren: hasChildren);
        UpdateAnchors(exits,   isEntry: false, visible: show, hasChildren: hasChildren);

        entries.Dispose();
        exits.Dispose();
    }

    // ---------------------------------------------------------------------
    // Shared anchor world-position helper — used by InputHandler hit testing
    // and by EdgeVisualizer / PacketVisualizer for endpoint resolution.
    //
    // Composite children sit AT their parent's wall (WST is the anchor).
    // Self-frame leaves sit at center; the anchor lives at WST + NodeAnchors offset.
    // ---------------------------------------------------------------------

    public static Vector3 GetAnchorWorldPosition(EntityManager em, Entity anchor, bool isExit)
    {
        if (!em.HasComponent<WorldSpaceTransform>(anchor)) return Vector3.zero;
        var wst = em.GetComponentData<WorldSpaceTransform>(anchor);

        if (em.HasComponent<NodeParent>(anchor))
            return wst.Position;

        if (em.HasComponent<NodeAnchors>(anchor))
        {
            var na     = em.GetComponentData<NodeAnchors>(anchor);
            var local  = isExit ? na.ExitLocal : na.EntryLocal;
            return wst.Position + math.rotate(wst.Rotation, local);
        }
        return wst.Position;
    }

    // ---------------------------------------------------------------------
    // Anchor context test — single source of truth for "is this anchor part
    // of what the user is currently looking at?". Used by InputHandler for
    // hit-testing, by WorldQuery for connection validation, and by this
    // visualizer for visibility filtering. Keeping the rule in one place
    // prevents the three layers from drifting apart.
    //
    // An anchor is in context C when EITHER:
    //   (A) it is a direct child of C — i.e. a self-frame leaf placed at C, or
    //   (B) it is a grandchild of C — i.e. an immediate FrameEntry/FrameExit
    //       child of some node placed at C (which is how composite frames
    //       expose their wall anchors at the parent canvas level).
    // C = default(StableId) means the root canvas; the parent of root is "no
    // parent at all", so a top-level node's parent matches root.
    // ---------------------------------------------------------------------

    public static bool IsAnchorInContext(EntityManager em, Entity anchor, StableId contextId)
    {
        Entity ctx;
        if (contextId.Value == 0)
        {
            ctx = Entity.Null;
        }
        else
        {
            ctx = StableIdAllocator.Resolve(em, contextId);
            if (ctx == Entity.Null) return false;
        }
        return IsAnchorInContextEntity(em, anchor, ctx);
    }

    static bool IsAnchorInContextEntity(EntityManager em, Entity anchor, Entity ctx)
    {
        Entity parent = em.HasComponent<NodeParent>(anchor)
            ? em.GetComponentData<NodeParent>(anchor).Parent
            : Entity.Null;

        if (parent == ctx) return true; // case A: direct child of ctx

        // case B: anchor's parent (a composite) is itself a direct child of ctx
        if (parent == Entity.Null) return false;
        Entity grandparent = em.HasComponent<NodeParent>(parent)
            ? em.GetComponentData<NodeParent>(parent).Parent
            : Entity.Null;
        return grandparent == ctx;
    }

    // ---------------------------------------------------------------------
    // Per-frame anchor pass
    // ---------------------------------------------------------------------

    void UpdateAnchors(NativeArray<Entity> anchors, bool isEntry, bool visible, HashSet<Entity> hasChildren)
    {
        var dict = isEntry ? entryCircles : exitCircles;

        foreach (var entity in anchors)
        {
            bool hasParent = entityManager.HasComponent<NodeParent>(entity);
            // Top-level composite: its self-tags are ignored — the children carry the visible anchors.
            if (!hasParent && hasChildren.Contains(entity)) { Hide(dict, entity); continue; }

            if (!IsInCurrentContext(entity, hasParent)) { Hide(dict, entity); continue; }
            if (!visible)                               { Hide(dict, entity); continue; }

            if (!dict.TryGetValue(entity, out var lr))
                lr = CreateCircle(dict, entity, isEntry);

            lr.enabled = true;
            Vector3 pos = GetAnchorWorldPosition(entityManager, entity, isExit: !isEntry);
            SetCirclePositions(lr, pos, AnchorHitRadius);

            Color col = DetermineColor(entity, isEntry);
            RenderingUtils.SetLineColor(lr, col);
        }
    }

    Color DetermineColor(Entity entity, bool isEntry)
    {
        var state = EditorState.Instance;
        if (state == null || !state.IsConnecting)
            return DebugColors.AnchorDefault;

        if (entityManager.HasComponent<StableId>(entity))
        {
            var stableId = entityManager.GetComponentData<StableId>(entity);
            if (stableId.Value == state.ConnectSource.Value)
                return DebugColors.AnchorActive;
        }

        // Only FrameEntry anchors are valid targets — FrameExit circles stay dim.
        if (!isEntry) return DebugColors.AnchorDefault;

        bool valid = WorldQuery.Instance != null
                     && WorldQuery.Instance.IsValidConnectionTarget(state.ConnectSource, entity);
        return valid ? DebugColors.AnchorHighlight : DebugColors.AnchorInvalid;
    }

    bool IsInCurrentContext(Entity entity, bool _hasParent)
    {
        var state = EditorState.Instance;
        if (state == null) return true;
        return IsAnchorInContext(entityManager, entity, state.CurrentContext.NodeId);
    }

    // ---------------------------------------------------------------------
    // LineRenderer lifecycle
    // ---------------------------------------------------------------------

    void Hide(Dictionary<Entity, LineRenderer> dict, Entity entity)
    {
        if (dict.TryGetValue(entity, out var lr) && lr != null)
            lr.enabled = false;
    }

    void CleanupDestroyed(Dictionary<Entity, LineRenderer> dict)
    {
        List<Entity> stale = null;
        foreach (var kv in dict)
            if (!entityManager.Exists(kv.Key))
                (stale ??= new List<Entity>()).Add(kv.Key);
        if (stale == null) return;
        foreach (var e in stale)
        {
            if (dict.TryGetValue(e, out var lr) && lr != null)
                Destroy(lr.gameObject);
            dict.Remove(e);
        }
    }

    LineRenderer CreateCircle(Dictionary<Entity, LineRenderer> dict, Entity entity, bool isEntry)
    {
        var go = new GameObject($"Anchor_{(isEntry ? "Entry" : "Exit")}_{entity.Index}");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.loop           = true;
        lr.useWorldSpace  = true;
        lr.startWidth     = LineWidth;
        lr.endWidth       = LineWidth;
        lr.material       = anchorMaterial;
        dict[entity] = lr;
        return lr;
    }

    static void SetCirclePositions(LineRenderer lr, Vector3 center, float radius, int segments = Segments)
    {
        lr.positionCount = segments + 1;
        for (int i = 0; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            lr.SetPosition(i, center + new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius,
                0f));
        }
    }
}
