using Unity.Entities;

/// <summary>
/// Marks this entity as an exit point of its parent node's frame.
/// Physically positioned on the exit wall of the frame.
/// Internal edges arrive here from the frame interior. External edges depart from here into the world.
/// </summary>
public struct FrameExit : IComponentData { }
