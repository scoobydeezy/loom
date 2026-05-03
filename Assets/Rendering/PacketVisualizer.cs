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

        foreach (var entity in packets)
        {
            if (!visuals.ContainsKey(entity))
                visuals[entity] = Instantiate(packetPrefab);

            var packet  = entityManager.GetComponentData<Packet>(entity);
            var edge    = entityManager.GetComponentData<Edge>(packet.CurrentEdge);
            float t     = packet.Progress / edge.Length;

            var fromPos = (Vector3)entityManager.GetComponentData<NodeTransform>(edge.FromNode).Position;
            var toPos   = (Vector3)entityManager.GetComponentData<NodeTransform>(edge.ToNode).Position;

            visuals[entity].transform.position = Vector3.Lerp(fromPos, toPos, t);
        }

        packets.Dispose();
    }
}
