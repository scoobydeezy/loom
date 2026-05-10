using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Cached world-space position and rotation for this entity.
/// Populated every frame by WorldSpaceCacheSystem.
/// Read this for all world-space operations — rendering, distance, hit testing.
/// </summary>
public struct WorldSpaceTransform : IComponentData
{
    public float3     Position;
    public quaternion Rotation;
}
