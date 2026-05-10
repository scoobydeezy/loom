using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Size of this node in its own local space.
/// Used for hit testing, selection, layout, anchor computation, and visual feedback.
/// Set at spawn time from NodeTypeDefinition.frameWidth and frameHeight.
/// </summary>
public struct NodeBounds : IComponentData
{
    public float2 Size; // x = width, y = height
}
