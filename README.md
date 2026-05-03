# Loom — Distributed Systems Sandbox Simulator

## What It Is

Loom is a real-time spatial simulator that makes distributed systems tangible. Abstract concepts — latency, throughput, queuing, routing, load balancing — become observable motion in physical space.

**Core premise:** Distributed systems are packets moving through constrained geometry. All system behavior must emerge from movement and structure alone. No timers, no hidden delays, no abstract processing logic.

---

## Physical Metaphor

| Concept      | Representation                                          |
| ------------ | ------------------------------------------------------- |
| Node         | A compute location (server, service, DB)                |
| Edge         | A constrained path between nodes (latency = distance)   |
| Mechanism    | A physical actor — intercepts packets, applies a rule   |
| Packet       | A unit of work (moving bead on a string)                |
| Throughput   | Path capacity                                           |
| Compute time | Distance traversed inside a node                        |
| Routing      | A mechanism selecting the least-congested outbound edge |

---

## Architecture

Everything is built from three primitives: **Node**, **Edge**, **Mechanism**. No other structural concepts exist.

The model is recursive. A node contains a graph of child nodes, edges, and mechanisms. Those children can contain their own graphs. The same traversal logic applies at every level.

```
Topology
  └── Node → Edge → Mechanism → Edge → Node
                          └── Node (contains its own graph)
```

Mechanisms are the only things that make decisions. Nodes are places. Edges are paths. Packets are dumb — they carry a destination and move. All intelligence lives in mechanisms.

### Traversal Rules

- **Plain node, 1 outbound edge:** auto-forwarded by `PacketTraverseSystem`
- **Plain node, 0 outbound edges:** packet waits — correct backpressure
- **Plain node, >1 outbound edges:** misconfiguration — packets pile up visibly
- **Mechanism:** stamps `AwaitingRouting`, `MechanismSystem` selects outbound edge from `MechanismConnections`

---

## Node Types

Node types are **recipes** — `NodeTypeDefinition` ScriptableObjects describing how a node is assembled from child nodes and mechanisms. They never enter the ECS world. Type is not declared at runtime; it is recognized by matching assembled structure against a recipe (Phase 2).

**Entry point** = first child in the recipe. **Exit points** = all entities in the last group.
`Intake` and `Egress` node types have been removed — entry and exit are structural positions, not named types.

| Asset          | Recipe                                   | Notes                          |
| -------------- | ---------------------------------------- | ------------------------------ |
| ProcessingLane | Leaf                                     | edgeCapacity:1, pathLength:3.0 |
| WebServer      | Route x1 → ProcessingLane x16 → Route x1 | Fan-out, process, collect      |
| Database       | Route x1 → ProcessingLane x4 → Route x1  | Fewer lanes, saturates faster  |
| LoadBalancer   | Route x1 → ProcessingLane x8             | Distribute across 8 backends   |
| Cache          | ProcessingLane x1                        | Single lane, no routing needed |

---

## Stack

- **Engine:** Unity (URP)
- **Architecture:** ECS / DOTS (C#)
- **Rendering:** Hybrid ECS-driven (`PacketVisualizer`, `EdgeVisualizer`)
- **Scale target:** 10k–100k+ packets

---

## Design Rules

- Three primitives only: Node, Edge, Mechanism
- Packets are dumb — they carry a destination, mechanisms decide the path
- All behavior emerges from movement through edges
- No timers, no wait states, no artificial delays
- Node type is recognized from structure, never declared
- Mechanisms own all routing decisions — never nodes, never packets
- A plain node with multiple outbound edges is a misconfiguration

---

## Current State

**Milestone 3 complete.** Proven so far:

- ECS packet movement and edge traversal
- Dynamic routing via Mechanism entities
- Working triangle topology with any combination of node types
- Recursive node spawning from `NodeTypeDefinition` recipes
- Internal node graphs with mechanisms, processing lanes, and fan-out
- Capacity-based congestion and backpressure (`WaitingAtNode`)
- All edges rendered as lines, internal edges tinted distinctly
- LoadBalancer distributing across 8 lanes concurrently

---

## Phase 1 Goal — Sandbox Mode

An interactive environment to place nodes, connect edges, and observe live packet flow — congestion, routing, and throughput visible in real time.

### Milestones

| #   | Status | Description                                                       |
| --- | ------ | ----------------------------------------------------------------- |
| 1   | ✅     | Packet movement on edges                                          |
| 2   | ✅     | Graph routing across full loops                                   |
| 3   | ✅     | Internal node physics (mechanisms, dynamic routing, backpressure) |
| 4   | 🎯     | Visualized geometry (splines, spatial layout, internal paths)     |
| 5   | 🎯     | World builder (drag/drop nodes, live edge connections)            |
| 6   | 🎯     | Sandbox polish (pause/play/step, metrics, adjustable capacity)    |

---

## Philosophy

> Simulation first, visualization second. The simulation is never explained — it is observed. All complexity must emerge from structure, not logic.
