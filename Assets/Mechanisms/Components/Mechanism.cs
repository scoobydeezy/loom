using Unity.Entities;

// Marker component. Position is stored in NodeTransform (local) and WorldSpaceTransform (world)
// so renderers need no special casing.
public struct Mechanism : IComponentData { }
