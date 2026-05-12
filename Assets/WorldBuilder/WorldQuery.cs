using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Read-only façade for querying ECS world state from editor UI and tools.
/// Never exposes EntityManager directly. Grows as UI needs grow.
///
/// This is the seam for replay, ghost previews, diff views, and spectator mode —
/// every read goes through here so future features can intercept queries
/// without touching call sites.
/// </summary>
public class WorldQuery : MonoBehaviour
{
    public static WorldQuery Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    EntityManager EM
    {
        get
        {
            var w = World.DefaultGameObjectInjectionWorld;
            return w != null ? w.EntityManager : default;
        }
    }

    /// <summary>All root-level entities (no NodeParent).</summary>
    public IEnumerable<StableId> GetRootEntities()
    {
        var em = EM;
        var result = new List<StableId>();
        using var q   = em.CreateEntityQuery(ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<StableId>());
        using var all = q.ToEntityArray(Allocator.Temp);
        foreach (var e in all)
            if (!em.HasComponent<NodeParent>(e))
                result.Add(em.GetComponentData<StableId>(e));
        return result;
    }

    /// <summary>All direct children of the given context node. Pass default(StableId) for the root context.</summary>
    public IEnumerable<StableId> GetEntitiesInContext(StableId contextNode)
    {
        if (contextNode.Value == 0) return GetRootEntities();

        var em     = EM;
        var parent = StableIdAllocator.Resolve(em, contextNode);
        var result = new List<StableId>();
        if (parent == Entity.Null) return result;

        using var q   = em.CreateEntityQuery(ComponentType.ReadOnly<NodeParent>(), ComponentType.ReadOnly<StableId>());
        using var all = q.ToEntityArray(Allocator.Temp);
        foreach (var e in all)
            if (em.GetComponentData<NodeParent>(e).Parent == parent)
                result.Add(em.GetComponentData<StableId>(e));
        return result;
    }

    /// <summary>World-space position of an entity.</summary>
    public bool TryGetWorldPosition(StableId id, out Vector3 position)
    {
        position = Vector3.zero;
        var em = EM;
        Entity e = StableIdAllocator.Resolve(em, id);
        if (e == Entity.Null || !em.HasComponent<WorldSpaceTransform>(e)) return false;
        position = em.GetComponentData<WorldSpaceTransform>(e).Position;
        return true;
    }

    /// <summary>Local-space bounds of an entity.</summary>
    public bool TryGetBounds(StableId id, out Vector2 size)
    {
        size = Vector2.zero;
        var em = EM;
        Entity e = StableIdAllocator.Resolve(em, id);
        if (e == Entity.Null || !em.HasComponent<NodeBounds>(e)) return false;
        size = em.GetComponentData<NodeBounds>(e).Size;
        return true;
    }

    /// <summary>All edges connected to an entity (as FromNode or ToNode).</summary>
    public IEnumerable<StableId> GetConnectedEdges(StableId id)
    {
        var em = EM;
        var result = new List<StableId>();
        Entity target = StableIdAllocator.Resolve(em, id);
        if (target == Entity.Null) return result;

        using var q   = em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<StableId>());
        using var all = q.ToEntityArray(Allocator.Temp);
        foreach (var e in all)
        {
            var edge = em.GetComponentData<Edge>(e);
            if (edge.FromNode == target || edge.ToNode == target)
                result.Add(em.GetComponentData<StableId>(e));
        }
        return result;
    }

    /// <summary>Resolve a StableId to a runtime Entity. For internal use by commands only.</summary>
    internal Entity Resolve(StableId id) => StableIdAllocator.Resolve(EM, id);

    /// <summary>
    /// Display name for a node — from <c>NodeType.TypeName</c> when present,
    /// otherwise the StableId rendered as a short label.
    /// </summary>
    public string GetDisplayName(StableId id)
    {
        if (id.Value == 0) return "Root";
        var em = EM;
        Entity e = StableIdAllocator.Resolve(em, id);
        if (e == Entity.Null) return $"#{id.Value}";
        if (em.HasComponent<NodeType>(e))
        {
            var nt = em.GetComponentData<NodeType>(e);
            if (nt.TypeName.Length > 0) return nt.TypeName.ToString();
        }
        return $"#{id.Value}";
    }

    /// <summary>
    /// All NodeTypeDefinition assets available for placement on the canvas.
    /// Loaded from <c>Resources/NodeTypes/</c> so the set is data-driven —
    /// no hardcoded list in the controller.
    /// </summary>
    public IEnumerable<NodeTypeDefinition> GetPlaceableNodeTypes()
        => Resources.LoadAll<NodeTypeDefinition>("NodeTypes");

    /// <summary>
    /// Converts a UI Toolkit screen-space position to a world-space position on
    /// the simulation plane. UI Toolkit uses a top-left origin; Camera.ScreenToWorldPoint
    /// uses bottom-left, so Y is flipped before unprojecting.
    /// </summary>
    public Vector3 ScreenToWorld(Vector2 screenPos)
    {
        var cam = Camera.main;
        if (cam == null) return Vector3.zero;

        Vector3 flipped = new Vector3(screenPos.x, Screen.height - screenPos.y, 0f);
        if (cam.orthographic)
        {
            flipped.z = -cam.transform.position.z;
            var world = cam.ScreenToWorldPoint(flipped);
            world.z   = 0f;
            return world;
        }

        Ray   ray = cam.ScreenPointToRay(flipped);
        float t   = -ray.origin.z / ray.direction.z;
        var   p   = ray.origin + ray.direction * t;
        p.z = 0f;
        return p;
    }
}
