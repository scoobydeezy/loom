using Unity.Entities;
using Unity.Collections;

// Baked from NodeTypeDefinition at spawn time. The ScriptableObject never enters the ECS world;
// this component is the runtime identity record for display and future query/filter work.
public struct NodeType : IComponentData
{
    public FixedString64Bytes TypeName;
}
