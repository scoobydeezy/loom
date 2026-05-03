using Unity.Entities;

// Tag added when a packet has completed its current edge but cannot yet forward onto the next one.
// The packet holds no occupancy on any edge while this tag is present.
// PacketTraverseSystem retries forwarding each frame until the outbound edge has capacity.
public struct WaitingAtNode : IComponentData { }
