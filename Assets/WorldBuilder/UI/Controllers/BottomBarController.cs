using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Wires the bottom bar's group buttons (Sources / Nodes / Patterns / Mechanisms) to the
/// shared <see cref="PaletteController"/>. This controller owns nothing visual
/// itself — it just routes clicks. Flyout content is the palette's job.
/// </summary>
public class BottomBarController : MonoBehaviour
{
    Button groupSources;
    Button groupNodes;
    Button groupPatterns;
    Button groupMechanisms;

    void OnEnable()
    {
        var root = ResolveRoot();
        if (root == null) return;

        groupSources    = root.Q<Button>("group-sources");
        groupNodes      = root.Q<Button>("group-nodes");
        groupPatterns   = root.Q<Button>("group-patterns");
        groupMechanisms = root.Q<Button>("group-mechanisms");

        if (groupSources    != null) groupSources.clicked    += OnSourcesClicked;
        if (groupNodes      != null) groupNodes.clicked      += OnNodesClicked;
        if (groupPatterns   != null) groupPatterns.clicked   += OnPatternsClicked;
        if (groupMechanisms != null) groupMechanisms.clicked += OnMechanismsClicked;
    }

    void OnDisable()
    {
        if (groupSources    != null) groupSources.clicked    -= OnSourcesClicked;
        if (groupNodes      != null) groupNodes.clicked      -= OnNodesClicked;
        if (groupPatterns   != null) groupPatterns.clicked   -= OnPatternsClicked;
        if (groupMechanisms != null) groupMechanisms.clicked -= OnMechanismsClicked;
        groupSources = groupNodes = groupPatterns = groupMechanisms = null;
    }

    VisualElement ResolveRoot()
    {
        if (HUDController.Instance != null && HUDController.Instance.Root != null)
            return HUDController.Instance.Root;
        var doc = GetComponent<UIDocument>();
        return doc != null ? doc.rootVisualElement : null;
    }

    void OnSourcesClicked()    => Toggle(PaletteController.GroupSources,    groupSources);
    void OnNodesClicked()      => Toggle(PaletteController.GroupNodes,      groupNodes);
    void OnPatternsClicked()   => Toggle(PaletteController.GroupPatterns,   groupPatterns);
    void OnMechanismsClicked() => Toggle(PaletteController.GroupMechanisms, groupMechanisms);

    void Toggle(string group, Button button)
    {
        var palette = PaletteController.Instance;
        if (palette == null) return;
        palette.ToggleGroup(group, new[] { groupSources, groupNodes, groupPatterns, groupMechanisms }, button);
    }
}
