using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Local-space position and rotation relative to the parent node.
/// For root-level entities (no NodeParent), this is world space.
/// NEVER read this directly for world-space operations — use WorldSpaceTransform.
/// </summary>
public struct NodeTransform : IComponentData
{
    public float3     Position; // local space
    public quaternion Rotation; // local space
}
