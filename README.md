Here's a rewritten version that's tighter, removes redundancy, and works as a self-contained context primer for a new chat:

---

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

## Two-Layer Architecture

### 1. Global Graph

`Node ↔ Edge ↔ Node ↔ Edge ↔ Node`

Represents system topology. Edges only connect Nodes. Handles inter-service routing.

### 2. Internal Node Space

`Entry → internal lanes/queues → Exit`

Each node is itself a graph. Compute time = distance. Queues emerge from congestion, not logic.

---

## System Layers

1. **Graph** — Global topology (nodes + edges)
2. **Packet** — Movement and traversal
3. **Node** — Internal structure (lanes, queues, constraints)
4. **Rendering** — Visualization of simulation state

---

## Stack

- **Engine:** Unity (URP)
- **Architecture:** ECS / DOTS (C#)
- **Rendering:** Hybrid ECS-driven
- **Scale target:** 10k–100k+ packets

---

## Design Rules

- Edges only connect Nodes
- All behavior emerges from movement through edges
- No timers, no wait states, no artificial delays
- ECS: Components = data, Systems = movement, behavior emerges from structure

---

## Current State

**Milestone 2 complete.** Proven so far:

- ECS packet movement and edge traversal
- Graph routing across nodes
- Working triangle topology (A → B → C → A)
- Capacity-based congestion
- Visual packet motion

---

## Phase 1 Goal — Sandbox Mode

An interactive environment to place nodes, connect edges, and observe live packet flow — congestion, routing, and throughput visible in real time.

### Milestones

| #   | Status | Description                                                    |
| --- | ------ | -------------------------------------------------------------- |
| 1   | ✅     | Packet movement on edges                                       |
| 2   | ✅     | Graph routing across full loops                                |
| 3   | 🎯     | Internal node physics (lanes, queues, entry/exit)              |
| 4   | 🎯     | Visualized geometry (splines, spatial layout, internal paths)  |
| 5   | 🎯     | World builder (drag/drop nodes, live edge connections)         |
| 6   | 🎯     | Sandbox polish (pause/play/step, metrics, adjustable capacity) |

---

## Philosophy

> Simulation first, visualization second. The simulation is never explained — it is observed. All complexity must emerge from structure, not logic.
