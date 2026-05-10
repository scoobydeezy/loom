using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Bridge between the simulation and the editor's command buffer. Simulation
/// systems must not call EditorCommandBuffer directly — they raise events here,
/// and this bus deferred-executes them in LateUpdate with recordForUndo: false.
///
/// This keeps simulation systems unaware of the editor and prevents simulation-
/// sourced mutations (failures, drops) from poisoning the undo stack.
/// </summary>
public class SimulationEventBus : MonoBehaviour
{
    public static SimulationEventBus Instance { get; private set; }

    readonly Queue<IEditorCommand> pending = new Queue<IEditorCommand>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    EntityManager EM
    {
        get
        {
            var w = World.DefaultGameObjectInjectionWorld;
            return w != null ? w.EntityManager : default;
        }
    }

    /// <summary>Raised when a simulation-side event determines a node should be destroyed (e.g. failure injection).</summary>
    public void RaiseNodeFailure(Entity node)
    {
        var em = EM;
        if (node == Entity.Null || !em.Exists(node) || !em.HasComponent<StableId>(node)) return;
        var id = em.GetComponentData<StableId>(node).Value;
        pending.Enqueue(new DestroyNodeCommand(id));
    }

    /// <summary>Raised by the simulation when a packet is dropped (Phase 4 tail-drop).</summary>
    public void RaisePacketDrop(Entity packet, float3 position)
    {
        var em = EM;
        if (packet == Entity.Null || !em.Exists(packet) || !em.HasComponent<StableId>(packet)) return;
        var id = em.GetComponentData<StableId>(packet).Value;
        pending.Enqueue(new DropPacketCommand(id, position));
    }

    void LateUpdate()
    {
        var buffer = EditorCommandBuffer.Instance;
        if (buffer == null)
        {
            pending.Clear();
            return;
        }

        while (pending.Count > 0)
            buffer.Execute(pending.Dequeue(), recordForUndo: false);
    }
}
