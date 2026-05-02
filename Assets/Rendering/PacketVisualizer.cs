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
            {
                visuals[entity] = Instantiate(packetPrefab);
            }

            var packet = entityManager.GetComponentData<Packet>(entity);
            var edge = entityManager.GetComponentData<Edge>(packet.CurrentEdge);

            var fromPos = entityManager.GetComponentData<NodeTransform>(edge.FromNode).Position;
            var toPos = entityManager.GetComponentData<NodeTransform>(edge.ToNode).Position;

            float t = packet.Progress / edge.Length;
            Vector3 pos = Vector3.Lerp(fromPos, toPos, t);

            visuals[entity].transform.position = pos;
        }

        packets.Dispose();
    }
}