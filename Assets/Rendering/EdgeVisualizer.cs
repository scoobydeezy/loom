using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;

/// <summary>
/// Renders every edge as a LineRenderer. Color reflects observed flow stress from PacketVisualizer.
/// Width distinguishes global edges from internal (child) edges.
/// Reads positions from WorldSpaceTransform every frame — never caches, so node dragging works automatically.
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

    EntityManager        entityManager;
    EntityQuery          edgeQuery;
    PacketVisualizer     packetVisualizer;
    NodeFrameVisualizer  nodeFrameVisualizer;

    void Start()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        edgeQuery     = entityManager.CreateEntityQuery(typeof(Edge));
    }

    void Update()
    {
        if (packetVisualizer == null)
            packetVisualizer = FindAnyObjectByType<PacketVisualizer>();
        if (nodeFrameVisualizer == null)
            nodeFrameVisualizer = FindAnyObjectByType<NodeFrameVisualizer>();

        var edges = edgeQuery.ToEntityArray(Unity.Collections.Allocator.Temp);

        foreach (var entity in edges)
        {
            if (!lines.ContainsKey(entity))
                lines[entity] = CreateLine();

            var edge = entityManager.GetComponentData<Edge>(entity);

            if (!entityManager.Exists(edge.FromNode) || !entityManager.Exists(edge.ToNode))
                continue;
            if (!entityManager.HasComponent<WorldSpaceTransform>(edge.FromNode) ||
                !entityManager.HasComponent<WorldSpaceTransform>(edge.ToNode))
                continue;

            bool isInternal = RenderingUtils.IsInternalEdge(entityManager, edge);

            // Internal edges connect children inside a composite — draw between their centers.
            // External edges terminate at frame anchors (FrameEntry/FrameExit entities sit on the wall).
            Vector3 fromPos = isInternal
                ? (Vector3)entityManager.GetComponentData<WorldSpaceTransform>(edge.FromNode).Position
                : RenderingUtils.ResolvePosition(entityManager, nodeFrameVisualizer, edge.FromNode);
            Vector3 toPos = isInternal
                ? (Vector3)entityManager.GetComponentData<WorldSpaceTransform>(edge.ToNode).Position
                : RenderingUtils.ResolvePosition(entityManager, nodeFrameVisualizer, edge.ToNode);

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
