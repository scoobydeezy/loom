using UnityEditor;
using UnityEngine;

// Creates the default NodeTypeDefinition assets under Assets/Nodes/NodeTypes/ on project load.
// Runs automatically; safe to call multiple times (idempotent). Leaf types are created first
// so composite types can reference them as serialized assets.
[InitializeOnLoad]
static class NodeTypeInitializer
{
    static NodeTypeInitializer()
    {
        EditorApplication.delayCall += EnsureDefaultTypesExist;
    }

    static void EnsureDefaultTypesExist()
    {
        const string folder = "Assets/Nodes/NodeTypes";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/Nodes", "NodeTypes");

        // Leaf types first — composite types reference them
        EnsureLeaf(folder, "Intake",         edgeCapacity: 50, internalPathLength: 0.2f);
        EnsureLeaf(folder, "ProcessingLane", edgeCapacity: 1,  internalPathLength: 3f);
        EnsureLeaf(folder, "Egress",         edgeCapacity: 50, internalPathLength: 0.2f);

        // Save so references below serialize correctly
        AssetDatabase.SaveAssets();

        // Load from disk to get stable asset-backed references
        var intake   = Load(folder, "Intake");
        var procLane = Load(folder, "ProcessingLane");
        var egress   = Load(folder, "Egress");

        // Composite types — recreate if missing or if children were never set
        // (handles migration from old flat-property format)
        EnsureComposite(folder, "WebServer", intake, procLane, egress, laneCount: 16);
        EnsureComposite(folder, "Database",  intake, procLane, egress, laneCount: 4);

        AssetDatabase.SaveAssets();
    }

    static void EnsureLeaf(string folder, string name, int edgeCapacity, float internalPathLength)
    {
        string path = $"{folder}/{name}.asset";
        if (AssetDatabase.LoadAssetAtPath<NodeTypeDefinition>(path) != null) return;

        var def = ScriptableObject.CreateInstance<NodeTypeDefinition>();
        def.typeName           = name;
        def.edgeCapacity       = edgeCapacity;
        def.internalPathLength = internalPathLength;
        def.children           = new NodeTypeChild[0];
        AssetDatabase.CreateAsset(def, path);
        Debug.Log($"[Loom] Created leaf type: {path}");
    }

    static void EnsureComposite(string folder, string name,
        NodeTypeDefinition intake, NodeTypeDefinition procLane, NodeTypeDefinition egress,
        int laneCount)
    {
        string path = $"{folder}/{name}.asset";
        var def = AssetDatabase.LoadAssetAtPath<NodeTypeDefinition>(path);

        bool missing     = def == null;
        bool stale       = def != null && (def.children == null || def.children.Length == 0);

        if (missing)
        {
            def = ScriptableObject.CreateInstance<NodeTypeDefinition>();
            AssetDatabase.CreateAsset(def, path);
            Debug.Log($"[Loom] Created composite type: {path}");
        }

        if (missing || stale)
        {
            def.typeName = name;
            def.children = new NodeTypeChild[]
            {
                new NodeTypeChild { definition = intake,   count = 1,         role = "intake"     },
                new NodeTypeChild { definition = procLane, count = laneCount, role = "processing" },
                new NodeTypeChild { definition = egress,   count = 1,         role = "egress"     },
            };
            EditorUtility.SetDirty(def);
            Debug.Log($"[Loom] Updated composite type: {path}");
        }
    }

    static NodeTypeDefinition Load(string folder, string name) =>
        AssetDatabase.LoadAssetAtPath<NodeTypeDefinition>($"{folder}/{name}.asset");
}
