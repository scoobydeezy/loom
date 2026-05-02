using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using System.Collections.Generic;

public class PacketVisualizer : MonoBehaviour
{
    public GameObject packetPrefab;

    Dictionary<Entity, GameObject> visuals = new();

    EntityManager entityManager;
    EntityQuery packetQuery;

    void Start()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        packetQuery = entityManager.CreateEntityQuery(typeof(Packet));
    }

    void Update()
    {
        var packets = packetQuery.ToEntityArray(Unity.Collections.Allocator.Temp);

        foreach (var entity in packets)
        {
            if (!visuals.ContainsKey(entity))
                visuals[entity] = Instantiate(packetPrefab);

            var packet = entityManager.GetComponentData<Packet>(entity);
            var edge   = entityManager.GetComponentData<Edge>(packet.CurrentEdge);
            float t    = packet.Progress / edge.Length;

            Vector3 pos;

            if (entityManager.HasComponent<InternalEdge>(packet.CurrentEdge))
            {
                var ie      = entityManager.GetComponentData<InternalEdge>(packet.CurrentEdge);
                var layout  = entityManager.GetComponentData<NodeLayout>(edge.ToNode);
                var nodePos = (Vector3)entityManager.GetComponentData<NodeTransform>(edge.ToNode).Position;

                // Internal stages rise above the node in Y so they read as "inside" the node.
                // Entry starts at nodePos (= external edge endpoint) and exit ends at nodePos
                // (= next external edge start) so there are no positional jumps on arrival
                // or departure. The only visual discontinuity is the small lateral step when a
                // packet is dispatched to a specific lane, which communicates worker assignment.
                float laneX = LaneOffset(ie.LaneIndex, layout.LaneCount);
                pos = ie.Role switch
                {
                    InternalEdgeRole.Entry => Vector3.Lerp(
                        nodePos,
                        nodePos + new Vector3(0f, 0.4f, 0f),
                        t),

                    InternalEdgeRole.Lane => Vector3.Lerp(
                        nodePos + new Vector3(laneX, 0.4f, 0f),
                        nodePos + new Vector3(laneX, 1.4f, 0f),
                        t),

                    InternalEdgeRole.Exit => Vector3.Lerp(
                        nodePos + new Vector3(0f, 1.4f, 0f),
                        nodePos,
                        t),

                    _ => nodePos
                };
            }
            else
            {
                var fromPos = (Vector3)entityManager.GetComponentData<NodeTransform>(edge.FromNode).Position;
                var toPos   = (Vector3)entityManager.GetComponentData<NodeTransform>(edge.ToNode).Position;
                pos = Vector3.Lerp(fromPos, toPos, t);
            }

            visuals[entity].transform.position = pos;
        }

        packets.Dispose();
    }

    // Centers the lane spread around the node's X axis
    static float LaneOffset(int laneIndex, int laneCount)
    {
        float spacing = Mathf.Min(0.4f, 3f / Mathf.Max(laneCount, 1));
        return (laneIndex - (laneCount - 1) * 0.5f) * spacing;
    }
}
