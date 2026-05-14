using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Owns the bottom-bar flyout content and the drag-to-place gesture that turns
/// a palette item into a <see cref="SpawnNodeCommand"/>. Splits "item picked"
/// from "position confirmed" so the same machinery works for mouse drag and
/// (future) controller select-then-place.
/// </summary>
public class PaletteController : MonoBehaviour
{
    public const string GroupSources    = "Sources";
    public const string GroupNodes      = "Nodes";
    public const string GroupPatterns   = "Patterns";
    public const string GroupMechanisms = "Mechanisms";

    public static PaletteController Instance { get; private set; }

    // Hardcoded grouping by NodeTypeDefinition.typeName. Acceptable here because the
    // palette layout is itself a presentation choice — the simulation does not care.
    static readonly HashSet<string> PatternTypeNames = new HashSet<string> { "Queue" };

    // Mechanisms are not yet placeable — they appear as disabled tiles only.
    static readonly MechanismKind[] MechanismOrder =
        { MechanismKind.Route, MechanismKind.Filter, MechanismKind.RateLimit };

    VisualElement root;
    VisualElement flyoutRegion;
    VisualElement canvasRegion;

    string activeGroup;

    // Drag state
    VisualElement       ghost;
    NodeTypeDefinition  dragDefinition;
    int                 dragPointerId = -1;
    bool                isDragging;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        root         = ResolveRoot();
        if (root == null) return;
        flyoutRegion = root.Q<VisualElement>("flyout-region");
        canvasRegion = root.Q<VisualElement>("canvas-region");
        CloseFlyoutInternal();
    }

    void OnDisable()
    {
        ReleaseGhost();
        root = flyoutRegion = canvasRegion = null;
        activeGroup = null;
    }

    VisualElement ResolveRoot()
    {
        if (HUDController.Instance != null && HUDController.Instance.Root != null)
            return HUDController.Instance.Root;
        var doc = GetComponent<UIDocument>();
        return doc != null ? doc.rootVisualElement : null;
    }

    // ---------------------------------------------------------------------
    // Flyout — called by BottomBarController on group-button click.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Toggle the flyout for <paramref name="group"/>. If it's already open,
    /// close it; otherwise rebuild and show. <paramref name="allGroupButtons"/>
    /// is used to clear the "active" class from the other group buttons.
    /// </summary>
    public void ToggleGroup(string group, IEnumerable<Button> allGroupButtons, Button activeButton)
    {
        foreach (var b in allGroupButtons) b?.RemoveFromClassList("active");

        if (activeGroup == group)
        {
            CloseFlyoutInternal();
            activeGroup = null;
            return;
        }

        activeGroup = group;
        activeButton?.AddToClassList("active");
        OpenFlyoutInternal(group);
    }

    void OpenFlyoutInternal(string group)
    {
        if (flyoutRegion == null) return;
        flyoutRegion.Clear();
        foreach (var element in BuildItemsForGroup(group))
            flyoutRegion.Add(element);
        flyoutRegion.AddToClassList("open");
    }

    void CloseFlyoutInternal()
    {
        if (flyoutRegion == null) return;
        flyoutRegion.Clear();
        flyoutRegion.RemoveFromClassList("open");
    }

    // ---------------------------------------------------------------------
    // Item construction
    // ---------------------------------------------------------------------

    IEnumerable<VisualElement> BuildItemsForGroup(string group)
    {
        var query = WorldQuery.Instance;

        if (group == GroupMechanisms)
        {
            foreach (var kind in MechanismOrder)
                yield return MakeMechanismItem(kind);
            yield break;
        }

        if (query == null) yield break;

        foreach (var def in query.GetPlaceableNodeTypes())
        {
            if (def == null) continue;
            bool isPattern = PatternTypeNames.Contains(def.typeName) ||
                             PatternTypeNames.Contains(def.name);
            bool isSource  = def.isPacketSource;

            if (group == GroupSources  && !isSource)  continue;
            if (group == GroupNodes    && (isPattern || isSource)) continue;
            if (group == GroupPatterns && (!isPattern || isSource)) continue;

            yield return MakeNodeItem(def);
        }
    }

    VisualElement MakeNodeItem(NodeTypeDefinition def)
    {
        var element = MakeItemShell(
            label:  string.IsNullOrEmpty(def.typeName) ? def.name : def.typeName,
            color:  def.paletteColor,
            tooltip: string.IsNullOrEmpty(def.typeName) ? def.name : def.typeName);

        element.RegisterCallback<PointerDownEvent>(evt => OnPaletteItemPointerDown(evt, def, element));
        return element;
    }

    VisualElement MakeMechanismItem(MechanismKind kind)
    {
        var element = MakeItemShell(
            label:   kind.ToString(),
            color:   new Color(0.6f, 0.6f, 0.65f),
            tooltip: $"{kind} mechanism (not yet placeable)");
        element.AddToClassList("disabled");
        element.pickingMode = PickingMode.Ignore;
        return element;
    }

    static VisualElement MakeItemShell(string label, Color color, string tooltip)
    {
        var item = new VisualElement();
        item.AddToClassList("palette-item");
        item.tooltip = tooltip;

        var icon = new VisualElement();
        icon.AddToClassList("palette-icon");
        icon.style.backgroundColor = color;
        item.Add(icon);

        var text = new Label(label);
        text.AddToClassList("palette-label");
        item.Add(text);

        return item;
    }

    // ---------------------------------------------------------------------
    // Drag-to-place
    // ---------------------------------------------------------------------

    void OnPaletteItemPointerDown(PointerDownEvent evt, NodeTypeDefinition def, VisualElement source)
    {
        // Only place in Edit mode — Play mode is for observing.
        var state = EditorState.Instance;
        if (state == null || state.Mode != LoomMode.Edit) return;

        if (isDragging) return;
        if (root == null) return;

        isDragging      = true;
        dragDefinition  = def;
        dragPointerId   = evt.pointerId;

        ghost = MakeGhost(def);
        root.Add(ghost);
        PositionGhost(evt.position);

        // Capture on the ghost so move/up keep flowing even after the source unhovers.
        ghost.CapturePointer(dragPointerId);
        ghost.RegisterCallback<PointerMoveEvent>(OnGhostPointerMove);
        ghost.RegisterCallback<PointerUpEvent>(OnGhostPointerUp);
        ghost.RegisterCallback<PointerCaptureOutEvent>(OnGhostPointerCaptureOut);

        evt.StopPropagation();
    }

    void OnGhostPointerMove(PointerMoveEvent evt)
    {
        if (!isDragging || ghost == null) return;
        PositionGhost(evt.position);
    }

    void OnGhostPointerUp(PointerUpEvent evt)
    {
        if (!isDragging) return;

        bool    overCanvas = IsOverCanvas(evt.position);
        Vector2 screenPos  = PanelToScreen(evt.position);
        var     def        = dragDefinition;

        ReleaseGhost();

        if (overCanvas && def != null)
        {
            var query = WorldQuery.Instance;
            if (query != null && EditorCommandBuffer.Instance != null)
            {
                Vector3  world  = query.ScreenToWorld(screenPos);
                ulong    parent = EditorState.Instance != null
                                  ? EditorState.Instance.CurrentContext.NodeId.Value
                                  : 0;
                EditorCommandBuffer.Instance.Execute(
                    new SpawnNodeCommand(def.typeName ?? def.name, new float3(world), parent));
            }
        }
    }

    /// <summary>
    /// Convert a panel-space position (top-left origin, panel units) to a screen-pixel
    /// position with the same top-left convention <see cref="WorldQuery.ScreenToWorld"/>
    /// expects. Computed from the root element's resolved layout vs Screen size so we
    /// match the actual PanelSettings scale regardless of reference resolution.
    /// </summary>
    Vector2 PanelToScreen(Vector2 panelPos)
    {
        if (root == null) return panelPos;
        float panelW = root.resolvedStyle.width;
        float panelH = root.resolvedStyle.height;
        if (float.IsNaN(panelW) || panelW <= 0f) panelW = Screen.width;
        if (float.IsNaN(panelH) || panelH <= 0f) panelH = Screen.height;
        float scaleX = Screen.width  / panelW;
        float scaleY = Screen.height / panelH;
        return new Vector2(panelPos.x * scaleX, panelPos.y * scaleY);
    }

    void OnGhostPointerCaptureOut(PointerCaptureOutEvent evt)
    {
        // If capture is lost (e.g. focus change), abandon the drag without spawning.
        ReleaseGhost();
    }

    void ReleaseGhost()
    {
        if (ghost != null)
        {
            ghost.UnregisterCallback<PointerMoveEvent>(OnGhostPointerMove);
            ghost.UnregisterCallback<PointerUpEvent>(OnGhostPointerUp);
            ghost.UnregisterCallback<PointerCaptureOutEvent>(OnGhostPointerCaptureOut);
            if (dragPointerId >= 0 && ghost.HasPointerCapture(dragPointerId))
                ghost.ReleasePointer(dragPointerId);
            if (ghost.parent != null) ghost.RemoveFromHierarchy();
        }
        ghost          = null;
        dragDefinition = null;
        dragPointerId  = -1;
        isDragging     = false;
    }

    VisualElement MakeGhost(NodeTypeDefinition def)
    {
        // Size the ghost roughly to the node's frame, projected to screen pixels.
        // The exact pixel size is approximate — the spawn position is what matters.
        float worldPerPixel = 1f;
        var cam = Camera.main;
        if (cam != null && cam.orthographic)
            worldPerPixel = cam.orthographicSize * 2f / Mathf.Max(1, Screen.height);

        float w = Mathf.Max(24f, def.frameWidth  / Mathf.Max(0.0001f, worldPerPixel));
        float h = Mathf.Max(24f, def.frameHeight / Mathf.Max(0.0001f, worldPerPixel));

        var g = new VisualElement();
        g.AddToClassList("palette-ghost");
        g.style.width  = w;
        g.style.height = h;
        g.style.backgroundColor = def.paletteColor;
        g.pickingMode = PickingMode.Ignore;
        return g;
    }

    void PositionGhost(Vector2 panelPos)
    {
        if (ghost == null) return;
        float w = ghost.resolvedStyle.width;
        float h = ghost.resolvedStyle.height;
        // Fall back to style values until layout resolves.
        if (float.IsNaN(w) || w <= 0f) w = ghost.style.width.value.value;
        if (float.IsNaN(h) || h <= 0f) h = ghost.style.height.value.value;
        ghost.style.left = panelPos.x - w * 0.5f;
        ghost.style.top  = panelPos.y - h * 0.5f;
    }

    bool IsOverCanvas(Vector2 panelPos)
    {
        if (canvasRegion == null) return false;
        return canvasRegion.worldBound.Contains(panelPos);
    }
}
