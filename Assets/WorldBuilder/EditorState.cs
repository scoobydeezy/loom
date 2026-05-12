using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;

/// <summary>
/// Singleton MonoBehaviour holding editor-side state — mode, selection, current canvas
/// context, and any in-progress operations (e.g. edge connect).
/// All mutation flows through Apply* methods invoked by commands or InputHandler;
/// this class never mutates ECS structure directly.
/// </summary>
public class EditorState : MonoBehaviour
{
    public static EditorState Instance { get; private set; }

    public LoomMode           Mode             { get; private set; } = LoomMode.Edit;
    public HashSet<StableId>  SelectedEntities { get; private set; } = new HashSet<StableId>();
    public CanvasContext      CurrentContext   { get; private set; } = new CanvasContext();
    public bool               IsConnecting     { get; private set; }
    public StableId           ConnectSource    { get; private set; }

    /// <summary>Fired after Mode changes. Use for enabling/disabling simulation, repainting UI.</summary>
    public event System.Action<LoomMode> OnModeChanged;
    /// <summary>
    /// Fired after the selection set changes. Payload is the new selection.
    /// Typed as <see cref="IReadOnlyCollection{T}"/> rather than IReadOnlySet&lt;T&gt;
    /// because the latter is not in the BCL Unity 6 ships with under
    /// apiCompatibilityLevel: .NET Framework.
    /// </summary>
    public event System.Action<IReadOnlyCollection<StableId>> OnSelectionChanged;
    /// <summary>Fired after the context stack changes (push or pop). Payload is the full stack, root-first.</summary>
    public event System.Action<IReadOnlyList<CanvasContext>> OnContextChanged;
    /// <summary>Fired when an edge-connect operation starts.</summary>
    public event System.Action OnConnectBegan;
    /// <summary>Fired when an edge-connect operation ends (committed or cancelled).</summary>
    public event System.Action OnConnectEnded;

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

    internal void ApplyModeChange(LoomMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        ApplySimulationEnabled(mode == LoomMode.Play);
        OnModeChanged?.Invoke(Mode);
    }

    internal void ApplySelection(HashSet<StableId> ids)
    {
        SelectedEntities = ids ?? new HashSet<StableId>();
        OnSelectionChanged?.Invoke(SelectedEntities);
    }

    internal void ApplyContextPush(StableId node)
    {
        CanvasNavigator.Instance.Push(node);
        CurrentContext = CanvasNavigator.Instance.Current;
        OnContextChanged?.Invoke(CanvasNavigator.Instance.Stack);
    }

    internal void ApplyContextPop()
    {
        CanvasNavigator.Instance.Pop();
        CurrentContext = CanvasNavigator.Instance.Current;
        OnContextChanged?.Invoke(CanvasNavigator.Instance.Stack);
    }

    internal void BeginConnect(StableId anchor)
    {
        IsConnecting  = true;
        ConnectSource = anchor;
        OnConnectBegan?.Invoke();
    }

    internal void EndConnect()
    {
        if (!IsConnecting) return;
        IsConnecting  = false;
        ConnectSource = default;
        OnConnectEnded?.Invoke();
    }

    /// <summary>
    /// Toggle the SimulationSystemGroup based on mode. Edit mode pauses it; Play mode runs it.
    /// Centralized here so SetModeCommand stays serializable — only the data lives in commands.
    /// </summary>
    static void ApplySimulationEnabled(bool enabled)
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null) return;
        var group = world.GetExistingSystemManaged<SimulationSystemGroup>();
        if (group != null) group.Enabled = enabled;
    }
}
