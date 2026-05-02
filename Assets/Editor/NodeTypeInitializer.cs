using UnityEditor;
using UnityEngine;

// Creates the default NodeTypeDefinition assets under Assets/Nodes/NodeTypes/ the first
// time Unity opens this project. Runs automatically; safe to call multiple times (idempotent).
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

        //                         name           lanes  queue  pathLen  exit
        CreateIfMissing(folder, "WebServer",       16,   128,   3.0f,    16);
        CreateIfMissing(folder, "Database",         4,    20,   8.0f,     4);
        CreateIfMissing(folder, "LoadBalancer",    64,   256,   0.2f,    64);
        CreateIfMissing(folder, "Cache",            1,    32,   0.2f,     1);

        AssetDatabase.SaveAssets();
    }

    static void CreateIfMissing(
        string folder, string name,
        int laneCount, int queueCapacity, float internalPathLength, int exitCapacity)
    {
        string path = $"{folder}/{name}.asset";
        if (AssetDatabase.LoadAssetAtPath<NodeTypeDefinition>(path) != null)
            return;

        var def = ScriptableObject.CreateInstance<NodeTypeDefinition>();
        def.typeName           = name;
        def.laneCount          = laneCount;
        def.queueCapacity      = queueCapacity;
        def.internalPathLength = internalPathLength;
        def.exitCapacity       = exitCapacity;

        AssetDatabase.CreateAsset(def, path);
        Debug.Log($"[Loom] Created node type asset: {path}");
    }
}
