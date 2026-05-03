using Unity.Entities;

// Tag added when a packet has completed its current edge and is waiting for the next edge to be selected.
// Current node is derived from em.GetComponentData<Edge>(packet.CurrentEdge).ToNode.
public struct AwaitingRouting : IComponentData { }
