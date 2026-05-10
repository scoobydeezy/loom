/// <summary>
/// Editor-wide mode. First-class concept rather than a UI state.
/// Edit:  simulation paused; structural mutations permitted; commands recorded for undo.
/// Play:  simulation running; mutations route through SimulationEventBus with recordForUndo: false.
/// </summary>
public enum LoomMode { Edit, Play }
