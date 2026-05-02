using UnityEngine;
using Unity.Entities;

public class LoomBootstrap : MonoBehaviour
{
    public int packetCount = 1000;

    void Start()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var entityManager = world.EntityManager;

        // Create Node archetype
        var nodeArchetype = entityManager.CreateArchetype(typeof(Node));

        // Create Edge archetype
        var edgeArchetype = entityManager.CreateArchetype(typeof(Edge));

        // Create Packet archetype
        var packetArchetype = entityManager.CreateArchetype(typeof(Packet));

        // Create two nodes
        Entity nodeA = entityManager.CreateEntity(nodeArchetype);
        Entity nodeB = entityManager.CreateEntity(nodeArchetype);

        entityManager.SetComponentData(nodeA, new Node { Id = 1 });
        entityManager.SetComponentData(nodeB, new Node { Id = 2 });

        // Create one edge between them
        Entity edge = entityManager.CreateEntity(edgeArchetype);
        entityManager.SetComponentData(edge, new Edge
        {
            FromNode = nodeA,
            ToNode = nodeB,
            Length = 10f
        });

        // Spawn packets on the edge
        for (int i = 0; i < packetCount; i++)
        {
            Entity packet = entityManager.CreateEntity(packetArchetype);
            entityManager.SetComponentData(packet, new Packet
            {
                CurrentEdge = edge,
                Progress = Random.Range(0f, 10f),
                Speed = 2f
            });
        }

        Debug.Log("Loom world created with packets moving!");
    }
}