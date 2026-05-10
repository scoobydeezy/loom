using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Resolves and holds <see cref="InputAction"/> references for editor input.
/// The bound <see cref="InputActionAsset"/> is the single source of truth for
/// every binding — no key codes live in C#. A future remapping UI mutates the
/// asset; nothing in code needs to change.
/// </summary>
public class InputBindings : MonoBehaviour
{
    public static InputBindings Instance { get; private set; }

    [SerializeField] InputActionAsset actionAsset;

    public InputAction Select        { get; private set; }
    public InputAction Drag          { get; private set; }
    public InputAction Pan           { get; private set; }
    public InputAction Zoom          { get; private set; }
    public InputAction Cancel        { get; private set; }
    public InputAction Confirm       { get; private set; }
    public InputAction ToggleMode    { get; private set; }
    public InputAction Undo          { get; private set; }
    public InputAction Redo          { get; private set; }
    public InputAction ExitContext   { get; private set; }
    public InputAction MousePosition { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (actionAsset == null)
        {
            Debug.LogError("[InputBindings] actionAsset is not assigned. Drop LoomInputActions.inputactions into the inspector slot.");
            return;
        }

        var map = actionAsset.FindActionMap("Editor", throwIfNotFound: true);

        Select        = map.FindAction("Select",        throwIfNotFound: true);
        Drag          = map.FindAction("Drag",          throwIfNotFound: true);
        Pan           = map.FindAction("Navigate/Pan",  throwIfNotFound: true);
        Zoom          = map.FindAction("Zoom",          throwIfNotFound: true);
        Cancel        = map.FindAction("Cancel",        throwIfNotFound: true);
        Confirm       = map.FindAction("Confirm",       throwIfNotFound: true);
        ToggleMode    = map.FindAction("ToggleMode",    throwIfNotFound: true);
        Undo          = map.FindAction("Undo",          throwIfNotFound: true);
        Redo          = map.FindAction("Redo",          throwIfNotFound: true);
        ExitContext   = map.FindAction("ExitContext",   throwIfNotFound: true);
        MousePosition = map.FindAction("MousePosition", throwIfNotFound: true);

        actionAsset.Enable();
    }

    void OnDestroy()
    {
        if (actionAsset != null) actionAsset.Disable();
        if (Instance == this) Instance = null;
    }
}
