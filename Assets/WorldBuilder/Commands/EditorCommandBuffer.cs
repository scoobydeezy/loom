using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// The single entry point for structural mutations to the ECS world. All editor
/// tooling, hotkeys, input handlers, and the simulation event bus route through
/// Execute. Maintains undo/redo stacks, supports transactions, and stamps
/// TopologyVersion on every outermost commit.
///
/// Simulation systems must NOT call this directly — they raise events on
/// SimulationEventBus, which forwards with recordForUndo: false.
/// </summary>
public class EditorCommandBuffer : MonoBehaviour
{
    public static EditorCommandBuffer Instance { get; private set; }

    readonly Stack<IEditorCommand> undoStack = new Stack<IEditorCommand>();
    readonly Stack<IEditorCommand> redoStack = new Stack<IEditorCommand>();

    bool                       inTransaction;
    int                        transactionDepth;
    bool                       transactionRecord;
    readonly List<IEditorCommand> transactionBatch = new List<IEditorCommand>();

    EntityManager entityManager;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    EntityManager EM
    {
        get
        {
            var world = World.DefaultGameObjectInjectionWorld;
            return world != null ? world.EntityManager : default;
        }
    }

    EditorState State => EditorState.Instance;

    StableIdRegistry Registry
    {
        get
        {
            var em = EM;
            var q  = em.CreateEntityQuery(typeof(StableIdRegistry));
            return q.CalculateEntityCount() > 0 ? q.GetSingleton<StableIdRegistry>() : default;
        }
    }

    /// <summary>
    /// Begin a transaction. Subsequent Execute calls accumulate into a single
    /// BatchCommand. Nested calls are allowed; the outermost EndTransaction commits.
    /// </summary>
    public void BeginTransaction(bool recordForUndo = true)
    {
        if (transactionDepth == 0)
        {
            transactionBatch.Clear();
            transactionRecord = recordForUndo;
        }
        transactionDepth++;
        inTransaction = true;
    }

    /// <summary>
    /// End the current transaction. The outermost call wraps the accumulated
    /// commands into a BatchCommand, optionally pushes it onto the undo stack,
    /// and stamps TopologyVersion exactly once.
    /// </summary>
    public void EndTransaction()
    {
        if (transactionDepth == 0) return;
        transactionDepth--;
        if (transactionDepth > 0) return;

        var batch = new BatchCommand(new List<IEditorCommand>(transactionBatch));
        transactionBatch.Clear();
        inTransaction = false;

        if (transactionRecord)
        {
            undoStack.Push(batch);
            redoStack.Clear();
        }

        TopologyVersion.Increment(EM);
    }

    /// <summary>
    /// Execute a command. Outside a transaction, this is one undo step and one
    /// topology stamp. Inside a transaction, the command is buffered until commit.
    /// </summary>
    public void Execute(IEditorCommand command, bool recordForUndo = true)
    {
        var em = EM;
        var registry = Registry;
        command.Execute(em, State, registry);

        if (inTransaction)
        {
            if (transactionRecord && recordForUndo)
                transactionBatch.Add(command);
            return;
        }

        if (recordForUndo)
        {
            undoStack.Push(command);
            redoStack.Clear();
        }

        TopologyVersion.Increment(em);
    }

    /// <summary>Pop the top of the undo stack, run its Undo, and push onto the redo stack.</summary>
    public void Undo()
    {
        if (undoStack.Count == 0) return;
        var cmd = undoStack.Pop();
        cmd.Undo(EM, State, Registry);
        redoStack.Push(cmd);
        TopologyVersion.Increment(EM);
    }

    /// <summary>Pop the top of the redo stack, re-Execute it, and push back onto undo.</summary>
    public void Redo()
    {
        if (redoStack.Count == 0) return;
        var cmd = redoStack.Pop();
        cmd.Execute(EM, State, Registry);
        undoStack.Push(cmd);
        TopologyVersion.Increment(EM);
    }

    public int UndoCount => undoStack.Count;
    public int RedoCount => redoStack.Count;
}
