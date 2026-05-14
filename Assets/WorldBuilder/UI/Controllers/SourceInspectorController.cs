using System;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Floating inspector panel for a selected PacketSource node. Visible only when
/// the selection is exactly one entity that carries a PacketSource component.
/// Each commit dispatches a ConfigureSourceCommand — one undo step per change.
///
/// Positioning is recomputed every frame so the panel tracks node drags and
/// camera moves. Built imperatively rather than via UXML — the panel exists at
/// most once and is too small to be worth a separate template file.
/// </summary>
public class SourceInspectorController : MonoBehaviour
{
    const float PanelOffsetX = 32f;
    const float PanelOffsetY = -8f;

    VisualElement root;
    VisualElement panel;
    Slider        rateSlider;
    DropdownField colorDropdown;
    DropdownField shapeDropdown;
    Label         titleLabel;

    StableId trackedId;
    bool     tracking;

    void OnEnable()
    {
        if (HUDController.Instance == null || HUDController.Instance.Root == null)
        {
            // Defer to first Update so HUDController has time to bind.
            return;
        }
        EnsureBuilt();

        var state = EditorState.Instance;
        if (state != null)
        {
            state.OnSelectionChanged += OnSelectionChanged;
            state.OnModeChanged      += OnModeChanged;
            RefreshFromState();
        }
    }

    void OnDisable()
    {
        var state = EditorState.Instance;
        if (state != null)
        {
            state.OnSelectionChanged -= OnSelectionChanged;
            state.OnModeChanged      -= OnModeChanged;
        }
        Hide();
    }

    void Update()
    {
        if (root == null) EnsureBuilt();
        if (!tracking || panel == null) return;
        ReprojectPanelPosition();
    }

    void EnsureBuilt()
    {
        if (panel != null) return;
        if (HUDController.Instance == null) return;
        root = HUDController.Instance.Root;
        if (root == null) return;

        panel = new VisualElement { name = "source-inspector" };
        panel.style.position           = Position.Absolute;
        panel.style.minWidth           = 220;
        panel.style.paddingTop         = 10;
        panel.style.paddingBottom      = 10;
        panel.style.paddingLeft        = 12;
        panel.style.paddingRight       = 12;
        panel.style.backgroundColor    = new Color(0.07f, 0.07f, 0.07f, 0.92f);
        panel.style.borderTopLeftRadius     = 8;
        panel.style.borderTopRightRadius    = 8;
        panel.style.borderBottomLeftRadius  = 8;
        panel.style.borderBottomRightRadius = 8;
        panel.style.borderLeftWidth = panel.style.borderRightWidth =
        panel.style.borderTopWidth  = panel.style.borderBottomWidth = 1;
        panel.style.borderLeftColor = panel.style.borderRightColor =
        panel.style.borderTopColor  = panel.style.borderBottomColor = new Color(1f, 1f, 1f, 0.12f);
        panel.style.display = DisplayStyle.None;

        titleLabel = new Label("Source");
        titleLabel.style.color = new Color(1f, 1f, 1f, 0.9f);
        titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        titleLabel.style.marginBottom            = 8;
        panel.Add(titleLabel);

        rateSlider = new Slider("Emit Rate", 0.1f, 10f) { value = 1f };
        rateSlider.RegisterValueChangedCallback(OnRateChanged);
        panel.Add(rateSlider);

        colorDropdown = new DropdownField("Color", EnumChoices<PacketColor>(), 0);
        colorDropdown.RegisterValueChangedCallback(OnColorChanged);
        panel.Add(colorDropdown);

        shapeDropdown = new DropdownField("Shape", EnumChoices<PacketShape>(), 0);
        shapeDropdown.RegisterValueChangedCallback(OnShapeChanged);
        panel.Add(shapeDropdown);

        root.Add(panel);
    }

    static System.Collections.Generic.List<string> EnumChoices<T>() where T : Enum
    {
        var list = new System.Collections.Generic.List<string>();
        foreach (var v in Enum.GetValues(typeof(T))) list.Add(v.ToString());
        return list;
    }

    // ---------------------------------------------------------------------
    // Selection tracking — show only when exactly one PacketSource is selected.
    // ---------------------------------------------------------------------

    void OnSelectionChanged(System.Collections.Generic.IReadOnlyCollection<StableId> _) => RefreshFromState();
    void OnModeChanged(LoomMode _)                                                       => RefreshFromState();

    void RefreshFromState()
    {
        if (panel == null) return;
        var state = EditorState.Instance;
        if (state == null || state.Mode != LoomMode.Edit || state.SelectedEntities.Count != 1)
        {
            Hide();
            return;
        }

        StableId only = default;
        foreach (var id in state.SelectedEntities) { only = id; break; }

        var em = World.DefaultGameObjectInjectionWorld != null
                 ? World.DefaultGameObjectInjectionWorld.EntityManager
                 : default;
        Entity entity = StableIdAllocator.Resolve(em, only);
        if (entity == Entity.Null || !em.HasComponent<PacketSource>(entity))
        {
            Hide();
            return;
        }

        trackedId = only;
        tracking  = true;
        Populate(em.GetComponentData<PacketSource>(entity));
        panel.style.display = DisplayStyle.Flex;
        ReprojectPanelPosition();
    }

    void Hide()
    {
        tracking = false;
        trackedId = default;
        if (panel != null) panel.style.display = DisplayStyle.None;
    }

    void Populate(PacketSource ps)
    {
        suppressEvents = true;
        rateSlider.SetValueWithoutNotify(ps.EmitRate);
        colorDropdown.SetValueWithoutNotify(ps.Color.ToString());
        shapeDropdown.SetValueWithoutNotify(ps.Shape.ToString());
        suppressEvents = false;
    }

    bool suppressEvents;

    // ---------------------------------------------------------------------
    // Change handlers — each commit is one undoable command.
    // ---------------------------------------------------------------------

    void OnRateChanged(ChangeEvent<float> evt)
    {
        if (suppressEvents) return;
        SubmitChange(newRate: evt.newValue, prevRate: evt.previousValue, rateChanged: true);
    }

    void OnColorChanged(ChangeEvent<string> evt)
    {
        if (suppressEvents) return;
        if (!Enum.TryParse(evt.newValue, out PacketColor next))     return;
        if (!Enum.TryParse(evt.previousValue, out PacketColor prev)) prev = next;
        SubmitChange(newColor: next, prevColor: prev, colorChanged: true);
    }

    void OnShapeChanged(ChangeEvent<string> evt)
    {
        if (suppressEvents) return;
        if (!Enum.TryParse(evt.newValue, out PacketShape next))      return;
        if (!Enum.TryParse(evt.previousValue, out PacketShape prev)) prev = next;
        SubmitChange(newShape: next, prevShape: prev, shapeChanged: true);
    }

    void SubmitChange(
        float newRate = 0f, float prevRate = 0f, bool rateChanged = false,
        PacketColor newColor = default, PacketColor prevColor = default, bool colorChanged = false,
        PacketShape newShape = default, PacketShape prevShape = default, bool shapeChanged = false)
    {
        if (!tracking) return;
        var em = World.DefaultGameObjectInjectionWorld != null
                 ? World.DefaultGameObjectInjectionWorld.EntityManager
                 : default;
        Entity entity = StableIdAllocator.Resolve(em, trackedId);
        if (entity == Entity.Null || !em.HasComponent<PacketSource>(entity)) return;

        var ps = em.GetComponentData<PacketSource>(entity);
        var cmd = new ConfigureSourceCommand(
            trackedId.Value,
            previousEmitRate: rateChanged  ? prevRate  : ps.EmitRate,
            newEmitRate:      rateChanged  ? newRate   : ps.EmitRate,
            previousColor:    colorChanged ? prevColor : ps.Color,
            newColor:         colorChanged ? newColor  : ps.Color,
            previousShape:    shapeChanged ? prevShape : ps.Shape,
            newShape:         shapeChanged ? newShape  : ps.Shape);

        EditorCommandBuffer.Instance?.Execute(cmd);
    }

    // ---------------------------------------------------------------------
    // Positioning — project the tracked node's world position to panel-local
    // coordinates each frame so the panel stays anchored to the node.
    // ---------------------------------------------------------------------

    void ReprojectPanelPosition()
    {
        var query = WorldQuery.Instance;
        if (query == null || !query.TryGetWorldPosition(trackedId, out var worldPos)) return;

        var cam = Camera.main;
        if (cam == null) return;

        Vector3 screen = cam.WorldToScreenPoint(worldPos);
        // Panel uses top-left origin; Unity screen uses bottom-left.
        float panelW = root.resolvedStyle.width;
        float panelH = root.resolvedStyle.height;
        if (float.IsNaN(panelW) || panelW <= 0f) panelW = Screen.width;
        if (float.IsNaN(panelH) || panelH <= 0f) panelH = Screen.height;
        float scaleX = panelW / Screen.width;
        float scaleY = panelH / Screen.height;

        float left = screen.x * scaleX + PanelOffsetX;
        float top  = (Screen.height - screen.y) * scaleY + PanelOffsetY;

        panel.style.left = left;
        panel.style.top  = top;
    }
}
