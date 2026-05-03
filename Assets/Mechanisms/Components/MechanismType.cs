using Unity.Entities;

public enum MechanismKind { Route, Filter, RateLimit }

public struct MechanismType : IComponentData
{
    public MechanismKind Kind;
}
