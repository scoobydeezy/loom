/// <summary>
/// Observable identity color carried by a packet. Drives the packet's
/// rendered base color; simulation state is layered on top as a tint
/// modifier, never a replacement.
/// </summary>
public enum PacketColor
{
    White,
    Red,
    Blue,
    Green,
    Yellow,
    Purple,
    Orange
}

/// <summary>
/// Observable identity shape carried by a packet. Drives the mesh
/// swapped in by PacketVisualizer on edge transition.
/// </summary>
public enum PacketShape
{
    Sphere,   // default — current packet visual
    Cube,
    Diamond,  // rotated cube
    Cylinder
}
