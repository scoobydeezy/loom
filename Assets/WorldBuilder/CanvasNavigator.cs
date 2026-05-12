using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One frame of the canvas context stack — which node the user is currently inside
/// plus the camera state to restore when popping back to this level.
/// NodeId == default(StableId) represents the root context.
/// </summary>
[Serializable]
public class CanvasContext
{
    public StableId NodeId;
    public Vector3  CameraPosition;
    public float    CameraZoom = 10f;

    public bool IsRoot => NodeId.Value == 0;
}

/// <summary>
/// Manages the user's drill-down stack through nested node interiors.
/// Pushing saves the current camera state and switches to a node's interior.
/// Popping restores the saved camera state for the previous level.
/// Visibility rule: only entities whose NodeParent matches the current context node
/// are visible and interactive — enforced by renderers and WorldQuery, not here.
/// </summary>
public class CanvasNavigator : MonoBehaviour
{
    public static CanvasNavigator Instance { get; private set; }

    public CanvasContext Current => stack.Count > 0 ? stack[stack.Count - 1] : root;

    /// <summary>Read-only view of the full context stack, root-first.</summary>
    public IReadOnlyList<CanvasContext> Stack => stack;

    [SerializeField] Camera targetCamera;

    readonly CanvasContext      root  = new CanvasContext();
    readonly List<CanvasContext> stack = new List<CanvasContext>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (targetCamera == null) targetCamera = Camera.main;
        stack.Add(root);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Push a new context frame for the given node. Saves the current camera state
    /// onto the outgoing frame so Pop can restore it.
    /// </summary>
    public void Push(StableId nodeId)
    {
        SaveCameraInto(stack[stack.Count - 1]);
        stack.Add(new CanvasContext { NodeId = nodeId });
        RestoreCameraFrom(stack[stack.Count - 1]);
    }

    /// <summary>
    /// Pop the current frame, restoring the previous frame's camera state.
    /// No-op if already at the root.
    /// </summary>
    public void Pop()
    {
        if (stack.Count <= 1) return;
        stack.RemoveAt(stack.Count - 1);
        RestoreCameraFrom(stack[stack.Count - 1]);
    }

    /// <summary>Replace the entire stack with the given frame as the current context.</summary>
    public void RestoreTo(CanvasContext context)
    {
        stack.Clear();
        stack.Add(context ?? root);
        RestoreCameraFrom(stack[stack.Count - 1]);
    }

    void SaveCameraInto(CanvasContext ctx)
    {
        if (targetCamera == null) return;
        ctx.CameraPosition = targetCamera.transform.position;
        ctx.CameraZoom     = targetCamera.orthographic ? targetCamera.orthographicSize : targetCamera.fieldOfView;
    }

    void RestoreCameraFrom(CanvasContext ctx)
    {
        if (targetCamera == null) return;
        if (ctx.CameraPosition != Vector3.zero)
            targetCamera.transform.position = ctx.CameraPosition;
        if (targetCamera.orthographic) targetCamera.orthographicSize = ctx.CameraZoom;
        else                            targetCamera.fieldOfView     = ctx.CameraZoom;
    }
}
