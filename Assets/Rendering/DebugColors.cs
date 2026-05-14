using UnityEngine;

/// <summary>
/// Named color constants used by editor visualizers (anchor circles, peg tinting,
/// ghost edges). Centralizing them keeps the palette consistent across visualizers
/// and prevents magic-number colors from creeping into logic.
/// Alpha matters only for renderers using transparent materials (e.g. AnchorCircle.mat).
/// </summary>
public static class DebugColors
{
    public static readonly Color AnchorDefault   = new Color(1f,   1f,   1f,   0.4f);
    public static readonly Color AnchorHighlight = new Color(0.2f, 0.9f, 0.3f, 0.8f);
    public static readonly Color AnchorInvalid   = new Color(0.8f, 0.2f, 0.2f, 0.4f);
    public static readonly Color AnchorActive    = new Color(0.0f, 1.0f, 1.0f, 0.9f);
    public static readonly Color GhostEdge       = new Color(1f,   1f,   1f,   0.4f);
}
