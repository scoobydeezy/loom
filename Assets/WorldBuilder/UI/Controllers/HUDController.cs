using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Owns the UIDocument that renders the world-builder HUD and exposes shared
/// element handles to sibling controllers (TopBar / BottomBar / Palette).
/// Sibling controllers read <see cref="Root"/> in their OnEnable rather than
/// each calling <c>GetComponent&lt;UIDocument&gt;()</c> — keeps the lookup centralized.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class HUDController : MonoBehaviour
{
    public static HUDController Instance { get; private set; }

    /// <summary>Root of the rendered HUD. Null until the UIDocument has been built.</summary>
    public VisualElement Root { get; private set; }

    /// <summary>Transparent overlay that occupies the simulation viewport between the two bars.</summary>
    public VisualElement CanvasRegion { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnEnable()
    {
        var doc = GetComponent<UIDocument>();
        Root         = doc != null ? doc.rootVisualElement : null;
        CanvasRegion = Root?.Q<VisualElement>("canvas-region");
    }

    void OnDisable()
    {
        Root         = null;
        CanvasRegion = null;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
