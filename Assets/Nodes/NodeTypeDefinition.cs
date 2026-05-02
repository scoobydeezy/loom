using UnityEngine;

[CreateAssetMenu(fileName = "NodeTypeDefinition", menuName = "Loom/Node Type Definition")]
public class NodeTypeDefinition : ScriptableObject
{
    public string typeName;

    [Tooltip("Worker concurrency — number of parallel processing lanes inside this node")]
    public int laneCount = 1;

    [Tooltip("Max packets that can queue at the entry edge before backpressure propagates upstream")]
    public int queueCapacity = 32;

    [Tooltip("Distance packets travel through each lane (proxy for compute time at packet speed)")]
    public float internalPathLength = 1f;

    [Tooltip("Capacity of the exit staging edge before packets block inside the lanes")]
    public int exitCapacity = 1;
}
