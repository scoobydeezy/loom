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

### Two-Layer Simulation Model

**Layer 1: Global Graph**

```
Node ↔ Edge ↔ Node ↔ Edge ↔ Node
```

- Represents the distributed system topology
- Edges only ever connect Nodes — no exceptions
- Handles inter-service routing

**Layer 2: Internal Node Space**

```
Entry → [internal lanes / queues / edges] → Exit
```

- Each Node is itself a graph
- Compute time = distance traversed inside the node
- Queues emerge from structural congestion, not logic

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
Components:       Node, NodeTransform, NodeType, Edge, Packet, PacketProgress, PacketRouteIndex, QueueState, EdgeCapacity
Buffers:          PacketRoute, NodeQueue
Tags:             PacketInTransitTag, NodeActiveTag, EdgeSaturatedTag
Systems:          PacketTraverseSystem, NodeDispatchSystem, EdgeCapacitySystem, RoutingSystem
Aspects:          PacketAspect, NodeAspect (when grouping related component access)
ScriptableObjects: NodeTypeDefinition (configuration recipes — never enter ECS world directly)
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
- **Preset configurations** for common node types (e.g. a load balancer defaults to round-robin routing across its edges)
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

## What Not To Do (Common AI Mistakes to Avoid)

These patterns are tempting but violate Loom's architecture. Reject them if suggested:

| Anti-Pattern                               | Why It's Wrong            | Correct Alternative                               |
| ------------------------------------------ | ------------------------- | ------------------------------------------------- |
| `yield return new WaitForSeconds(latency)` | Hidden timer, not spatial | Make the edge longer                              |
| `packetState = Processing; timer -= dt;`   | Abstract state machine    | Route packet through internal node graph          |
| Lerp with a fixed duration                 | Duration is a timer       | Lerp with a fixed speed; distance determines time |
| `if (isProcessing) skip` flags             | Invisible logic           | Capacity constraint on the edge                   |
| Separate "queue list" data structure       | Logic-based queue         | Packets physically waiting at edge entry          |

---

## File & Folder Structure

Files are grouped by **domain**, not by type. This keeps all related components, buffers, and systems co-located as the codebase grows. A domain with 10 files stays navigable; a flat `Components/` folder with 40+ files does not.

```
Assets/
└── Loom/
    ├── Bootstrap/
    │   └── LoomBootstrap.cs              # World init, system ordering
    ├── Packets/
    │   ├── Components/
    │   │   ├── Packet.cs
    │   │   ├── PacketProgress.cs         # (soon)
    │   │   └── PacketRouteIndex.cs       # (soon)
    │   ├── Buffers/
    │   │   └── PacketRoute.cs
    │   └── Systems/
    │       └── PacketTraverseSystem.cs
    ├── Nodes/
    │   ├── Components/
    │   │   ├── Node.cs
    │   │   ├── NodeTransform.cs
    │   │   └── NodeType.cs
    │   ├── Buffers/
    │   │   └── NodeQueue.cs              # (soon)
    │   ├── Systems/
    │   │   └── NodeDispatchSystem.cs     # (soon)
    │   └── NodeTypes/                    # ScriptableObject assets (configuration only)
    │       ├── NodeTypeDefinition.cs     # ScriptableObject class definition
    │       ├── WebServer.asset
    │       ├── Database.asset
    │       ├── LoadBalancer.asset
    │       └── Cache.asset
    ├── Edges/
    │   ├── Components/
    │   │   └── Edge.cs
    │   └── Systems/
    │       └── EdgeCapacitySystem.cs     # (soon)
    ├── Rendering/
    │   └── PacketVisualizer.cs           # Hybrid renderer — reads ECS, drives GameObjects/VFX
    └── Prefabs/
```

**Rules for new domains:** If a concept needs more than one component or its own system, it gets a domain folder. Shared utilities that serve multiple domains live in a top-level `Loom/Shared/` folder.

---

## Node Type System

Node configuration is data-driven via **ScriptableObjects**. A `NodeTypeDefinition` asset defines the properties of a class of node. When a node is spawned, `LoomBootstrap` reads the assigned definition and uses its values to compose ECS components and build the node's internal topology. The ScriptableObject itself never enters the ECS world.

### Why ScriptableObjects

- Native to Unity's asset pipeline — no custom parsing
- Appear in the editor as inspectable, drag-and-drop assets
- Live in the project like prefabs; no scene or GameObject required
- Decouples configuration from code — new node types require no new C#

### NodeTypeDefinition Properties

| Property             | Type   | Meaning                                                                 |
| -------------------- | ------ | ----------------------------------------------------------------------- |
| `typeName`           | string | Display label                                                           |
| `laneCount`          | int    | Worker concurrency — how many packets the node processes simultaneously |
| `queueCapacity`      | int    | Max packets waiting at the entry edge before backpressure               |
| `internalPathLength` | float  | Distance packets travel inside the node — determines compute time       |
| `exitCapacity`       | int    | Capacity of the exit edge                                               |

### Built-in Node Types

| Type             | laneCount | queueCapacity | internalPathLength | Notes                                               |
| ---------------- | --------- | ------------- | ------------------ | --------------------------------------------------- |
| **WebServer**    | 16        | 128           | medium             | Many workers, moderate compute                      |
| **Database**     | 4         | 20            | long               | Few connections, expensive queries — saturates fast |
| **LoadBalancer** | 64        | 256           | near-zero          | High concurrency, near-invisible latency            |
| **Cache**        | 1         | 32            | near-zero          | Single-threaded, near-instant response              |

### Lane Count as a First-Class Property

Lane count represents **worker concurrency** — the number of things a node can do simultaneously. It is the single most important property distinguishing node types. A database with 4 lanes behaves fundamentally differently from a web server with 16. Never hard-code lane count; always derive it from the `NodeTypeDefinition`.

---

## Coding Standards

- **Burst-compatible code** wherever possible — avoid managed allocations in hot paths
- **`NativeArray` / `NativeList`** for collections inside jobs
- **`[ReadOnly]`** attributes on job fields that don't write
- XML doc comments on all public-facing Components and Systems
- Systems should have a single, clearly named responsibility
- Prefer composition over inheritance — always

---

## Philosophy

> Simulation first, visualization second.
> The simulation is never explained — it is observed.
> All complexity must emerge from structure, not logic.
