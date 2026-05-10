using System;
using Unity.Entities;

/// <summary>
/// A stable, persistent identity for any ECS entity.
/// Assigned once at spawn. Never changes. Never reused.
/// Use this in commands, save files, scenario scripts, and replay logs
/// instead of Entity, which is a runtime index and not stable across sessions.
/// </summary>
[Serializable]
public struct StableId : IComponentData, IEquatable<StableId>
{
    public ulong Value;

    public bool Equals(StableId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is StableId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(StableId a, StableId b) => a.Value == b.Value;
    public static bool operator !=(StableId a, StableId b) => a.Value != b.Value;
}
