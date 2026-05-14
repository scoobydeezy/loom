using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Translates input from <see cref="InputBindings"/> InputActions into editor
/// commands or <see cref="EditorState"/> calls. No legacy <c>UnityEngine.Input</c>
/// — every key, button, and axis comes through an InputAction so the
/// <c>LoomInputActions.inputactions</c> asset stays the single source of binding truth.
///
/// During a drag the NodeTransform is written every frame for visual feedback,
/// then a single MoveNodeCommand is emitted on mouse-up so the undo stack
/// contains one step per drag rather than one per frame.
/// </summary>
public class InputHandler : MonoBehaviour
{
    [SerializeField] Camera targetCamera;

    [Tooltip("Click threshold (seconds) below which a press is treated as a click rather than a drag.")]
    [SerializeField] float clickThreshold = 0.18f;

    [Tooltip("Double-click window in seconds.")]
    [SerializeField] float doubleClickWindow = 0.35f;

    [Tooltip("Scroll-wheel zoom factor per notch.")]
    [SerializeField] float zoomFactor = 1.1f;

    [Tooltip("Screen-space pixel distance the mouse must move before a press becomes a drag.")]
    [SerializeField] float dragThresholdPx = 3f;

    [Tooltip("Material applied to the ghost edge during connect mode. Leave unassigned to skip the ghost visual; connection logic still works.")]
    [SerializeField] Material ghostEdgeMaterial;

    [Tooltip("Width of the ghost edge line drawn during connect mode.")]
    [SerializeField] float ghostEdgeWidth = 0.04f;

    // Drag state
    Entity   dragEntity   = Entity.Null;
    StableId dragStableId;
    float3   dragStartLocal;
    float3   dragStartWorld;
    Vector2  dragMouseStartScreen;
    bool     isDragging;
    float    pressStartTime;
    float    lastClickTime;
    int      connectBeganFrame = -1;

    // Connect-mode ghost edge — created on BeginConnect, destroyed on EndConnect.
    GameObject   ghostEdgeGO;
    LineRenderer ghostEdgeLR;

    bool subscribed;

    void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
    }

    void OnEnable()  { TrySubscribe(); }
    void Start()     { TrySubscribe(); }   // fallback if InputBindings.Awake hadn't run yet at OnEnable
    void OnDisable() { Unsubscribe(); }

    void TrySubscribe()
    {
        if (subscribed) return;
        var b = InputBindings.Instance;
        if (b == null) return;

        b.Select.performed       += OnSelectPressed;
        b.Select.canceled        += OnSelectReleased;
        b.Confirm.performed      += OnConfirm;
        b.Cancel.performed       += OnCancel;
        b.ToggleMode.performed   += OnToggleMode;
        b.Undo.performed         += OnUndo;
        b.Redo.performed         += OnRedo;
        b.ExitContext.performed  += OnExitContext;
        b.Zoom.performed         += OnZoom;
        b.Pan.performed          += OnPan;
        if (b.Delete != null) b.Delete.performed += OnDelete;

        var state = EditorState.Instance;
        if (state != null)
        {
            state.OnConnectBegan += OnConnectBegan;
            state.OnConnectEnded += OnConnectEnded;
        }

        subscribed = true;
    }

    void Unsubscribe()
    {
        if (!subscribed) return;
        var b = InputBindings.Instance;
        if (b != null)
        {
            b.Select.performed      -= OnSelectPressed;
            b.Select.canceled       -= OnSelectReleased;
            b.Confirm.performed     -= OnConfirm;
            b.Cancel.performed      -= OnCancel;
            b.ToggleMode.performed  -= OnToggleMode;
            b.Undo.performed        -= OnUndo;
            b.Redo.performed        -= OnRedo;
            b.ExitContext.performed -= OnExitContext;
            b.Zoom.performed        -= OnZoom;
            b.Pan.performed         -= OnPan;
            if (b.Delete != null) b.Delete.performed -= OnDelete;
        }

        var state = EditorState.Instance;
        if (state != null)
        {
            state.OnConnectBegan -= OnConnectBegan;
            state.OnConnectEnded -= OnConnectEnded;
        }

        DestroyGhostEdge();
        subscribed = false;
    }

    // ---------------------------------------------------------------------
    // Per-frame polling — drag threshold detection and live drag write.
    // ---------------------------------------------------------------------

    void Update()
    {
        var b     = InputBindings.Instance;
        var state = EditorState.Instance;

        // Ghost edge follows the cursor while a connect is in progress.
        if (state != null && state.IsConnecting && ghostEdgeLR != null)
            UpdateGhostEdge();

        if (b == null || dragEntity == Entity.Null) return;

        // Promote a press into a drag once the mouse moves past the threshold.
        if (!isDragging && b.Select.IsPressed())
        {
            Vector2 nowScreen = MouseScreenPosition();
            if ((nowScreen - dragMouseStartScreen).sqrMagnitude > dragThresholdPx * dragThresholdPx)
                isDragging = true;
        }

        if (isDragging)
            ApplyLiveDrag(MouseScreenToWorld(MouseScreenPosition()));
    }

    // ---------------------------------------------------------------------
    // Discrete callbacks
    // ---------------------------------------------------------------------

    void OnSelectPressed(InputAction.CallbackContext ctx)
    {
        var state = EditorState.Instance;
        if (state == null || state.Mode != LoomMode.Edit) return;

        // Alt+LMB is reserved for panning — let OnPan handle it.
        if (IsAltHeld()) return;

        pressStartTime       = Time.unscaledTime;
        dragMouseStartScreen = MouseScreenPosition();

        Vector3 worldPoint = MouseScreenToWorld(dragMouseStartScreen);

        // Shift+click on a FrameExit anchor circle begins an edge connect.
        if (IsShiftHeld() && !state.IsConnecting)
        {
            Entity exitAnchor = PickAnchorEntity(worldPoint, exitAnchor: true);
            if (exitAnchor != Entity.Null)
            {
                var em = EM;
                if (em.HasComponent<StableId>(exitAnchor))
                {
                    state.BeginConnect(em.GetComponentData<StableId>(exitAnchor));
                    connectBeganFrame = Time.frameCount;
                    return;
                }
            }
        }

        StableId hit = PickTopmost(worldPoint);

        // Once connecting, OnConfirm completes the edge — skip selection here.
        if (state.IsConnecting) return;

        // Selection: Ctrl held → toggle into set, else replace.
        if (hit.Value != 0)
        {
            bool additive = IsCtrlHeld();
            var  ids      = additive ? new HashSet<StableId>(state.SelectedEntities) : new HashSet<StableId>();
            if (additive && ids.Contains(hit)) ids.Remove(hit);
            else                                ids.Add(hit);
            state.ApplySelection(ids);

            BeginDragStaging(hit);
        }
        else
        {
            state.ApplySelection(new HashSet<StableId>());
        }
    }

    void OnConfirm(InputAction.CallbackContext ctx)
    {
        // The click that began a connect can also trigger Confirm in the same frame — skip it.
        if (connectBeganFrame == Time.frameCount) return;

        var state = EditorState.Instance;
        if (state == null || !state.IsConnecting) return;

        Vector3 worldPoint  = MouseScreenToWorld(MouseScreenPosition());
        Entity  entryAnchor = PickAnchorEntity(worldPoint, exitAnchor: false);
        if (entryAnchor == Entity.Null) return;

        var query = WorldQuery.Instance;
        if (query == null || !query.IsValidConnectionTarget(state.ConnectSource, entryAnchor)) return;

        var em = EM;
        if (!em.HasComponent<StableId>(entryAnchor)) return;
        var targetId = em.GetComponentData<StableId>(entryAnchor);

        EditorCommandBuffer.Instance.Execute(
            new ConnectEdgeCommand(state.ConnectSource.Value, targetId.Value));
        state.EndConnect();
    }

    void OnDelete(InputAction.CallbackContext ctx)
    {
        var state = EditorState.Instance;
        if (state == null || state.Mode != LoomMode.Edit) return;
        if (state.SelectedEntities.Count == 0) return;

        var buffer = EditorCommandBuffer.Instance;
        if (buffer == null) return;

        // One transaction → one undo step, regardless of how many entities are selected.
        var ids = new List<StableId>(state.SelectedEntities);
        buffer.BeginTransaction();
        foreach (var id in ids)
            buffer.Execute(new DestroyNodeCommand(id.Value));
        buffer.EndTransaction();

        state.ApplySelection(new HashSet<StableId>());
    }

    // ---------------------------------------------------------------------
    // Connect-mode ghost edge — created when the user begins a connect, follows
    // the cursor each frame in Update, destroyed on EndConnect.
    // ---------------------------------------------------------------------

    void OnConnectBegan()
    {
        DestroyGhostEdge();
        if (ghostEdgeMaterial == null) return;

        ghostEdgeGO = new GameObject("ConnectGhostEdge");
        ghostEdgeGO.transform.SetParent(transform, worldPositionStays: false);
        ghostEdgeLR = ghostEdgeGO.AddComponent<LineRenderer>();
        ghostEdgeLR.positionCount = 2;
        ghostEdgeLR.useWorldSpace = true;
        ghostEdgeLR.startWidth    = ghostEdgeWidth;
        ghostEdgeLR.endWidth      = ghostEdgeWidth;
        ghostEdgeLR.material      = ghostEdgeMaterial;
        RenderingUtils.SetLineColor(ghostEdgeLR, DebugColors.GhostEdge);
    }

    void OnConnectEnded()
    {
        DestroyGhostEdge();
    }

    void DestroyGhostEdge()
    {
        if (ghostEdgeGO != null)
        {
            Destroy(ghostEdgeGO);
            ghostEdgeGO = null;
            ghostEdgeLR = null;
        }
    }

    void UpdateGhostEdge()
    {
        var state = EditorState.Instance;
        if (state == null) return;
        var em = EM;
        Entity src = StableIdAllocator.Resolve(em, state.ConnectSource);
        if (src == Entity.Null || !em.HasComponent<WorldSpaceTransform>(src))
        {
            ghostEdgeLR.enabled = false;
            return;
        }

        Vector3 from = (Vector3)em.GetComponentData<WorldSpaceTransform>(src).Position;
        Vector3 to   = MouseScreenToWorld(MouseScreenPosition());
        ghostEdgeLR.enabled = true;
        ghostEdgeLR.SetPosition(0, from);
        ghostEdgeLR.SetPosition(1, to);
    }

    /// <summary>
    /// Pick the closest FrameEntry or FrameExit anchor whose hit zone contains the click.
    /// Hit zone is the circle drawn by AnchorVisualizer — same radius constant drives both,
    /// so visual and logical hit area are guaranteed to match.
    ///
    /// Top-level composites carry self-tags too but their children supply the visible anchors,
    /// so the composite itself is skipped — mirroring NodeFrameVisualizer's discriminator.
    /// </summary>
    Entity PickAnchorEntity(Vector3 worldPoint, bool exitAnchor)
    {
        var em    = EM;
        var state = EditorState.Instance;
        if (state == null) return Entity.Null;

        using var parentQuery = em.CreateEntityQuery(typeof(NodeParent));
        using var children    = parentQuery.ToEntityArray(Allocator.Temp);
        var hasChildren = new HashSet<Entity>();
        for (int i = 0; i < children.Length; i++)
            hasChildren.Add(em.GetComponentData<NodeParent>(children[i]).Parent);

        using var q = exitAnchor
            ? em.CreateEntityQuery(typeof(FrameExit),  typeof(WorldSpaceTransform))
            : em.CreateEntityQuery(typeof(FrameEntry), typeof(WorldSpaceTransform));
        using var anchors = q.ToEntityArray(Allocator.Temp);

        Entity best   = Entity.Null;
        float  bestSq = AnchorVisualizer.AnchorHitRadius * AnchorVisualizer.AnchorHitRadius;

        for (int i = 0; i < anchors.Length; i++)
        {
            Entity e         = anchors[i];
            bool   hasParent = em.HasComponent<NodeParent>(e);

            if (!hasParent && hasChildren.Contains(e)) continue; // top-level composite — children carry the anchors
            if (!AnchorVisualizer.IsAnchorInContext(em, e, state.CurrentContext.NodeId)) continue;

            Vector3 pos = AnchorVisualizer.GetAnchorWorldPosition(em, e, exitAnchor);
            float   dx  = worldPoint.x - pos.x;
            float   dy  = worldPoint.y - pos.y;
            float   sq  = dx * dx + dy * dy;
            if (sq <= bestSq)
            {
                bestSq = sq;
                best   = e;
            }
        }
        return best;
    }

    void OnSelectReleased(InputAction.CallbackContext ctx)
    {
        if (dragEntity == Entity.Null) return;

        float held = Time.unscaledTime - pressStartTime;
        if (isDragging)                       CommitDrag();
        else if (held < clickThreshold)       HandleClickRelease();

        dragEntity   = Entity.Null;
        dragStableId = default;
        isDragging   = false;
    }

    void OnCancel(InputAction.CallbackContext ctx)
    {
        var state = EditorState.Instance;
        if (state != null && state.IsConnecting) state.EndConnect();
        if (isDragging) CancelDrag();
    }

    void OnToggleMode(InputAction.CallbackContext ctx)
    {
        var state  = EditorState.Instance;
        var buffer = EditorCommandBuffer.Instance;
        if (state == null || buffer == null) return;
        var next = state.Mode == LoomMode.Edit ? LoomMode.Play : LoomMode.Edit;
        buffer.Execute(new SetModeCommand(state.Mode, next),
                       recordForUndo: state.Mode == LoomMode.Edit);
    }

    void OnUndo(InputAction.CallbackContext ctx)
    {
        if (EditorCommandBuffer.Instance != null) EditorCommandBuffer.Instance.Undo();
    }

    void OnRedo(InputAction.CallbackContext ctx)
    {
        if (EditorCommandBuffer.Instance != null) EditorCommandBuffer.Instance.Redo();
    }

    void OnExitContext(InputAction.CallbackContext ctx)
    {
        var state = EditorState.Instance;
        if (state != null && !state.CurrentContext.IsRoot) state.ApplyContextPop();
    }

    void OnZoom(InputAction.CallbackContext ctx)
    {
        if (targetCamera == null || !targetCamera.orthographic) return;
        float scroll = ctx.ReadValue<float>();
        if (Mathf.Abs(scroll) < 0.001f) return;
        targetCamera.orthographicSize = Mathf.Max(0.1f,
            targetCamera.orthographicSize * (scroll > 0 ? 1f / zoomFactor : zoomFactor));
    }

    void OnPan(InputAction.CallbackContext ctx)
    {
        if (targetCamera == null) return;
        Vector2 deltaPx = ctx.ReadValue<Vector2>();
        if (deltaPx.sqrMagnitude < 0.0001f) return;

        // Convert screen-space pixels to world units so panning tracks the mouse 1:1.
        float worldPerPixel = targetCamera.orthographic
            ? targetCamera.orthographicSize * 2f / Mathf.Max(1, Screen.height)
            : 0.01f;
        Vector3 worldDelta = new Vector3(deltaPx.x, deltaPx.y, 0f) * worldPerPixel;
        targetCamera.transform.position -= worldDelta;
    }

    // ---------------------------------------------------------------------
    // Click / drag mechanics
    // ---------------------------------------------------------------------

    void HandleClickRelease()
    {
        // Double-click → enter context.
        if (Time.unscaledTime - lastClickTime < doubleClickWindow)
        {
            var state = EditorState.Instance;
            if (state != null && state.SelectedEntities.Count == 1)
            {
                foreach (var id in state.SelectedEntities)
                {
                    EditorCommandBuffer.Instance.Execute(
                        new EnterContextCommand(id.Value, state.CurrentContext),
                        recordForUndo: false);
                    break;
                }
            }
            lastClickTime = 0f;
        }
        else
        {
            lastClickTime = Time.unscaledTime;
        }
    }

    void BeginDragStaging(StableId id)
    {
        var em = EM;
        var e  = StableIdAllocator.Resolve(em, id);
        if (e == Entity.Null || !em.HasComponent<NodeTransform>(e)) return;
        if (em.HasComponent<Mechanism>(e)) return;

        dragEntity     = e;
        dragStableId   = id;
        dragStartLocal = em.GetComponentData<NodeTransform>(e).Position;
        dragStartWorld = em.HasComponent<WorldSpaceTransform>(e)
                         ? em.GetComponentData<WorldSpaceTransform>(e).Position
                         : float3.zero;
    }

    void ApplyLiveDrag(Vector3 mouseNowWorld)
    {
        var em = EM;
        if (!em.HasComponent<NodeTransform>(dragEntity)) return;

        Vector3 mouseStartWorld = MouseScreenToWorld(dragMouseStartScreen);
        Vector3 delta           = mouseNowWorld - mouseStartWorld;
        float3  newWorld        = dragStartWorld + (float3)delta;

        float3 newLocal = newWorld;
        if (em.HasComponent<NodeParent>(dragEntity))
        {
            Entity parent = em.GetComponentData<NodeParent>(dragEntity).Parent;
            if (parent != Entity.Null && em.HasComponent<WorldSpaceTransform>(parent))
            {
                var pw = em.GetComponentData<WorldSpaceTransform>(parent);
                newLocal = math.rotate(math.inverse(pw.Rotation), newWorld - pw.Position);
            }
        }

        var t = em.GetComponentData<NodeTransform>(dragEntity);
        t.Position = newLocal;
        em.SetComponentData(dragEntity, t);
        if (!em.HasComponent<TransformDirty>(dragEntity)) em.AddComponent<TransformDirty>(dragEntity);
    }

    void CommitDrag()
    {
        var em = EM;
        if (!em.HasComponent<NodeTransform>(dragEntity)) return;
        float3 finalLocal = em.GetComponentData<NodeTransform>(dragEntity).Position;
        if (math.distancesq(finalLocal, dragStartLocal) < 1e-8f) return;
        EditorCommandBuffer.Instance.Execute(
            new MoveNodeCommand(dragStableId.Value, dragStartLocal, finalLocal));
    }

    void CancelDrag()
    {
        var em = EM;
        if (dragEntity != Entity.Null && em.HasComponent<NodeTransform>(dragEntity))
        {
            var t = em.GetComponentData<NodeTransform>(dragEntity);
            t.Position = dragStartLocal;
            em.SetComponentData(dragEntity, t);
            if (!em.HasComponent<TransformDirty>(dragEntity)) em.AddComponent<TransformDirty>(dragEntity);
        }
        dragEntity   = Entity.Null;
        dragStableId = default;
        isDragging   = false;
    }

    // ---------------------------------------------------------------------
    // Hit testing
    // ---------------------------------------------------------------------

    StableId PickTopmost(Vector3 worldPoint)
    {
        var state = EditorState.Instance;
        var query = WorldQuery.Instance;
        if (state == null || query == null) return default;

        StableId best       = default;
        float    bestSizeSq = float.MaxValue;

        foreach (var id in query.GetEntitiesInContext(state.CurrentContext.NodeId))
        {
            if (!query.TryGetWorldPosition(id, out var pos)) continue;
            if (!query.TryGetBounds(id, out var size)) continue;

            float dx = Mathf.Abs(worldPoint.x - pos.x);
            float dy = Mathf.Abs(worldPoint.y - pos.y);
            if (dx <= size.x * 0.5f && dy <= size.y * 0.5f)
            {
                float sq = size.x * size.y;
                if (sq < bestSizeSq)
                {
                    bestSizeSq = sq;
                    best       = id;
                }
            }
        }
        return best;
    }

    // ---------------------------------------------------------------------
    // Helpers — Input System reads, world unprojection, ECS handle.
    // ---------------------------------------------------------------------

    EntityManager EM
    {
        get
        {
            var w = World.DefaultGameObjectInjectionWorld;
            return w != null ? w.EntityManager : default;
        }
    }

    Vector2 MouseScreenPosition()
    {
        var b = InputBindings.Instance;
        return b != null && b.MousePosition != null
            ? b.MousePosition.ReadValue<Vector2>()
            : Vector2.zero;
    }

    Vector3 MouseScreenToWorld(Vector2 screen)
    {
        if (targetCamera == null) return Vector3.zero;
        Vector3 m = new Vector3(screen.x, screen.y, 0f);
        if (targetCamera.orthographic)
        {
            m.z = -targetCamera.transform.position.z;
            return targetCamera.ScreenToWorldPoint(m);
        }
        Ray   ray = targetCamera.ScreenPointToRay(m);
        float t   = -ray.origin.z / ray.direction.z;
        return ray.origin + ray.direction * t;
    }

    static bool IsShiftHeld() => Keyboard.current != null &&
        (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

    static bool IsCtrlHeld() => Keyboard.current != null &&
        (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);

    static bool IsAltHeld() => Keyboard.current != null &&
        (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed);
}
