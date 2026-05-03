# CLAUDE.md — Loom Project Context

> This file establishes full context for AI-assisted development of Loom.
> Read this before making any code suggestions, architectural decisions, or modifications.

---

## What Is Loom?

Loom is a real-time spatial simulator that makes distributed systems tangible. Abstract concepts — latency, throughput, queuing, routing, load balancing — become observable motion in physical space.

**Core premise:** Distributed systems are packets moving through constrained geometry. All system behavior must emerge from movement and structure alone.

---

## The Prime Directive

> **Everything must be explainable as movement through constrained geometry.**

This is not a stylistic preference. It is an architectural constraint that governs every decision.

**Never suggest:**

- Timers or coroutine-based delays to simulate latency
- `Invoke()` / `InvokeRepeating()` for packet pacing
- Abstract "processing states" or invisible wait logic
- Anything that cannot be observed spatially

**Always prefer:**

- Distance as the mechanism for time
- Capacity as the mechanism for throughput limits
- Congestion (structural crowding) as the mechanism for queuing
- ECS Systems as the mechanism for all behavior

---

## Physical Metaphor Reference

| Concept      | Implementation                                                          |
| ------------ | ----------------------------------------------------------------------- |
| Node         | A compute location (server, service, DB) — a physical position in space |
| Edge         | A constrained path between nodes — latency = path length                |
| Packet       | A unit of work — a moving entity traversing edges                       |
| Throughput   | The capacity of an edge (max concurrent packets)                        |
| Compute time | Distance traveled inside a node's internal graph                        |
| Routing      | Path selection logic across edges                                       |
| Queuing      | Packets waiting at a saturated edge entry point                         |

---

## Architecture

### Recursive Simulation Model

The architecture is recursive. A node can contain a graph of child nodes and edges. Those child nodes can themselves contain graphs. The same mechanic — packets moving through edges — applies at every level of nesting.

```
Topology (nodes + edges)
    └── Node (contains a graph of child nodes + edges)
            └── Node (contains a graph of child nodes + edges)
                    └── Node (leaf — no children, just position and edge properties)
```

There is no special "Part" type. There is no distinction between a processing lane, a node interior, and a cluster. They are all nodes connected by edges. The only structural concept is nesting depth, expressed via the `NodeParent` component.

**Layer 1: Global Graph**

```
Node ↔ Edge ↔ Node ↔ Edge ↔ Node
```

- Represents the distributed system topology
- Edges only ever connect Nodes — no exceptions
- Handles inter-service routing

**Layer 2+: Internal Node Space**

```
Entry → [child nodes + edges] → Exit
```

- Each Node is itself a graph of child Nodes connected by Edges
- Compute time = distance traversed inside the node
- Queues emerge from structural congestion, not logic
- Nesting is unbounded — a cluster is a node whose children are themselves composite nodes

### Exit Points

Most nodes have a single exit point. Nodes with multiple egress children (e.g. a load balancer with 8 egress lanes) have **multiple exit points** — one per egress child. In the world builder, each exit point wires to a distinct downstream node. `SpawnResult.ExitNode` must become `SpawnResult.ExitNodes` (plural) before the world builder is implemented.

### System Layers (in dependency order)

| Layer            | Responsibility                                        |
| ---------------- | ----------------------------------------------------- |
| Graph System     | Global topology — nodes, edges, connectivity          |
| Packet System    | Movement and traversal of packets along edges         |
| Node System      | Internal structure — lanes, queues, entry/exit points |
| Rendering System | Visualization of simulation state                     |

---

## Tech Stack

| Concern      | Choice                               |
| ------------ | ------------------------------------ |
| Engine       | Unity (URP pipeline)                 |
| Architecture | ECS / DOTS                           |
| Language     | C#                                   |
| Rendering    | Hybrid ECS-driven                    |
| Scale target | 10,000–100,000+ simultaneous packets |

---

## ECS/DOTS Principles

This project uses Unity's Entity Component System. Always adhere to ECS idioms:

- **Components = data only.** No methods, no logic, no references to MonoBehaviours.
- **Systems = behavior.** All logic lives in Systems, not Components.
- **Entities = identity.** Nodes, edges, and packets are all Entities.
- **Prefer `IJobEntity` and Burst-compiled jobs** for any per-packet or per-edge logic.
- **No `MonoBehaviour` in simulation logic.** MonoBehaviours are acceptable only for editor tooling and UI.
- **Structural changes** (adding/removing components) must go through `EntityCommandBuffer`, not direct mutation inside jobs.

### ECS Naming Conventions

Components are named after the concept they represent — not suffixed with `Data`. The component **is** the thing, not a description of it. `Node.cs` is the Node. `Packet.cs` is the Packet. This reinforces that simulation entities are defined entirely by their components, and that no "real" counterpart exists elsewhere.

```
Components:        Node, NodeTransform, NodeType, NodeParent, Edge, Packet, PacketProgress, PacketRouteIndex, QueueState, EdgeCapacity
Buffers:           PacketRoute, NodeQueue
Tags:              PacketInTransitTag, NodeActiveTag, EdgeSaturatedTag
Systems:           PacketTraverseSystem, NodeDispatchSystem, EdgeCapacitySystem, RoutingSystem
Aspects:           PacketAspect, NodeAspect (when grouping related component access)
MonoBehaviours:    LoomBootstrap, PacketVisualizer, EdgeVisualizer (editor/rendering only)
ScriptableObjects: NodeTypeDefinition (recipe descriptors — never enter ECS world directly)
```

---

## Core Design Rules

### Graph Integrity

- Edges **only** connect Nodes. An edge must have a valid source Node and destination Node.
- Nodes define system boundaries. Nothing crosses a Node boundary without entering its internal graph.

### Movement Rule

- All system behavior must emerge from packet movement through edges.
- Speed, distance, and capacity are the only levers.

### No Hidden Logic

- No `WaitForSeconds`, no `Invoke`, no coroutine-based delays.
- No boolean flags that represent "processing" without spatial meaning.
- No simulation states that aren't directly observable in the scene.

### Emergence Over Scripting

- Queuing is not implemented — it emerges when edge capacity is saturated.
- Backpressure is not implemented — it emerges from queue depth.
- Congestion is not implemented — it emerges from packet density.
- Node type is not declared — it is recognized by matching assembled structure against a recipe.

---

## Node Type System

Node configuration is data-driven via **ScriptableObjects**. A `NodeTypeDefinition` is a recipe — a description of how a node should be assembled from child nodes and edges. It is used by `LoomBootstrap` (and eventually the world builder) to construct the ECS entity graph. The ScriptableObject itself never enters the ECS world.

### NodeTypeDefinition Properties

| Property             | Type         | Meaning                                                                 |
| -------------------- | ------------ | ----------------------------------------------------------------------- |
| `typeName`           | string       | Display label                                                           |
| `children`           | ChildEntry[] | Ordered list of child node types and counts                             |
| `edgeCapacity`       | int          | Capacity of edges connecting this node to its siblings                  |
| `internalPathLength` | float        | Travel distance through this node (leaf nodes only — ignored otherwise) |

**ChildEntry fields:** `definition` (NodeTypeDefinition), `count` (int), `role` (string — human-readable label only, not enforced by simulation).

### Leaf vs. Composite Nodes

- **Leaf nodes** have no children. Their behavior is defined entirely by `edgeCapacity` and `internalPathLength`.
- **Composite nodes** have children. Their behavior emerges from the graph those children form. `internalPathLength` is ignored on composite nodes.

### Built-in Node Type Assets

| Asset            | Type      | Children                                 | Notes                              |
| ---------------- | --------- | ---------------------------------------- | ---------------------------------- |
| `Intake`         | Leaf      | —                                        | edgeCapacity: 50, pathLength: 0.2  |
| `ProcessingLane` | Leaf      | —                                        | edgeCapacity: 1, pathLength: 3.0   |
| `Egress`         | Leaf      | —                                        | edgeCapacity: 50, pathLength: 0.2  |
| `WebServer`      | Composite | Intake x1, ProcessingLane x16, Egress x1 |                                    |
| `Database`       | Composite | Intake x1, ProcessingLane x4, Egress x1  | Saturates fast                     |
| `LoadBalancer`   | Composite | Intake x1, ProcessingLane x1, Egress x8  | Near-zero path length, wide egress |
| `Cache`          | Composite | Intake x1, ProcessingLane x1, Egress x1  | Near-zero path length              |

### Type Recognition (Future)

Node type is not declared at runtime — it is **recognized**. A recipe matcher compares assembled node structure against `NodeTypeDefinition` patterns and surfaces a label when there is a match. This is a UI/presentation concern only. The simulation has no concept of type. A misconfigured node that happens to perform like a load balancer is not recognized as one — the recipe matches structure, not behavior.

This system does not exist yet. It belongs in Phase 2.

---

## Rendering

All rendering is handled by hybrid MonoBehaviours that read ECS state and drive GameObjects. The simulation is never aware of the renderer.

### EdgeVisualizer

`EdgeVisualizer.cs` renders every edge in the simulation — global and internal — as a `LineRenderer`. It reads `Edge` component data each frame, resolves `FromNode` and `ToNode` positions from `NodeTransform`, and updates line endpoints. Node positions are the single source of truth — the visualizer never caches positions.

All edges at all nesting depths are rendered. There is no filtering by depth.

### PacketVisualizer

`PacketVisualizer.cs` renders packets as moving objects. Each packet lerps between its current edge's `FromNode` and `ToNode` positions using its `Progress` value. Child nodes have their own positions, so internal traversal visualizes naturally with no special cases.

---

## File & Folder Structure

Files are grouped by **domain**, not by type.

```
Assets/
├── Bootstrap/
│   └── LoomBootstrap.cs              # World init, recursive node spawner
├── Packets/
│   ├── Components/
│   │   └── Packet.cs
│   ├── Buffers/
│   │   └── PacketRoute.cs
│   └── Systems/
│       └── PacketTraverseSystem.cs
├── Nodes/
│   ├── Components/
│   │   ├── Node.cs
│   │   ├── NodeTransform.cs
│   │   ├── NodeType.cs
│   │   └── NodeParent.cs             # Marks a node as belonging to another node's internal graph
│   └── NodeTypes/                    # ScriptableObject recipe assets
│       ├── NodeTypeDefinition.cs
│       ├── Intake.asset
│       ├── ProcessingLane.asset
│       ├── Egress.asset
│       ├── WebServer.asset
│       ├── Database.asset
│       ├── LoadBalancer.asset
│       └── Cache.asset
├── Edges/
│   └── Components/
│       └── Edge.cs
└── Rendering/
    ├── PacketVisualizer.cs           # Hybrid renderer — reads ECS, drives GameObjects
    └── EdgeVisualizer.cs             # Renders every edge as a LineRenderer
```

**Rules for new domains:** If a concept needs more than one component or its own system, it gets a domain folder. Shared utilities that serve multiple domains live in a top-level `Shared/` folder.

---

## Known Future Work

These are architectural decisions made but not yet implemented. Do not work around them — implement them when their milestone arrives.

- **`SpawnResult.ExitNode` → `ExitNodes` (plural):** Load balancers and other wide-egress nodes have multiple exit points. Each wires to a distinct downstream node. This must be resolved before the world builder (Milestone 5) is implemented.
- **Recipe matcher:** Structural pattern recognition that compares assembled node graphs against `NodeTypeDefinition` recipes and surfaces a type label. Belongs in Phase 2.
- **Node dragging:** Nodes will be draggable mid-simulation. `EdgeVisualizer` and `PacketVisualizer` already support this — they read positions every frame. No changes needed to the renderers when this is implemented.

---

## What Not To Do (Common AI Mistakes to Avoid)

These patterns are tempting but violate Loom's architecture. Reject them if suggested:

| Anti-Pattern                               | Why It's Wrong                        | Correct Alternative                               |
| ------------------------------------------ | ------------------------------------- | ------------------------------------------------- |
| `yield return new WaitForSeconds(latency)` | Hidden timer, not spatial             | Make the edge longer                              |
| `packetState = Processing; timer -= dt;`   | Abstract state machine                | Route packet through internal node graph          |
| Lerp with a fixed duration                 | Duration is a timer                   | Lerp with a fixed speed; distance determines time |
| `if (isProcessing) skip` flags             | Invisible logic                       | Capacity constraint on the edge                   |
| Separate "queue list" data structure       | Logic-based queue                     | Packets physically waiting at edge entry          |
| `Part` component or slot-type enums        | Redundant abstraction                 | Nodes are nodes at every level of nesting         |
| Declaring node type at spawn time          | Type is recognized, not declared      | Recipe matcher reads structure, surfaces label    |
| Single `ExitNode` for wide-egress nodes    | Breaks load balancer fan-out topology | `ExitNodes` (plural), one per egress child        |

---

## Coding Standards

- **Burst-compatible code** wherever possible — avoid managed allocations in hot paths
- **`NativeArray` / `NativeList`** for collections inside jobs
- **`[ReadOnly]`** attributes on job fields that don't write
- XML doc comments on all public-facing Components and Systems
- Systems should have a single, clearly named responsibility
- Prefer composition over inheritance — always

---

## Product Roadmap

This roadmap describes the _intended scope_ of each phase. It is not a task tracker — refer to project management tooling for milestone status.

---

### Phase 1 — Sandbox

**Goal:** A working physics engine for distributed systems. Prove the spatial metaphor holds.

The user can construct arbitrary topologies from scratch and watch them run. No templates, no presets — just nodes, edges, and packets.

**Capabilities:**

- Place and remove nodes freely in 2D space
- Connect nodes with edges; configure edge length and capacity
- Observe live packet flow, congestion, and routing in real time
- Pause, step, and resume the simulation
- Adjust capacity and speed while the simulation runs
- Internal node physics: packets traverse a subgraph inside each node, producing emergent compute time and queue formation

**Definition of done:** A user with no context can open Loom, build a triangle topology, and observe congestion without reading any documentation.

---

### Phase 2 — Vocabulary

**Goal:** Give the simulation domain knowledge. Nodes and edges become typed, and Loom can represent real-world architectures — not just abstract graphs.

**Capabilities:**

- **Typed nodes** with distinct visual identities and behavioral properties: web server, database, load balancer, cache, message queue, CDN, DNS resolver, API gateway, etc.
- **Typed edges** with protocol semantics: HTTP, TCP, UDP, WebSocket — affecting packet behavior and capacity defaults
- **Packet typing**: read requests, write requests, health checks, cache hits — each with different routing or priority characteristics
- **Recipe matching**: assembled node structures are compared against `NodeTypeDefinition` patterns; matching structures surface a type label organically
- **Architecture templates**: FTP server, three-tier web app, basic microservices cluster — importable starting points

**Definition of done:** A user can assemble a recognizable, labeled architecture (e.g. "Nginx → App Server → Postgres") and have it behave differently from an unlabeled graph of the same shape.

---

### Phase 3 — Scenarios

**Goal:** Structured learning on top of the sandbox. Loom becomes a teaching tool with guided experiences and a curriculum path.

**Capabilities:**

- **Scenario mode**: pre-built architectures with guided objectives ("add a cache to reduce DB load", "re-route traffic around a failed node")
- **Curriculum progression**: concepts introduced in sequence — from single-server to distributed, from static to dynamic, from reliable to fault-tolerant
- **Annotated architectures**: nodes and edges surface explanatory context without breaking simulation immersion
- **Comparison mode**: run two topologies side-by-side to observe behavioral differences
- **Challenge mode**: given a traffic pattern and constraints, design an architecture that meets the target metrics

**Definition of done:** A user with no networking background can complete a structured scenario path that takes them from a single web server to a multi-region load-balanced system.

---

### Phase 4 — Simulation Depth

**Goal:** Add consequence. The simulation now models cost, failure, and security — and the user must manage them. This is where gamification lives.

**Capabilities:**

- **Cost model**: each node carries a running cost per unit time; edges carry data transfer cost; packets accumulate cost as they traverse the system
- **Failure simulation**: nodes and edges can fail stochastically or on demand; packets are dropped, rerouted, or lost
- **Redundancy mechanics**: replication, failover, and health-check routing emerge as survival strategies, not abstractions
- **Security layer**: hostile packets (unauthorized requests, DDoS traffic, intrusion attempts) spawn and traverse the graph; firewalls and rate-limiters are node types that intercept them
- **Metrics dashboard**: latency percentiles, throughput, error rate, cost per request — derived from observed packet behavior, not injected data
- **Budget constraints**: scenario mode includes cost ceilings; users optimize for performance within a budget

**Definition of done:** A user can observe a cascading failure, diagnose it by watching packet behavior, and redesign the architecture to survive it — all while staying within a cost budget.

---

### Phase 5 — Platform

**Goal:** Loom becomes a shared tool. Users can save, publish, and build on each other's architectures.

**Capabilities:**

- **Save / load** architecture files (serialized graph state)
- **Export** as shareable blueprints or embeddable diagrams
- **Blueprint library**: community-contributed architectures, tagged by domain (web, data pipeline, CI/CD, cloud-native)
- **Diff and versioning**: compare two versions of an architecture and observe how behavior changes
- **API / headless mode**: run simulations programmatically for integration with external tools or curriculum platforms

**Definition of done:** A user can publish an architecture blueprint, and another user can import and extend it without any direct coordination.

---

## Philosophy

> Simulation first, visualization second.
> The simulation is never explained — it is observed.
> All complexity must emerge from structure, not logic.
