using UnityEngine;
using System;

/// <summary>
/// A child entry in a NodeTypeDefinition's assembly recipe.
/// </summary>
[Serializable]
public class NodeTypeChild
{
    public NodeTypeDefinition definition;
    public int count = 1;

    [Tooltip("Freeform label for human readability and future recipe matching. Not enforced by simulation.")]
    public string role;
}

/// <summary>
/// Structural recipe describing what a node of this type looks like.
/// Leaf nodes have an empty children array; their edgeCapacity and internalPathLength
/// determine the edges that connect them to adjacent siblings.
/// Non-leaf nodes define an internal graph through their children array.
/// This definition is used at assembly time only — the simulation never reads it.
/// </summary>
[CreateAssetMenu(fileName = "NodeTypeDefinition", menuName = "Loom/Node Type Definition")]
public class NodeTypeDefinition : ScriptableObject
{
    public string typeName;

    [Tooltip("Child node types assembled into this node's internal graph. Empty = leaf node.")]
    public NodeTypeChild[] children;

    [Tooltip("Capacity of outgoing edges from this node type to adjacent siblings.")]
    public int edgeCapacity = 1;

    [Tooltip("Length of outgoing edges from this node type (proxy for latency at packet speed). Leaf nodes only.")]
    public float internalPathLength = 1f;
}
