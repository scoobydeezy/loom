using Unity.Entities;

/// <summary>
/// Marks a node as a packet source. Emits packets at EmitRate per second
/// with the specified Color and Shape. Packets are injected onto the single
/// outbound edge from this node.
///
/// The simulation has no concept of "destination" — color and shape are the
/// packet's entire identity. Mechanisms route by congestion (and, in Phase 4,
/// by reading these identity fields).
///
/// Backpressure is honest: when the outbound edge entry point is physically
/// blocked, the accumulator continues to grow and the source emits as soon
/// as space clears. Packets are never dropped at the source.
/// </summary>
public struct PacketSource : IComponentData
{
    public float       EmitRate;     // packets per second
    public PacketColor Color;
    public PacketShape Shape;
    public float       Accumulator;  // internal — tracks fractional packet debt
}
