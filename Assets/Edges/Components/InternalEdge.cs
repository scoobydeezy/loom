using Unity.Entities;

// Tag on edges created inside a node's internal graph by SpawnNode.
// Absent on global edges wired by LoomBootstrap.Start — those connect top-level node boundaries.
public struct InternalEdge : IComponentData { }
