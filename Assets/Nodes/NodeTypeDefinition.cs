using UnityEngine;
using System;

public enum ChildType { Node, Mechanism }

/// <summary>
/// One entry in a NodeTypeDefinition's internal-graph recipe.
/// childType selects whether this entry instantiates a Node subtree or a Mechanism entity.
/// </summary>
[Serializable]
public class NodeTypeChild
{
    public ChildType          childType     = ChildType.Node;
    public NodeTypeDefinition definition;                           // ChildType.Node only
    public MechanismKind      mechanismKind = MechanismKind.Route; // ChildType.Mechanism only
    public int                count         = 1;

    [Tooltip("Freeform label for human readability and future recipe matching. Not enforced by simulation.")]
    public string role;
}

/// <summary>
/// Structural recipe describing what a node of this type looks like.
/// Leaf nodes have an empty children array.
/// Non-leaf nodes define an internal graph through their children array.
/// This definition is used at assembly time only — the simulation never reads it.
/// </summary>
[CreateAssetMenu(fileName = "NodeTypeDefinition", menuName = "Loom/Node Type Definition")]
public class NodeTypeDefinition : ScriptableObject
{
    public string typeName;

    [Tooltip("Child entries (nodes or mechanisms) assembled into this node's internal graph. Empty = leaf node.")]
    public NodeTypeChild[] children;
}
