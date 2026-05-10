using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Entry and exit anchor positions in this node's local space.
/// World-space anchor positions are derived by transforming through WorldSpaceTransform.
/// Entry = where external edges arrive. Exit = where external edges depart.
/// For nodes with multiple exits, ExitLocal is the centroid of all exit positions.
/// </summary>
public struct NodeAnchors : IComponentData
{
    public float3 EntryLocal;
    public float3 ExitLocal;
}
