# Loom — Distributed Systems Sandbox Simulator

Loom is a real-time spatial simulator that makes distributed systems tangible.

Latency, throughput, queuing, routing, load balancing, and compute time are not
numbers or timers — they are **visible motion through constrained geometry**, expressed as beads on strings.

> Distributed systems are packets moving through space.

---

## The Prime Directive

**Everything in Loom must be explainable as movement through constrained geometry.**

- Distance is time
- Physical density (bead packing) is natural throughput; explicit rate limits are a mechanism concern
- Congestion is queuing
- Structure creates behavior
- No timers, no hidden delays, no abstract “processing” states

If it cannot be seen in the scene, it does not exist in the simulation.

---

## The Four Primitives

Everything in Loom is built from four primitives. They exist for different reasons
and must not be conflated.

| Primitive     | Role        | Why it exists                                    |
| ------------- | ----------- | ------------------------------------------------ |
| **Node**      | Containment | Forces packets to traverse internal structure    |
| **Edge**      | Transport   | Distance = latency; physical presence constrains flow |
| **Mechanism** | Decision    | The only place routing or filtering logic lives  |
| **Packet**    | Traveler    | Moves, carries a destination, makes no decisions |

---

## The Two Mental Models (both true)

### From the packet’s perspective (topology)

```
Edge → Mechanism → Edge → Mechanism → Edge
```

Packets never experience “a node”.  
They experience paths and decision points.

### From the world’s perspective (containment)

```
Edge → [Node containing edges and mechanisms] → Edge
```

Nodes are real simulation entities. They prevent packets from bypassing
what is intended to be “inside” a server, load balancer, database, etc.

This dual truth is the core insight of Loom.

---

## How Behavior Emerges

Nothing is scripted.

| Behavior     | Emerges from                                      |
| ------------ | ------------------------------------------------- |
| Queuing      | Physical packet density — BeadDiameter packing    |
| Backpressure | Packets waiting at blocked edge entries           |
| Compute time | Distance traveled inside a node                   |
| Routing      | Mechanisms selecting outbound edges               |
| Node “type”  | Recognized from internal structure (recipes)      |

Nodes are places.  
Packets are dumb.  
Mechanisms are the only actors.

---

## Recursive Simulation

A node is not a point. It is a graph.

That graph is built from more nodes, edges, and mechanisms, and those nodes
can contain their own graphs. This recursion is unbounded.

```
Node
 └── Edge → Mechanism → Edge → Node
                                └── (contains its own graph)
```

Zooming in reveals more structure — not decoration.

---

## Traversal Rules

- Plain node, **1 outbound edge** → auto-forward
- Plain node, **0 outbound edges** → packet waits (correct backpressure)
- Plain node, **>1 outbound edges** → misconfiguration (no mechanism to choose)
- Mechanism → stamps `AwaitingRouting`, `MechanismSystem` selects next edge

Mechanisms own all routing decisions — never nodes, never packets.

---

## Node Types (Recipes)

Node types are **recipes** (`NodeTypeDefinition` ScriptableObjects) describing
how a node is assembled from child nodes and mechanisms.

They never enter the ECS world.

- Entry point = first child in the recipe
- Exit points = all entities in the last child group
- Type is **recognized**, not declared (Phase 2)

Examples:

| Asset          | Recipe                         |
| -------------- | ------------------------------ |
| ProcessingLane | Leaf node                      |
| WebServer      | Route ×1 → ProcessingLane ×16  |
| Database       | Route ×1 → ProcessingLane ×4   |
| LoadBalancer   | Route ×1 → ProcessingLane ×8   |
| Cache          | ProcessingLane ×1              |
| QueueHolding   | Leaf node (high-density buffer)|
| Queue          | Filter ×1 → QueueHolding ×1 → Route ×1 |

All lanes exit independently. No collector mechanism.

---

## Stack

- **Engine:** Unity (URP)
- **Architecture:** ECS / DOTS (C#)
- **Rendering:** Hybrid ECS-driven visualizers
- **Scale target:** 10,000–100,000+ packets

---

## Design Rules

- Only four primitives exist: Node, Edge, Mechanism, Packet
- Components are data; Systems contain behavior
- No timers, no abstract states, no fake queues
- Mechanisms chain for complex behavior (never stack policies)
- Nodes are containment boundaries, not visual groupings
- A plain node with multiple outbound edges is a visible mistake

---

## Current State

Loom currently supports:

- ECS packet traversal across arbitrary graphs
- Mechanism-based routing
- Recursive node construction from recipes
- Internal node graphs producing real compute time
- Density-based congestion and backpressure (BeadDiameter packing)
- Edge and packet visualization driven directly from ECS state

A triangle of mixed node types can run indefinitely with observable congestion,
load balancing, and queuing — without any scripted behavior.

---

## Phase 1 Goal — Sandbox

An interactive environment to place nodes, connect edges, and observe live
packet flow where latency, throughput, routing, and congestion are physically visible.

| Milestone | Status | Description                                  |
| --------- | ------ | -------------------------------------------- |
| 1         | ✅     | Packet movement on edges                     |
| 2         | ✅     | Graph routing across loops                   |
| 3         | ✅     | Internal node physics with mechanisms        |
| 4         | 🎯     | Spatial geometry & splines                   |
| 5         | 🎯     | World builder tools                          |
| 6         | 🎯     | Sandbox polish (pause, metrics, adjustments) |

---

## Philosophy

> Simulation first. Visualization second.  
> The simulation is never explained — it is observed.  
> All complexity must emerge from structure, not logic.
