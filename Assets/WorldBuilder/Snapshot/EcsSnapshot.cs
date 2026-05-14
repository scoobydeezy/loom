using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Serializable record of a single edge — captured by StableId on both endpoints
/// so it can be restored after the endpoints have been respawned.
/// </summary>
[Serializable]
public class EdgeSnapshot
{
    public ulong FromStableId;
    public ulong ToStableId;
    public ulong EdgeStableId;
    public float Length;
}

/// <summary>
/// Serializable record of a node entity and its descendants — used by undo for
/// destroy, by copy/paste, by save/load, and eventually by scenario resets.
/// Contains only primitives and StableId values. No Entity references.
/// </summary>
[Serializable]
public class NodeSnapshot
{
    public ulong              StableId;
    public ulong              ParentStableId;          // 0 = root
    public float3             LocalPosition;
    public quaternion         LocalRotation;
    public float2             Bounds;
    public string             TypeName;                // "" if no NodeType component
    public bool               IsMechanism;
    public MechanismKind      MechanismKind;
    public bool               HasFrameEntry;
    public bool               HasFrameExit;
    public bool               HasPacketSource;
    public float              PacketSourceEmitRate;
    public PacketColor        PacketSourceColor;
    public PacketShape        PacketSourceShape;
    public List<NodeSnapshot> Children;
    public List<EdgeSnapshot> ConnectedEdges;          // populated on the root only
}

/// <summary>
/// Reusable utility for capturing and restoring node hierarchies.
/// Used by DestroyNodeCommand, copy/paste, save/load, and scenario resets.
/// All addressing is by StableId — Entity values never leak into the snapshot.
/// </summary>
public static class EcsSnapshot
{
    /// <summary>
    /// Captures the full state of a node and all its descendants. The returned
    /// snapshot also contains every edge connected to any captured entity.
    /// </summary>
    public static NodeSnapshot Capture(Entity root, EntityManager em)
    {
        var captured = new HashSet<Entity>();
        var snapshot = CaptureRecursive(root, em, captured);

        // Walk all edges and include those whose endpoints lie inside the captured set.
        snapshot.ConnectedEdges = new List<EdgeSnapshot>();
        using (var edgeQuery = em.CreateEntityQuery(typeof(Edge)))
        using (var edges     = edgeQuery.ToEntityArray(Allocator.Temp))
        {
            for (int i = 0; i < edges.Length; i++)
            {
                var edge = em.GetComponentData<Edge>(edges[i]);
                bool fromIn = captured.Contains(edge.FromNode);
                bool toIn   = captured.Contains(edge.ToNode);
                if (!fromIn && !toIn) continue;
                if (!em.HasComponent<StableId>(edges[i])) continue;

                snapshot.ConnectedEdges.Add(new EdgeSnapshot
                {
                    EdgeStableId = em.GetComponentData<StableId>(edges[i]).Value,
                    FromStableId = em.HasComponent<StableId>(edge.FromNode) ? em.GetComponentData<StableId>(edge.FromNode).Value : 0,
                    ToStableId   = em.HasComponent<StableId>(edge.ToNode)   ? em.GetComponentData<StableId>(edge.ToNode).Value   : 0,
                    Length       = edge.Length,
                });
            }
        }

        return snapshot;
    }

    static NodeSnapshot CaptureRecursive(Entity entity, EntityManager em, HashSet<Entity> captured)
    {
        captured.Add(entity);

        var snap = new NodeSnapshot
        {
            StableId      = em.HasComponent<StableId>(entity) ? em.GetComponentData<StableId>(entity).Value : 0,
            LocalPosition = em.HasComponent<NodeTransform>(entity) ? em.GetComponentData<NodeTransform>(entity).Position : float3.zero,
            LocalRotation = em.HasComponent<NodeTransform>(entity) ? em.GetComponentData<NodeTransform>(entity).Rotation : quaternion.identity,
            Bounds        = em.HasComponent<NodeBounds>(entity) ? em.GetComponentData<NodeBounds>(entity).Size : float2.zero,
            TypeName      = em.HasComponent<NodeType>(entity) ? em.GetComponentData<NodeType>(entity).TypeName.ToString() : "",
            IsMechanism   = em.HasComponent<Mechanism>(entity),
            MechanismKind = em.HasComponent<MechanismType>(entity) ? em.GetComponentData<MechanismType>(entity).Kind : MechanismKind.Route,
            HasFrameEntry = em.HasComponent<FrameEntry>(entity),
            HasFrameExit  = em.HasComponent<FrameExit>(entity),
            HasPacketSource      = em.HasComponent<PacketSource>(entity),
            PacketSourceEmitRate = em.HasComponent<PacketSource>(entity) ? em.GetComponentData<PacketSource>(entity).EmitRate : 1f,
            PacketSourceColor    = em.HasComponent<PacketSource>(entity) ? em.GetComponentData<PacketSource>(entity).Color    : PacketColor.White,
            PacketSourceShape    = em.HasComponent<PacketSource>(entity) ? em.GetComponentData<PacketSource>(entity).Shape    : PacketShape.Sphere,
            ParentStableId = (em.HasComponent<NodeParent>(entity) && em.HasComponent<StableId>(em.GetComponentData<NodeParent>(entity).Parent))
                ? em.GetComponentData<StableId>(em.GetComponentData<NodeParent>(entity).Parent).Value
                : 0,
            Children = new List<NodeSnapshot>(),
        };

        // Walk children — any entity whose NodeParent points at this one.
        using (var q = em.CreateEntityQuery(typeof(NodeParent)))
        using (var all = q.ToEntityArray(Allocator.Temp))
        {
            for (int i = 0; i < all.Length; i++)
            {
                var p = em.GetComponentData<NodeParent>(all[i]).Parent;
                if (p == entity)
                    snap.Children.Add(CaptureRecursive(all[i], em, captured));
            }
        }

        return snap;
    }

    /// <summary>
    /// Restores a previously captured snapshot. Respawns all entities, re-registers
    /// their original StableIds, and reconnects edges by resolving StableIds at the end.
    /// Returns the runtime Entity for the snapshot's root node.
    /// </summary>
    public static Entity Restore(NodeSnapshot snapshot, EntityManager em)
    {
        // First pass: spawn all nodes/mechanisms, restoring StableIds verbatim.
        Entity rootEntity = RestoreNodeRecursive(snapshot, em, parent: Entity.Null);

        // Second pass: rebuild edges now that all endpoints exist in the registry.
        if (snapshot.ConnectedEdges != null)
        {
            foreach (var es in snapshot.ConnectedEdges)
            {
                Entity from = StableIdAllocator.Resolve(em, new StableId { Value = es.FromStableId });
                Entity to   = StableIdAllocator.Resolve(em, new StableId { Value = es.ToStableId });
                if (from == Entity.Null || to == Entity.Null) continue;

                Entity edge = em.CreateEntity(typeof(Edge), typeof(StableId));
                em.SetComponentData(edge, new StableId { Value = es.EdgeStableId });
                StableIdAllocator.Register(em, new StableId { Value = es.EdgeStableId }, edge);
                em.SetComponentData(edge, new Edge { FromNode = from, ToNode = to, Length = es.Length });

                if (em.HasComponent<Mechanism>(from) && em.HasBuffer<MechanismConnections>(from))
                    em.GetBuffer<MechanismConnections>(from).Add(new MechanismConnections { Edge = edge });
            }
        }

        TopologyVersion.Increment(em);
        return rootEntity;
    }

    static Entity RestoreNodeRecursive(NodeSnapshot snap, EntityManager em, Entity parent)
    {
        Entity entity;

        if (snap.IsMechanism)
        {
            entity = em.CreateEntity(
                typeof(Mechanism), typeof(MechanismType), typeof(MechanismConnections),
                typeof(NodeParent),
                typeof(NodeTransform), typeof(WorldSpaceTransform),
                typeof(NodeAnchors),
                typeof(TopologyRoot), typeof(TransformDirty),
                typeof(StableId));
            em.SetComponentData(entity, new MechanismType { Kind = snap.MechanismKind });
        }
        else
        {
            bool isTopLevel = parent == Entity.Null;
            if (isTopLevel)
            {
                entity = em.CreateEntity(
                    typeof(Node), typeof(NodeType),
                    typeof(NodeTransform), typeof(WorldSpaceTransform),
                    typeof(NodeBounds), typeof(NodeAnchors),
                    typeof(TopologyRoot), typeof(TransformDirty),
                    typeof(StableId));
                em.SetComponentData(entity, new NodeType { TypeName = snap.TypeName ?? "" });
            }
            else
            {
                entity = em.CreateEntity(
                    typeof(Node), typeof(NodeParent),
                    typeof(NodeTransform), typeof(WorldSpaceTransform),
                    typeof(NodeBounds), typeof(NodeAnchors),
                    typeof(TopologyRoot), typeof(TransformDirty),
                    typeof(StableId));
            }
            em.SetComponentData(entity, new NodeBounds { Size = snap.Bounds });
        }

        em.SetComponentData(entity, new StableId { Value = snap.StableId });
        StableIdAllocator.Register(em, new StableId { Value = snap.StableId }, entity);

        em.SetComponentData(entity, new NodeTransform { Position = snap.LocalPosition, Rotation = snap.LocalRotation });

        if (parent != Entity.Null)
        {
            em.SetComponentData(entity, new NodeParent { Parent = parent });
            em.SetComponentData(entity, new TopologyRoot { Root = em.GetComponentData<TopologyRoot>(parent).Root });
        }
        else
        {
            em.SetComponentData(entity, new TopologyRoot { Root = entity });
        }

        em.SetComponentData(entity, new NodeAnchors { EntryLocal = float3.zero, ExitLocal = float3.zero });

        if (snap.HasFrameEntry) em.AddComponent<FrameEntry>(entity);
        if (snap.HasFrameExit)  em.AddComponent<FrameExit>(entity);

        if (snap.HasPacketSource)
        {
            em.AddComponent<PacketSource>(entity);
            em.SetComponentData(entity, new PacketSource
            {
                EmitRate    = snap.PacketSourceEmitRate,
                Color       = snap.PacketSourceColor,
                Shape       = snap.PacketSourceShape,
                Accumulator = 0f,
            });
        }

        if (snap.Children != null)
            foreach (var c in snap.Children)
                RestoreNodeRecursive(c, em, entity);

        return entity;
    }

    /// <summary>
    /// Destroys the captured entity and all its descendants, plus any edges touching them.
    /// Unregisters their StableIds from the registry.
    /// </summary>
    public static void DestroyCaptured(NodeSnapshot snapshot, EntityManager em)
    {
        // Destroy edges first so dangling references don't survive.
        if (snapshot.ConnectedEdges != null)
        {
            foreach (var es in snapshot.ConnectedEdges)
            {
                Entity edge = StableIdAllocator.Resolve(em, new StableId { Value = es.EdgeStableId });
                if (edge == Entity.Null) continue;
                StableIdAllocator.Unregister(em, new StableId { Value = es.EdgeStableId });
                em.DestroyEntity(edge);
            }
        }

        DestroyNodeRecursive(snapshot, em);
        TopologyVersion.Increment(em);
    }

    static void DestroyNodeRecursive(NodeSnapshot snap, EntityManager em)
    {
        if (snap.Children != null)
            foreach (var c in snap.Children) DestroyNodeRecursive(c, em);

        Entity entity = StableIdAllocator.Resolve(em, new StableId { Value = snap.StableId });
        if (entity == Entity.Null) return;
        StableIdAllocator.Unregister(em, new StableId { Value = snap.StableId });
        em.DestroyEntity(entity);
    }
}
