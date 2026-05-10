using UnityEngine;
using Unity.Entities;

/// <summary>
/// Rendering helpers for debug visualizers.
/// Materials must be created as project assets and assigned via inspector —
/// never created at runtime via Shader.Find. URP strips unreferenced shaders
/// from builds, which causes runtime material creation to fall back to the
/// magenta error shader.
/// </summary>
public static class RenderingUtils
{
    /// <summary>
    /// Applies a color to a URP material. Sets both _Color and _BaseColor since
    /// URP shaders use _BaseColor and Material.color only writes _Color.
    /// Reserved for one-off color adjustments — visualizers swap material references instead.
    /// </summary>
    public static void ApplyColor(Material mat, Color color)
    {
        mat.color = color;
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
    }

    /// <summary>
    /// Sets the color of a LineRenderer's material in place.
    /// Do NOT use lr.startColor / lr.endColor — those rely on vertex color sampling
    /// which URP Unlit does not perform.
    /// Reserved for one-off color adjustments — visualizers swap material references instead.
    /// </summary>
    public static void SetLineColor(LineRenderer lr, Color color)
    {
        if (lr.material != null)
            ApplyColor(lr.material, color);
    }

    /// <summary>
    /// True when both endpoints are children of the same parent node — i.e.,
    /// the edge lives entirely inside one composite's internal graph.
    /// </summary>
    public static bool IsInternalEdge(EntityManager em, Edge edge)
    {
        if (!em.HasComponent<NodeParent>(edge.FromNode)) return false;
        if (!em.HasComponent<NodeParent>(edge.ToNode))   return false;
        var fromParent = em.GetComponentData<NodeParent>(edge.FromNode).Parent;
        var toParent   = em.GetComponentData<NodeParent>(edge.ToNode).Parent;
        return fromParent == toParent;
    }

    /// <summary>
    /// World-space position to use for the visual endpoint of an edge connecting to <paramref name="node"/>.
    /// FrameEntry / FrameExit entities are positioned exactly on their wall, so the lookup just
    /// returns the anchor's world position; otherwise we fall back to the node's NodeTransform.
    /// Entry and exit are symmetric — there's no separate entry/exit method because the anchor entity
    /// itself encodes which wall it lives on.
    /// </summary>
    public static Vector3 ResolvePosition(EntityManager em, NodeFrameVisualizer nfv, Entity node)
    {
        if (nfv != null && nfv.AnchorPositions.TryGetValue(node, out var pos))
            return pos;
        return (Vector3)em.GetComponentData<NodeTransform>(node).Position;
    }
}
