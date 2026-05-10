using System;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Push a node onto the canvas context stack so the user drills inside it.
/// Undo pops back to the previous context and restores the saved camera state.
/// </summary>
[Serializable]
public class EnterContextCommand : IEditorCommand
{
    public ulong         NodeStableId;
    public Vector3       PreviousCameraPosition;
    public float         PreviousCameraZoom;
    public ulong         PreviousContextNodeId;

    public EnterContextCommand() { }
    public EnterContextCommand(ulong nodeStableId, CanvasContext previousContext)
    {
        NodeStableId           = nodeStableId;
        PreviousContextNodeId  = previousContext != null ? previousContext.NodeId.Value : 0;
        PreviousCameraPosition = previousContext != null ? previousContext.CameraPosition : Vector3.zero;
        PreviousCameraZoom     = previousContext != null ? previousContext.CameraZoom : 10f;
    }

    public void Execute(EntityManager em, EditorState state, StableIdRegistry registry)
    {
        if (state == null) return;
        state.ApplyContextPush(new StableId { Value = NodeStableId });
    }

    public void Undo(EntityManager em, EditorState state, StableIdRegistry registry)
    {
        if (state == null) return;
        state.ApplyContextPop();
    }
}
