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

| Concept | Physical Representation |
|---|---|
| Connection | String / line / spline |
| Packet | Bead moving on the string |
| Latency | Distance of the string |
| Compute time | Physical size / delay inside a node |
| Routing | Which string the bead moves onto next |
| Proximity | Spatial layout of nodes |

Loom is **not** a diagram tool.  
Loom is a **real-time simulation engine** with a game-like front end.

---

## Core Principle

> Always build: **Simulation → Routing → Visualization → Editor Tools**

Never start with visuals or UI.

---

## Engine & Stack

- **Engine:** Unity (URP)
- **Architecture:** ECS / DOTS
- **Language:** C#
- **Rendering:** Lightweight, data-driven
- **Goal:** Simulate thousands–tens of thousands of packets efficiently

---

## The Entire Universe of Loom

Everything in Loom is only three things:

| Thing | Description |
|---|---|
| Node | A machine/service (server, LB, DB, queue, etc.) |
| Edge | A connection between nodes with length (latency) |
| Packet | A unit traveling across edges |

---

## Current State (Milestone 1 Complete)

We have:

- ECS components:
  - `Node`
  - `Edge`
  - `Packet`
- `PacketMoveSystem` that advances packets over time
- A bootstrap that creates:
  - 2 nodes
  - 1 edge
  - 1000 packets
- A visualizer that shows beads moving along a line

This proves:
- ECS can handle the simulation
- Distance = time is visible
- The core concept works

Packets currently loop on a single edge.

---

## Phase 1 Goal — Sandbox Mode

A playable environment where you can:

- Drag & drop nodes
- Connect nodes with edges
- Watch live packet simulation
- Observe routing, queues, load balancing, latency, and compute visually

---

## Phase 1 Milestones

### ✅ Milestone 1 — Packets move on edges
Core ECS simulation proven.

### 🎯 Milestone 2 — Graph routing
- Nodes know connected edges
- Packets traverse node → edge → node
- Packets can move around a graph (e.g., A → B → C → A)

### 🎯 Milestone 3 — Node behaviors
Add node types:
- Forwarder
- Load Balancer (round robin)
- Queue (backlog)
- Processor (compute delay)

### 🎯 Milestone 4 — Real visual edges
- Use splines for edges
- Nodes exist in space
- Packets follow paths

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

## Architectural Layers

Loom is built from four subsystems:

1. **Graph System** — Nodes and edges
2. **Packet System** — Packet lifecycle & movement
3. **Node Logic System** — Routing, queues, compute
4. **World Editor** — Player tools for building networks

Each layer builds on the previous one.

---

## What Loom Is Becoming

Loom is effectively:

> “SimCity / Factorio for distributed systems”

A teaching tool, a sandbox, and potentially a training product.

---

## Development Rules

- No GameObjects for packets (ECS only)
- Visuals are a reflection of the simulation, never the source of truth
- All behavior is data-driven
- Always prove behavior in code before making it pretty

---

## Immediate Next Step (Milestone 2)

Teach packets to:

> Arrive at a node, have the node choose the next edge, and continue moving through the graph.

Success condition:

A hardcoded triangle:

A → B → C → A

with packets continuously circulating.