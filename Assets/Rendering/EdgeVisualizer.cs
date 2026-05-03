using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;

public class EdgeVisualizer : MonoBehaviour
{
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

            var edge    = entityManager.GetComponentData<Edge>(entity);
            var fromPos = (Vector3)entityManager.GetComponentData<NodeTransform>(edge.FromNode).Position;
            var toPos   = (Vector3)entityManager.GetComponentData<NodeTransform>(edge.ToNode).Position;

            var lr = lines[entity];
            lr.SetPosition(0, fromPos);
            lr.SetPosition(1, toPos);
        }

        edges.Dispose();
    }

    LineRenderer CreateLine()
    {
        var go = new GameObject("Edge");
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.startWidth    = 0.05f;
        lr.endWidth      = 0.05f;
        lr.startColor    = Color.white;
        lr.endColor      = Color.white;
        return lr;
    }
}
