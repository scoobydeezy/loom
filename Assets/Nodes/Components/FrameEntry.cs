using Unity.Entities;

/// <summary>
/// Marks this entity as the entry point of its parent node's frame.
/// Physically positioned on the entry wall of the frame.
/// External edges arrive here from outside. Internal edges depart from here into the frame interior.
/// </summary>
public struct FrameEntry : IComponentData { }
