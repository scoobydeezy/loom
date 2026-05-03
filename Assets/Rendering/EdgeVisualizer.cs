using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;

public class EdgeVisualizer : MonoBehaviour
{
    static readonly Color GlobalColor   = Color.white;
    static readonly Color InternalColor = new Color(0.3f, 0.6f, 1f);
    const float GlobalWidth   = 0.05f;
    const float InternalWidth = 0.03f;

    Dictionary<Entity, LineRenderer> lines = new();

    EntityManager entityManager;
    EntityQuery   edgeQuery;

    void Start()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        edgeQuery     = entityManager.CreateEntityQuery(typeof(Edge));
    }

    void Update()
    {
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
            // Edges touching a global mechanism (no NodeParent) are global.
            bool fromIsChild = entityManager.HasComponent<NodeParent>(edge.FromNode);
            bool toIsChild   = entityManager.HasComponent<NodeParent>(edge.ToNode);
            bool isInternal  = false;
            if (fromIsChild && toIsChild)
            {
                var fromParent = entityManager.GetComponentData<NodeParent>(edge.FromNode).Parent;
                var toParent   = entityManager.GetComponentData<NodeParent>(edge.ToNode).Parent;
                isInternal = fromParent == toParent;
            }

            Color col   = isInternal ? InternalColor : GlobalColor;
            float width = isInternal ? InternalWidth  : GlobalWidth;

            var lr = lines[entity];
            lr.SetPosition(0, fromPos);
            lr.SetPosition(1, toPos);
            lr.startColor = col;
            lr.endColor   = col;
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
        lr.startColor    = GlobalColor;
        lr.endColor      = GlobalColor;
        return lr;
    }
}
