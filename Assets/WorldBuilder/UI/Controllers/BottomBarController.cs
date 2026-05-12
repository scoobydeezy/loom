using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Wires the bottom bar's group buttons (Nodes / Patterns / Mechanisms) to the
/// shared <see cref="PaletteController"/>. This controller owns nothing visual
/// itself — it just routes clicks. Flyout content is the palette's job.
/// </summary>
public class BottomBarController : MonoBehaviour
{
    Button groupNodes;
    Button groupPatterns;
    Button groupMechanisms;

    void OnEnable()
    {
        var root = ResolveRoot();
        if (root == null) return;

        groupNodes      = root.Q<Button>("group-nodes");
        groupPatterns   = root.Q<Button>("group-patterns");
        groupMechanisms = root.Q<Button>("group-mechanisms");

        if (groupNodes      != null) groupNodes.clicked      += OnNodesClicked;
        if (groupPatterns   != null) groupPatterns.clicked   += OnPatternsClicked;
        if (groupMechanisms != null) groupMechanisms.clicked += OnMechanismsClicked;
    }

    void OnDisable()
    {
        if (groupNodes      != null) groupNodes.clicked      -= OnNodesClicked;
        if (groupPatterns   != null) groupPatterns.clicked   -= OnPatternsClicked;
        if (groupMechanisms != null) groupMechanisms.clicked -= OnMechanismsClicked;
        groupNodes = groupPatterns = groupMechanisms = null;
    }

    VisualElement ResolveRoot()
    {
        if (HUDController.Instance != null && HUDController.Instance.Root != null)
            return HUDController.Instance.Root;
        var doc = GetComponent<UIDocument>();
        return doc != null ? doc.rootVisualElement : null;
    }

    void OnNodesClicked()      => Toggle(PaletteController.GroupNodes,      groupNodes);
    void OnPatternsClicked()   => Toggle(PaletteController.GroupPatterns,   groupPatterns);
    void OnMechanismsClicked() => Toggle(PaletteController.GroupMechanisms, groupMechanisms);

    void Toggle(string group, Button button)
    {
        var palette = PaletteController.Instance;
        if (palette == null) return;
        palette.ToggleGroup(group, new[] { groupNodes, groupPatterns, groupMechanisms }, button);
    }
}
