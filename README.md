# Loom — Distributed Systems Sandbox Simulator

## Vision

Loom is a visual, interactive sandbox for understanding cloud and distributed system architecture through **spatial simulation**.

Abstract concepts like:

- latency
- throughput
- queues
- load balancing
- compute time
- routing
- topology

are made tangible through a physical metaphor:

| Concept      | Physical Representation               |
| ------------ | ------------------------------------- |
| Connection   | String / line / spline (edge)         |
| Packet       | Bead moving along the string          |
| Latency      | Physical distance of the string       |
| Compute time | Physical travel _inside_ a node       |
| Routing      | Which string the bead moves onto next |
| Proximity    | Spatial layout of nodes               |

Loom is **not** a diagram tool.  
Loom is a **real-time simulation engine** with a game-like front end.

---

## Core Principle

> Always build in this order:  
> **Simulation → Routing → Visualization → Editor Tools**

Never start with visuals or UI.

---

## The Foundational Insight (New Paradigm)

Loom operates on two truths at the same time:

### 🧠 Philosophical Truth (what Loom teaches)

> It’s edges all the way down.

A “node” is really just a region where many transport paths exist close together.

A data center, a server, a process, a function — all are just **clusters of paths**.

This is the mental model Loom is meant to reveal.

### 🧱 Engineering Truth (what the simulation needs)

For the simulation to work, we must maintain hierarchy:

> **Nodes and Edges form the routing graph**  
> **Nodes contain internal edges**

This allows:

- Clean routing
- Pathfinding
- Zooming in/out of abstraction layers
- Representing real architecture diagrams
- Eventually showing that the whole system can collapse into “one node”

Both truths are valid — at different layers.

---

## Engine & Stack

- **Engine:** Unity (URP)
- **Architecture:** ECS / DOTS
- **Language:** C#
- **Rendering:** Hybrid (visuals reflect ECS state)
- **Goal:** Simulate thousands–tens of thousands of packets efficiently

---

## The Entire Universe of Loom

Everything in Loom is only three things:

| Thing  | Description                                              |
| ------ | -------------------------------------------------------- |
| Node   | A compute location (server, pod, DB, LB, queue, etc.)    |
| Edge   | A network connection between nodes with length (latency) |
| Packet | A unit traveling across edges                            |

### Critical Rule

> Edges only connect Nodes.  
> Complexity lives _inside_ Nodes.

Packets always conceptually travel:

`Node → Edge → Node → Edge → Node`

But **inside** a node, they physically travel across internal paths.

---

## Architectural Layers of Loom

Loom is built from four subsystems:

1. **Graph System** — Nodes and edges (routing graph)
2. **Packet System** — Packet lifecycle & traversal
3. **Node Logic System** — Queues, processing, dispatching
4. **Rendering & Editor** — Visual and interactive layer

Each layer builds on the previous one.

---

## Current State (Milestone 1 Complete)

We have proven:

- ECS components for:
  - `Node`
  - `EdgeData`
  - `Packet`
  - `PacketRoute`
- `PacketTraverseSystem`
- A bootstrap world
- A visualizer showing packets moving along edges

This proves:

- ECS can handle the simulation
- Distance = time is visible
- The core metaphor works

Packets currently loop on simple edges.

---

## Phase 1 Goal — Sandbox Mode

A playable environment where you can:

- Drag & drop nodes
- Connect nodes with edges
- Watch live packet simulation
- Observe routing, queues, load balancing, latency, and compute visually

---

## Phase 1 Milestones

### ✅ Milestone 1 — Packets traverse edges

Core ECS simulation proven.

### ✅ Milestone 2 — Graph routing

- Nodes know connected edges
- Packets move node → edge → node
- Triangle routing works (A → B → C → A)

### 🎯 Milestone 3 — Internal node mechanics

Nodes stop being points and become **machines**:

- Entry point
- Internal lanes (edges)
- Queues
- Processors
- Exit point

### 🎯 Milestone 4 — Real visual edges

- Splines for edges
- Nodes exist in space
- Packets follow curved paths

### 🎯 Milestone 5 — World builder tools

- Place nodes
- Connect edges
- Live simulation updates

### 🎯 Milestone 6 — Sandbox polish

- Pause / Play / Step
- Metrics overlays
- Adjustable latency & compute
- Visual feedback for congestion

---

## Development Rules

- No GameObjects for packets (ECS only)
- Visuals are a reflection of the simulation, never the source of truth
- Components are pure data (nouns)
- Systems contain all behavior (verbs)
- Organize project by **feature/domain**, not ECS type

---

## What Loom Is Becoming

Loom is effectively:

> “Factorio / SimCity for distributed systems”

A teaching tool, a sandbox, and potentially a training platform.

---

## Immediate Next Step

Build **Milestone 3**:

> Nodes gain internal structure so bottlenecks, queues, and compute time are physically visible inside them — not faked with timers.
