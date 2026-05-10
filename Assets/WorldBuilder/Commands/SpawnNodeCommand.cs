using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Spawn a node from a NodeTypeDefinition recipe at the given world position.
/// Resolved by name so the command stays serializable — the ScriptableObject
/// itself never enters the undo stack.
///
/// The first Execute also stores a snapshot of the assembled subgraph keyed by
/// its allocated StableIds. Redo restores from that snapshot rather than re-running
/// the recipe — so StableIds are stable across the entire undo/redo lifetime.
/// </summary>
[Serializable]
public class SpawnNodeCommand : IEditorCommand
{
    public string DefinitionName;
    public float3 WorldPosition;
    public ulong  ParentContextStableId; // 0 = root context

    public ulong        SpawnedRootStableId;
    public NodeSnapshot RestoreSnapshot; // populated after first Execute for Redo

    public SpawnNodeCommand() { }
    public SpawnNodeCommand(string definitionName, float3 worldPosition, ulong parentContextStableId)
    {
        DefinitionName        = definitionName;
        WorldPosition         = worldPosition;
        ParentContextStableId = parentContextStableId;
    }

    public void Execute(EntityManager em, EditorState state, StableIdRegistry registry)
    {
        // Redo path — we already have a snapshot from the previous Execute.
        if (RestoreSnapshot != null)
        {
            EcsSnapshot.Restore(RestoreSnapshot, em);
            return;
        }

        var def = ResolveDefinition(DefinitionName);
        if (def == null)
        {
            Debug.LogWarning($"[SpawnNodeCommand] NodeTypeDefinition '{DefinitionName}' not found.");
            return;
        }

        Entity parent = StableIdAllocator.Resolve(em, new StableId { Value = ParentContextStableId });
        var    result = NodeAssembler.Assemble(em, def, WorldPosition, parent);
        SpawnedRootStableId = result.RootStableId.Value;

        // Capture immediately so Redo restores instead of re-running the recipe.
        RestoreSnapshot = EcsSnapshot.Capture(result.Node, em);
    }

    public void Undo(EntityManager em, EditorState state, StableIdRegistry registry)
    {
        if (SpawnedRootStableId == 0) return;
        Entity root = StableIdAllocator.Resolve(em, new StableId { Value = SpawnedRootStableId });
        if (root == Entity.Null) return;

        // Refresh snapshot from current state so any edits since spawn are preserved on Redo.
        RestoreSnapshot = EcsSnapshot.Capture(root, em);
        EcsSnapshot.DestroyCaptured(RestoreSnapshot, em);
    }

    static NodeTypeDefinition ResolveDefinition(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var all = Resources.LoadAll<NodeTypeDefinition>("");
        for (int i = 0; i < all.Length; i++)
            if (all[i].typeName == name || all[i].name == name) return all[i];

#if UNITY_EDITOR
        var guids = UnityEditor.AssetDatabase.FindAssets("t:NodeTypeDefinition");
        foreach (var g in guids)
        {
            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
            var d    = UnityEditor.AssetDatabase.LoadAssetAtPath<NodeTypeDefinition>(path);
            if (d != null && (d.typeName == name || d.name == name)) return d;
        }
#endif
        return null;
    }
}
