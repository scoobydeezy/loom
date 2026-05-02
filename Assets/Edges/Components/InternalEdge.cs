using Unity.Entities;

public struct InternalEdge : IComponentData
{
    public InternalEdgeRole Role;
    public int LaneIndex;
}

public enum InternalEdgeRole : byte
{
    Entry,
    Lane,
    Exit
}
