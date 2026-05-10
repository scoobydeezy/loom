using Unity.Entities;

/// <summary>
/// Tag: this entity's NodeTransform has changed this frame.
/// WorldSpaceCacheSystem uses this to identify subtrees needing recomputation.
/// Currently all transforms recompute every frame — this tag enables
/// subtree-only optimization when graphs grow large. Add it now; optimize later.
/// </summary>
public struct TransformDirty : IComponentData { }
