using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;

/// <summary>
/// Draws an axis-aligned bounding box around each composite node's children every frame.
/// Reads NodeParent and NodeTransform from ECS — never caches positions.
/// </summary>
public class NodeBoundsVisualizer : MonoBehaviour
{
    const float Padding    = 0.4f;
    const float LineWidth  = 0.04f;
    static readonly Color BoundsColor = new Color(0.9f, 0.7f, 0.1f, 0.8f);

    // One LineRenderer quad (5 points, loop) per parent node
    Dictionary<Entity, LineRenderer> boxes = new();

    EntityManager entityManager;
    EntityQuery   childQuery;

    void Start()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        // Query all entities that are children of some parent node
        childQuery = entityManager.CreateEntityQuery(typeof(NodeParent), typeof(NodeTransform));
    }

    void Update()
    {
        // Collect children grouped by parent
        var children = childQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        var parentBounds = new Dictionary<Entity, (Vector3 min, Vector3 max)>();

        foreach (var child in children)
        {
            var parent = entityManager.GetComponentData<NodeParent>(child).Parent;
            var pos    = (Vector3)entityManager.GetComponentData<NodeTransform>(child).Position;

            if (parentBounds.TryGetValue(parent, out var b))
            {
                parentBounds[parent] = (Vector3.Min(b.min, pos), Vector3.Max(b.max, pos));
            }
            else
            {
                parentBounds[parent] = (pos, pos);
            }
        }
        children.Dispose();

        // Draw or update a box for each composite parent
        foreach (var kv in parentBounds)
        {
            Entity parent = kv.Key;
            var    min    = kv.Value.min - Vector3.one * Padding;
            var    max    = kv.Value.max + Vector3.one * Padding;

            if (!boxes.TryGetValue(parent, out var lr))
            {
                lr = CreateBoxLine();
                boxes[parent] = lr;
            }

            // Rectangle corners (closed loop: 5 points, last == first)
            lr.SetPosition(0, new Vector3(min.x, min.y, min.z));
            lr.SetPosition(1, new Vector3(max.x, min.y, min.z));
            lr.SetPosition(2, new Vector3(max.x, max.y, min.z));
            lr.SetPosition(3, new Vector3(min.x, max.y, min.z));
            lr.SetPosition(4, new Vector3(min.x, min.y, min.z));
        }

        // Hide boxes for parents that no longer have children
        foreach (var kv in boxes)
        {
            if (!parentBounds.ContainsKey(kv.Key))
                kv.Value.enabled = false;
            else
                kv.Value.enabled = true;
        }
    }

    LineRenderer CreateBoxLine()
    {
        var go = new GameObject("NodeBounds");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 5;
        lr.loop          = false;
        lr.useWorldSpace = true;
        lr.startWidth    = LineWidth;
        lr.endWidth      = LineWidth;
        lr.startColor    = BoundsColor;
        lr.endColor      = BoundsColor;
        return lr;
    }
}
