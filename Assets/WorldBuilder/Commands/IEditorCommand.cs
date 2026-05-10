using Unity.Entities;

/// <summary>
/// All structural mutations to the ECS world implement this interface.
///
/// Implementations MUST be [Serializable] and contain only primitives, StableIds,
/// and value types — never Entity references. The attribute cannot be applied to
/// an interface in C#, but it is a hard requirement on every concrete command.
///
/// Execute applies the command. Undo reverses it. Both receive the live
/// EntityManager, EditorState, and StableIdRegistry — commands resolve
/// StableId → Entity at execution time, never store Entity directly.
/// </summary>
public interface IEditorCommand
{
    void Execute(EntityManager em, EditorState state, StableIdRegistry registry);
    void Undo   (EntityManager em, EditorState state, StableIdRegistry registry);
}
