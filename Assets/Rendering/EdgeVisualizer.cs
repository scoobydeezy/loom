using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;

/// <summary>
/// Renders every edge as a LineRenderer. Color reflects observed flow stress from PacketVisualizer.
/// Width distinguishes global edges from internal (child) edges.
/// Reads positions from NodeTransform every frame — never caches, so node dragging works automatically.
/// </summary>
[DefaultExecutionOrder(200)]
public class EdgeVisualizer : MonoBehaviour
{
    const float GlobalWidth   = 0.05f;
    const float InternalWidth = 0.03f;

    [Header("Edge Materials")]
    public Material matFree;
    public Material matFlowing;
    public Material matStressed;
    public Material matJammed;

    readonly Dictionary<Entity, LineRenderer> lines = new();

    EntityManager    entityManager;
    EntityQuery      edgeQuery;
    PacketVisualizer packetVisualizer;

    void Start()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        edgeQuery     = entityManager.CreateEntityQuery(typeof(Edge));
    }

    void Update()
    {
        if (packetVisualizer == null)
            packetVisualizer = FindAnyObjectByType<PacketVisualizer>();

        var edges = edgeQuery.ToEntityArray(Unity.Collections.Allocator.Temp);

        foreach (var entity in edges)
        {
            if (!lines.ContainsKey(entity))
                lines[entity] = CreateLine();

            var edge = entityManager.GetComponentData<Edge>(entity);

            if (!entityManager.Exists(edge.FromNode) || !entityManager.Exists(edge.ToNode))
                continue;
            if (!entityManager.HasComponent<NodeTransform>(edge.FromNode) ||
                !entityManager.HasComponent<NodeTransform>(edge.ToNode))
                continue;

            var fromPos = (Vector3)entityManager.GetComponentData<NodeTransform>(edge.FromNode).Position;
            var toPos   = (Vector3)entityManager.GetComponentData<NodeTransform>(edge.ToNode).Position;

            // An edge is internal when both endpoints are children of the same parent node.
            bool fromIsChild = entityManager.HasComponent<NodeParent>(edge.FromNode);
            bool toIsChild   = entityManager.HasComponent<NodeParent>(edge.ToNode);
            bool isInternal  = false;
            if (fromIsChild && toIsChild)
            {
                var fromParent = entityManager.GetComponentData<NodeParent>(edge.FromNode).Parent;
                var toParent   = entityManager.GetComponentData<NodeParent>(edge.ToNode).Parent;
                isInternal = fromParent == toParent;
            }

            EdgeStressLevel stress = EdgeStressLevel.Free;
            if (packetVisualizer != null)
                packetVisualizer.EdgeStressMap.TryGetValue(entity, out stress);

            float width = isInternal ? InternalWidth : GlobalWidth;

            var lr = lines[entity];
            lr.SetPosition(0, fromPos);
            lr.SetPosition(1, toPos);
            lr.material = stress switch
            {
                EdgeStressLevel.Flowing  => matFlowing,
                EdgeStressLevel.Stressed => matStressed,
                EdgeStressLevel.Jammed   => matJammed,
                _                        => matFree,
            };
            lr.startWidth = width;
            lr.endWidth   = width;
        }

        edges.Dispose();
    }

    LineRenderer CreateLine()
    {
        var go = new GameObject("Edge");
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.startWidth    = GlobalWidth;
        lr.endWidth      = GlobalWidth;
        lr.material      = matFree;
        return lr;
    }
}
