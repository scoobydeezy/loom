using System;
using System.Collections.Generic;
using Unity.Entities;

/// <summary>
/// Wraps a list of commands so they execute and undo as a unit.
/// Used by EditorCommandBuffer transactions — Begin/EndTransaction collects all
/// commands into a single BatchCommand pushed onto the undo stack.
/// Undo executes commands in reverse order.
/// </summary>
[Serializable]
public class BatchCommand : IEditorCommand
{
    public List<IEditorCommand> Commands;

    public BatchCommand() { Commands = new List<IEditorCommand>(); }
    public BatchCommand(List<IEditorCommand> commands) { Commands = commands ?? new List<IEditorCommand>(); }

    public void Execute(EntityManager em, EditorState state, StableIdRegistry registry)
    {
        for (int i = 0; i < Commands.Count; i++)
            Commands[i].Execute(em, state, registry);
    }

    public void Undo(EntityManager em, EditorState state, StableIdRegistry registry)
    {
        for (int i = Commands.Count - 1; i >= 0; i--)
            Commands[i].Undo(em, state, registry);
    }
}
