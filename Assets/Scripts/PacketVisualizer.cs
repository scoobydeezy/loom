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

    Vector3 start = new Vector3(-5, 0, 0);
    Vector3 end = new Vector3(5, 0, 0);

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

            float t = packet.Progress / 10f; // edge length
            Vector3 pos = Vector3.Lerp(start, end, t);

            visuals[entity].transform.position = pos;
        }

        packets.Dispose();
    }
}