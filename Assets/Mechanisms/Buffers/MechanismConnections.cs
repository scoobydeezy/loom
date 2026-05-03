using Unity.Entities;

/// <summary>
/// Outbound edges this mechanism can route packets onto.
/// Belongs exclusively on Mechanism entities — nodes are passive and never hold this buffer.
/// </summary>
public struct MechanismConnections : IBufferElementData
{
    public Entity Edge;
}
