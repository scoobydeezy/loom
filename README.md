# Loom — Distributed Systems Sandbox Simulator

## What It Is

Loom is a real-time spatial simulator that makes distributed systems tangible. Abstract concepts — latency, throughput, queuing, routing, load balancing — become observable motion in physical space.

**Core premise:** Distributed systems are packets moving through constrained geometry. All system behavior must emerge from movement and structure alone. No timers, no hidden delays, no abstract processing logic.

---

## Physical Metaphor

| Concept      | Representation                                        |
| ------------ | ----------------------------------------------------- |
| Node         | A compute location (server, service, DB)              |
| Edge         | A constrained path between nodes (latency = distance) |
| Packet       | A unit of work (moving bead)                          |
| Throughput   | Path capacity                                         |
| Compute time | Distance traversed inside a node                      |
| Routing      | Path selection across edges                           |

---

## Architecture

The model is recursive. A node can contain a graph of child nodes and edges. Those children can contain their own graphs. The same mechanic — packets moving through edges — applies at every level of nesting.

```
Topology
  └── Node (composite — contains child nodes + edges)
        └── Node (composite)
              └── Node (leaf — just position and edge properties)
```

There is no special Part type. A processing lane, a node interior, and a cluster are all the same thing: nodes connected by edges. Nesting depth is the only distinction, expressed via the `NodeParent` component.

### Global Graph

`Node ↔ Edge ↔ Node ↔ Edge ↔ Node`

Represents system topology. Edges only connect Nodes. Handles inter-service routing.

### Internal Node Space

`Entry → [child nodes + edges] → Exit`

Each node is itself a graph. Compute time = distance. Queues emerge from congestion, not logic. Nesting is unbounded.

---

## Node Types

Node types are **recipes** — `NodeTypeDefinition` ScriptableObjects that describe how a node should be assembled from child nodes and edges. They never enter the ECS world. Type is not declared at runtime; it is recognized by matching assembled structure against a recipe (Phase 2).

**Leaf types** (no children): `Intake`, `ProcessingLane`, `Egress`

**Composite types** (assembled from leaves): `WebServer`, `Database`, `LoadBalancer`, `Cache`

---

## Stack

- **Engine:** Unity (URP)
- **Architecture:** ECS / DOTS (C#)
- **Rendering:** Hybrid ECS-driven (`PacketVisualizer`, `EdgeVisualizer`)
- **Scale target:** 10k–100k+ packets

---

## Design Rules

- Edges only connect Nodes, at every level of nesting
- All behavior emerges from movement through edges
- No timers, no wait states, no artificial delays
- Node type is recognized from structure, never declared
- ECS: Components = data, Systems = behavior, everything else is emergence

---

## Current State

**Milestone 3 in progress.** Proven so far:

- ECS packet movement and edge traversal
- Graph routing across nodes
- Working triangle topology (A → B → C → A)
- Capacity-based congestion
- Visual packet motion
- Recursive node spawning from `NodeTypeDefinition` recipes
- Internal node graphs (composite nodes with child nodes and edges)
- All edges rendered as lines via `EdgeVisualizer`

---

## Phase 1 Goal — Sandbox Mode

An interactive environment to place nodes, connect edges, and observe live packet flow — congestion, routing, and throughput visible in real time.

### Milestones

| #   | Status | Description                                                    |
| --- | ------ | -------------------------------------------------------------- |
| 1   | ✅     | Packet movement on edges                                       |
| 2   | ✅     | Graph routing across full loops                                |
| 3   | 🔄     | Internal node physics (recursive spawning, composite nodes)    |
| 4   | 🎯     | Visualized geometry (splines, spatial layout, internal paths)  |
| 5   | 🎯     | World builder (drag/drop nodes, live edge connections)         |
| 6   | 🎯     | Sandbox polish (pause/play/step, metrics, adjustable capacity) |

---

## Philosophy

> Simulation first, visualization second. The simulation is never explained — it is observed. All complexity must emerge from structure, not logic.
