using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Drives the top bar: mode toggle button and context breadcrumb.
/// All updates are event-driven — no per-frame polling of EditorState.
/// </summary>
public class TopBarController : MonoBehaviour
{
    Button modeToggle;
    Label  breadcrumbText;

    void OnEnable()
    {
        var root = ResolveRoot();
        if (root == null) return;

        modeToggle     = root.Q<Button>("mode-toggle");
        breadcrumbText = root.Q<Label>("breadcrumb-text");

        if (modeToggle != null) modeToggle.clicked += OnModeTogglePressed;

        var state = EditorState.Instance;
        if (state != null)
        {
            state.OnModeChanged    += OnModeChanged;
            state.OnContextChanged += OnContextChanged;
            OnModeChanged(state.Mode);
            OnContextChanged(CanvasNavigator.Instance != null
                ? CanvasNavigator.Instance.Stack
                : new List<CanvasContext>());
        }
    }

    void OnDisable()
    {
        if (modeToggle != null) modeToggle.clicked -= OnModeTogglePressed;

        var state = EditorState.Instance;
        if (state != null)
        {
            state.OnModeChanged    -= OnModeChanged;
            state.OnContextChanged -= OnContextChanged;
        }

        modeToggle     = null;
        breadcrumbText = null;
    }

    VisualElement ResolveRoot()
    {
        if (HUDController.Instance != null && HUDController.Instance.Root != null)
            return HUDController.Instance.Root;
        var doc = GetComponent<UIDocument>();
        return doc != null ? doc.rootVisualElement : null;
    }

    void OnModeTogglePressed()
    {
        var state  = EditorState.Instance;
        var buffer = EditorCommandBuffer.Instance;
        if (state == null || buffer == null) return;

        var next = state.Mode == LoomMode.Edit ? LoomMode.Play : LoomMode.Edit;
        // Play → Edit is a non-undoable boundary (matches InputHandler).
        buffer.Execute(new SetModeCommand(state.Mode, next),
                       recordForUndo: state.Mode == LoomMode.Edit);
    }

    void OnModeChanged(LoomMode mode)
    {
        if (modeToggle == null) return;
        // Button label shows the mode the user can switch TO.
        modeToggle.text = mode == LoomMode.Edit ? "Play" : "Edit";
    }

    void OnContextChanged(IReadOnlyList<CanvasContext> stack)
    {
        if (breadcrumbText == null) return;
        if (stack == null || stack.Count == 0)
        {
            breadcrumbText.text = "Root";
            return;
        }

        var query = WorldQuery.Instance;
        var parts = stack.Select(c =>
            c == null || c.NodeId.Value == 0
                ? "Root"
                : (query != null ? query.GetDisplayName(c.NodeId) : $"#{c.NodeId.Value}"));
        breadcrumbText.text = string.Join(" / ", parts);
    }
}
