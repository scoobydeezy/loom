using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;

public class PacketVisualizer : MonoBehaviour
{
    public GameObject packetPrefab;

    Dictionary<Entity, GameObject> visuals = new();

    EntityManager entityManager;
    EntityQuery   packetQuery;

    void Start()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        packetQuery   = entityManager.CreateEntityQuery(typeof(Packet));
    }

    void Update()
    {
        var packets = packetQuery.ToEntityArray(Unity.Collections.Allocator.Temp);

        // Detect destroyed packets and clean up their visuals
        var toRemove = new List<Entity>();
        foreach (var known in visuals.Keys)
            if (!entityManager.Exists(known))
                toRemove.Add(known);
        foreach (var gone in toRemove)
        {
            Destroy(visuals[gone]);
            visuals.Remove(gone);
        }

        foreach (var entity in packets)
        {
            if (!visuals.ContainsKey(entity))
                visuals[entity] = Instantiate(packetPrefab);

            var packet  = entityManager.GetComponentData<Packet>(entity);
            var edge    = entityManager.GetComponentData<Edge>(packet.CurrentEdge);
            var fromPos = (Vector3)entityManager.GetComponentData<NodeTransform>(edge.FromNode).Position;
            var toPos   = (Vector3)entityManager.GetComponentData<NodeTransform>(edge.ToNode).Position;

            visuals[entity].transform.position = Vector3.Lerp(fromPos, toPos, packet.Progress / edge.Length);
        }

        packets.Dispose();
    }
}