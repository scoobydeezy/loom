using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;

/// <summary>
/// Renders a diamond (rotated square) at each Mechanism entity's NodeTransform position.
/// Reads ECS state every frame — never caches positions, so node dragging works automatically.
/// </summary>
public class MechanismVisualizer : MonoBehaviour
{
    const float DiamondSize = 0.22f; // half-extent of the diamond
    const float LineWidth   = 0.04f;
    static readonly Color DiamondColor = new Color(0.2f, 1f, 0.6f, 0.9f);

    Dictionary<Entity, LineRenderer> diamonds = new();

    EntityManager entityManager;
    EntityQuery   mechanismQuery;

    void Start()
    {
        entityManager   = World.DefaultGameObjectInjectionWorld.EntityManager;
        mechanismQuery  = entityManager.CreateEntityQuery(typeof(Mechanism), typeof(NodeTransform));
    }

    void Update()
    {
        var mechanisms = mechanismQuery.ToEntityArray(Unity.Collections.Allocator.Temp);

        foreach (var entity in mechanisms)
        {
            if (!diamonds.TryGetValue(entity, out var lr))
            {
                lr = CreateDiamond();
                diamonds[entity] = lr;
            }

            var pos = (Vector3)entityManager.GetComponentData<NodeTransform>(entity).Position;
            SetDiamondPositions(lr, pos);
        }

        // Disable diamonds for mechanisms that no longer exist
        foreach (var kv in diamonds)
        {
            if (!entityManager.Exists(kv.Key))
                kv.Value.enabled = false;
        }

        mechanisms.Dispose();
    }

    static void SetDiamondPositions(LineRenderer lr, Vector3 center)
    {
        float s = DiamondSize;
        lr.SetPosition(0, center + new Vector3( 0,  s, 0)); // top
        lr.SetPosition(1, center + new Vector3( s,  0, 0)); // right
        lr.SetPosition(2, center + new Vector3( 0, -s, 0)); // bottom
        lr.SetPosition(3, center + new Vector3(-s,  0, 0)); // left
        lr.SetPosition(4, center + new Vector3( 0,  s, 0)); // close loop
    }

    LineRenderer CreateDiamond()
    {
        var go = new GameObject("Mechanism");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 5;
        lr.loop          = false;
        lr.useWorldSpace = true;
        lr.startWidth    = LineWidth;
        lr.endWidth      = LineWidth;
        lr.startColor    = DiamondColor;
        lr.endColor      = DiamondColor;
        return lr;
    }
}
