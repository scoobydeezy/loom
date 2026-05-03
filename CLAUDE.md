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

- Distance is the mechanism for time
- Capacity is the mechanism for throughput limits
- Congestion (structural crowding) is the mechanism for queuing
- No timers, no `Invoke`, no coroutine-based delays
- No boolean flags that represent "processing" without spatial meaning
- No simulation states that aren't directly observable in the scene

---

## The Four Primitives

Everything in Loom is built from four primitives. They exist for distinct reasons — do not conflate them.

| Primitive     | Role        | Why it exists                                          |
| ------------- | ----------- | ------------------------------------------------------ |
| **Node**      | Containment | Enforces that packets must traverse internal structure |
| **Edge**      | Transport   | Distance = latency; capacity = throughput              |
| **Mechanism** | Decision    | The only place routing logic lives                     |
| **Packet**    | Traveler    | Carries a destination; makes no decisions              |

### Why Node and Mechanism are different primitives

This is a common point of confusion. They serve entirely different purposes:

- A **Mechanism** answers: _"Which edge next?"_ It is a decision point.
- A **Node** answers: _"You must traverse my interior before reaching the next external edge."_ It is a containment boundary.

Without Nodes as real simulation entities, nothing prevents a packet from bypassing what was intended to be "inside the server." Nodes give the recursion structural weight — zooming in reveals more graph, not decoration. A Node is not UI chrome.

Without Mechanisms as real simulation entities, there is no place for routing logic to live. Nodes are places. Packets are dumb. Mechanisms are the only actors.

No other structural concepts exist. A load balancer is nodes and edges with a routing mechanism. A firewall is a mechanism that drops hostile packets. A cache is a mechanism that short-circuits processing on a hit.

### The two correct mental models (held simultaneously)

**From the packet's perspective (topology):**

```
Edge → Mechanism → Edge → Mechanism → Edge
```

Packets never experience "a node." They experience transport paths and decision points.

**From the world's perspective (containment):**

```
Edge → [Node containing Mechanisms and Edges] → Edge
```

Nodes enforce that packets must traverse internal structure. Both views are true at the same time. That is Loom's core insight.

### Mechanism chaining (not stacking)

Each mechanism has exactly one responsibility. Complex behavior is created by chaining mechanisms — not by stacking multiple policies on one mechanism entity. If a packet needs to be rate-limited _and_ filtered, that is two mechanism entities with an edge between them, not one mechanism doing both. This keeps every decision point observable and single-responsibility. This rule becomes critical in Phase 4.

---

## Architecture

### Recursive Simulation Model

The model is recursive. Nodes, edges, and mechanisms compose into graphs. Those graphs can be nested inside nodes. The same traversal logic applies at every level.

```
Topology
    └── Node → Edge → Mechanism → Edge → Node
```

A node's internal structure is a graph of child nodes, edges, and mechanisms:

```
Entry → [Mechanism] → Node → [Mechanism] → Node → Exit
```

Those inner nodes can contain their own graphs. Nesting is unbounded. A "database query processor" is not a black box — it is a parsing mechanism feeding into an index lookup node feeding into a result assembly node, each with their own mechanisms.

There is no special "Part" type. A processing lane, a node interior, a cluster, and a pod are all the same thing at different scales.

### Global vs. Internal Graph

**Global graph** (`Node → edges → Node`):

- Represents the distributed system topology
- Exit lanes wire directly to the destination node's entry point — no inter-node mechanisms
- Mechanisms only exist _inside_ nodes as part of their internal structure

**Internal node graph** (`Entry → [Mechanism + Nodes + Edges] → Exit`):

- Each node is itself a graph of nodes, edges, and mechanisms
- Compute time = distance traversed inside the node
- Queues emerge from structural congestion, not logic

### Entry and Exit Points

- **Entry point** = first child in the recipe, whatever type it is
- **Exit points** = all entities in the last child group (`SpawnResult.ExitNodes` is an array)
- No dedicated Intake or Egress types exist — entry and exit are structural positions, not named things
- A mechanism as first child is auto-wired as the entry point and handles distribution immediately

### Traversal Rules

- **Plain node, 1 outbound edge:** `PacketTraverseSystem` forwards directly — no mechanism needed
- **Plain node, 0 outbound edges:** packet waits — correct backpressure behavior
- **Plain node, >1 outbound edges:** misconfiguration — packets pile up visibly (observable, intentional consequence)
- **Mechanism:** `PacketTraverseSystem` stamps `AwaitingRouting`; `MechanismSystem` selects outbound edge from `MechanismConnections`

### Emergence Over Scripting

These behaviors are not implemented — they emerge:

- **Queuing** — emerges when edge capacity is saturated
- **Backpressure** — emerges from queue depth
- **Congestion** — emerges from packet density
- **Node type** — recognized by matching assembled structure against a recipe, never declared
- **Routing** — emerges from mechanisms reading packet destinations and edge congestion

### System Layers (dependency order)

| Layer            | Responsibility                                           |
| ---------------- | -------------------------------------------------------- |
| Graph System     | Global topology — nodes, edges, mechanisms, connectivity |
| Packet System    | Movement and traversal of packets along edges            |
| Mechanism System | Packet interception — routing, filtering, transformation |
| Node System      | Internal structure — child graphs, entry/exit points     |
| Rendering System | Visualization of simulation state                        |

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

- **Components = data only.** No methods, no logic, no references to MonoBehaviours.
- **Systems = behavior.** All logic lives in Systems, not Components.
- **Entities = identity.** Nodes, edges, mechanisms, and packets are all Entities.
- **Prefer `IJobEntity` and Burst-compiled jobs** for any per-packet or per-edge logic.
- **No `MonoBehaviour` in simulation logic.** MonoBehaviours are acceptable only for editor tooling and UI.
- **Structural changes** (adding/removing components) must go through `EntityCommandBuffer`, not direct mutation inside jobs.

### ECS Naming Conventions

Components are named after the concept they represent — not suffixed with `Data`. The component **is** the thing.

```
Components:        Node, NodeTransform, NodeType, NodeParent, Edge, Mechanism, MechanismType, Packet, PacketDestination, WaitingAtNode, QueueState, EdgeCapacity
Buffers:           MechanismConnections
Tags:              AwaitingRouting, WaitingAtNode, NodeActiveTag, EdgeSaturatedTag
Systems:           PacketTraverseSystem, MechanismSystem
Aspects:           PacketAspect, NodeAspect (when grouping related component access)
MonoBehaviours:    LoomBootstrap, PacketVisualizer, EdgeVisualizer (editor/rendering only)
ScriptableObjects: NodeTypeDefinition (recipe descriptors — never enter ECS world directly)
```

---

## Node Type System

Node configuration is data-driven via **ScriptableObjects**. A `NodeTypeDefinition` is a recipe — a description of how a node should be assembled from child nodes and mechanisms. It is used by `LoomBootstrap` (and eventually the world builder) to construct the ECS entity graph. The ScriptableObject itself never enters the ECS world.

### NodeTypeDefinition Properties

| Property   | Type            | Meaning                      |
| ---------- | --------------- | ---------------------------- |
| `typeName` | string          | Display label                |
| `children` | NodeTypeChild[] | Ordered list of child groups |

**NodeTypeChild fields:**

| Field             | Type               | Meaning                                                         |
| ----------------- | ------------------ | --------------------------------------------------------------- |
| `childType`       | ChildType          | `Node` or `Mechanism`                                           |
| `definition`      | NodeTypeDefinition | Child node recipe — used when `childType == Node`               |
| `mechanismKind`   | MechanismKind      | Route / Filter / RateLimit — used when `childType == Mechanism` |
| `count`           | int                | How many of this child to spawn                                 |
| `role`            | string             | Human-readable label only — not enforced by simulation          |
| `outEdgeLength`   | float              | Length of edges from this child to the next group               |
| `outEdgeCapacity` | int                | Capacity of edges from this child to the next group             |

For `ChildType.Node` children, `outEdgeLength` and `outEdgeCapacity` are read from the child's `NodeTypeDefinition` (`internalPathLength` and `edgeCapacity`). For `ChildType.Mechanism` children, they are set directly on the `NodeTypeChild`.

### Leaf vs. Composite Nodes

- **Leaf nodes** have no children. Behavior is defined by `edgeCapacity` and `internalPathLength`.
- **Composite nodes** have children. Behavior emerges from the graph those children form.

### Built-in Node Type Assets

#### System Architecture Primitives

| Asset            | Type      | Recipe                        | Notes                                   |
| ---------------- | --------- | ----------------------------- | --------------------------------------- |
| `ProcessingLane` | Leaf      | —                             | edgeCapacity: 1, pathLength: 3.0        |
| `WebServer`      | Composite | Route x1 → ProcessingLane x16 | Distributes across 16 independent lanes |
| `Database`       | Composite | Route x1 → ProcessingLane x4  | Distributes across 4 independent lanes  |
| `LoadBalancer`   | Composite | Route x1 → ProcessingLane x8  | Distributes across 8 independent lanes  |
| `Cache`          | Composite | ProcessingLane x1             | Single lane, no routing needed          |

#### Physical Pattern Library

| Asset            | Type      | Recipe                                    | Notes                                          |
| ---------------- | --------- | ----------------------------------------- | ---------------------------------------------- |
| `QueueHolding`   | Leaf      | —                                         | edgeCapacity: 100, pathLength: 0.1; buffer node where packets visibly accumulate |
| `Queue`          | Composite | Filter x1 → QueueHolding x1 → Route x1   | **Phase 1 structural scaffolding.** Enforces admission control (Filter), materializes buffering (QueueHolding), distributes (Route). Drop logic is Phase 4; currently passes packets through. |

**Physical Pattern Library** — Nodes that make latent distributed-systems behavior visible as observable structure.

All composite nodes end with lane nodes as exit points. No collector mechanism. Each lane exits independently and wires directly to the destination node's entry point.

### Type Recognition (Phase 2)

Node type is not declared at runtime — it is **recognized**. A recipe matcher compares assembled node structure against `NodeTypeDefinition` patterns and surfaces a label on match. This is a UI/presentation concern only. The simulation has no concept of type.

---

## Rendering

All rendering is handled by hybrid MonoBehaviours that read ECS state. The simulation is never aware of the renderer.

- **`EdgeVisualizer.cs`** — renders every edge at every nesting depth as a `LineRenderer`. Reads `Edge` component data each frame; resolves positions from `NodeTransform`. Node positions are the single source of truth — never cached.
- **`PacketVisualizer.cs`** — lerps packets between `FromNode` and `ToNode` positions using `Progress`. Internal traversal visualizes naturally with no special cases.

---

## File & Folder Structure

Files are grouped by **domain**, not by type.

```
Assets/
├── Bootstrap/
│   └── LoomBootstrap.cs              # World init, recursive node spawner
├── Packets/
│   ├── Components/
│   │   ├── Packet.cs                 # CurrentEdge, Progress, Speed
│   │   └── PacketDestination.cs      # Target node entity — read by mechanisms
│   └── Systems/
│       └── PacketTraverseSystem.cs   # Moves packets, stamps AwaitingRouting or WaitingAtNode
├── Nodes/
│   ├── Components/
│   │   ├── Node.cs
│   │   ├── NodeTransform.cs
│   │   ├── NodeType.cs               # Display label only — not a simulation concept
│   │   └── NodeParent.cs             # Marks a node as belonging to another node's internal graph
│   └── NodeTypes/
│       ├── NodeTypeDefinition.cs     # Recipe class — ChildType enum, NodeTypeChild struct
│       ├── ProcessingLane.asset      # Leaf — edgeCapacity:1, pathLength:3.0
│       ├── WebServer.asset           # Route x1 → ProcessingLane x16
│       ├── Database.asset            # Route x1 → ProcessingLane x4
│       ├── LoadBalancer.asset        # Route x1 → ProcessingLane x8
│       ├── Cache.asset               # ProcessingLane x1
│       ├── QueueHolding.asset        # Leaf buffer node — edgeCapacity:100, pathLength:0.1
│       └── Queue.asset               # Filter x1 → QueueHolding x1 → Route x1
├── Edges/
│   └── Components/
│       └── Edge.cs                   # FromNode, ToNode, Length, Capacity, Occupancy
├── Mechanisms/
│   ├── Components/
│   │   ├── Mechanism.cs              # Marker tag
│   │   ├── MechanismType.cs          # MechanismKind enum: Route / Filter / RateLimit
│   │   └── AwaitingRouting.cs        # Tag: packet has arrived at a mechanism, awaiting selection
│   ├── Buffers/
│   │   └── MechanismConnections.cs   # Outbound edges this mechanism can select from — mechanisms only
│   └── Systems/
│       └── MechanismSystem.cs        # Reads MechanismType, selects outbound edge, removes AwaitingRouting
└── Rendering/
    ├── PacketVisualizer.cs           # Hybrid renderer — lerps packets between node positions
    └── EdgeVisualizer.cs             # Renders every edge as a LineRenderer; internal edges tinted
```

**Rules for new domains:** If a concept needs more than one component or its own system, it gets a domain folder. Shared utilities live in a top-level `Shared/` folder.

---

## Known Future Work

Do not work around these — implement them when their milestone arrives.

- **Recipe matcher** — structural pattern recognition surfacing a type label from assembled node graph. Phase 2.
- **Node dragging** — `EdgeVisualizer` and `PacketVisualizer` already support this (positions read every frame). No renderer changes needed.
- **Mechanism rendering** — mechanisms currently have no visual representation. Need a visual shape (bead, gate, knot) at their `NodeTransform` position. Milestone 4.
- **`EntryNodes` (plural)** — `SpawnResult.EntryNode` is currently singular. Will need to become an array when merge nodes are introduced.
- **Filter and RateLimit mechanisms** — dispatch structure is in place in `MechanismSystem`; both are stubs. Implement in Phase 4.
- **`WaitingAtNode` / `AwaitingRouting` performance** — tag-based skipping may need to move to a dedicated waiting queue at 100k+ packets. Profile before optimizing.

---

## What Not To Do (Common AI Mistakes)

| Anti-Pattern                                   | Why It's Wrong                                | Correct Alternative                                                |
| ---------------------------------------------- | --------------------------------------------- | ------------------------------------------------------------------ |
| `yield return new WaitForSeconds(latency)`     | Hidden timer, not spatial                     | Make the edge longer                                               |
| `packetState = Processing; timer -= dt;`       | Abstract state machine                        | Route packet through internal node graph                           |
| Lerp with a fixed duration                     | Duration is a timer                           | Lerp with a fixed speed; distance determines time                  |
| `if (isProcessing) skip` flags                 | Invisible logic                               | Capacity constraint on the edge                                    |
| Separate "queue list" data structure           | Logic-based queue                             | Packets physically waiting at edge entry                           |
| `Part` component or slot-type enums            | Redundant abstraction                         | Nodes are nodes at every level of nesting                          |
| Declaring node type at spawn time              | Type is recognized, not declared              | Recipe matcher reads structure, surfaces label                     |
| Routing logic on the `Packet` component        | Packets are dumb — they don't decide          | Mechanism entity reads destination, selects edge                   |
| Routing logic on the `Node` component          | Nodes are places — they don't decide          | Mechanism entity sits on the path between nodes                    |
| `switch(mechanismType)` mega-system            | Monolithic hidden logic                       | Dispatch structure with one case per type                          |
| `MechanismConnections` on Node entities        | Nodes don't route — mechanisms do             | Buffer belongs exclusively on Mechanism entities                   |
| Dedicated Intake / Egress node types           | Redundant — entry/exit are structural         | First child is entry, last group is exit                           |
| Plain node with multiple outbound edges        | Misconfiguration — nothing selects            | Add a Route mechanism before the fan-out                           |
| `EnsureWebServer` / validator methods          | Hardcodes recipes in C#                       | Recipes are data assets — no C# guardian needed                    |
| Restoring occupancy on failed forward          | Causes thrashing every frame                  | Stamp `WaitingAtNode`, retry next frame                            |
| Collector mechanism at end of recipe           | Bottleneck — collapses parallel lanes         | End recipe with ProcessingLane — lanes exit independently          |
| Inter-node mechanism as global waypoint        | Creates false spatial convergence             | Wire exit lanes directly to destination entry node                 |
| Adding inbound edges to `MechanismConnections` | Causes packet to loop back onto inbound edge  | Connections are outbound-only — inbound edges need no registration |
| Stacking multiple policies on one mechanism    | Hides complexity, breaks observability        | Chain two mechanism entities with an edge between them             |
| "Node is just a visual grouping"               | Nodes enforce traversal — they are not chrome | Node is a containment boundary with simulation weight              |

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

This roadmap describes intended scope per phase. It is not a task tracker.

### Phase 1 — Sandbox

**Goal:** A working physics engine for distributed systems. Prove the spatial metaphor holds.

**Capabilities:** Place and remove nodes freely in 2D space. Connect nodes with edges; configure edge length and capacity. Observe live packet flow, congestion, and routing in real time. Pause, step, and resume the simulation. Adjust capacity and speed mid-simulation. Internal node physics: packets traverse a subgraph inside each node, producing emergent compute time and queue formation.

**Definition of done:** A user with no context can open Loom, build a triangle topology, and observe congestion without reading any documentation.

### Phase 2 — Vocabulary

**Goal:** Give the simulation domain knowledge. Nodes and edges become typed; Loom can represent real-world architectures.

**Capabilities:** Typed nodes with distinct visual identities (web server, database, load balancer, cache, message queue, CDN, API gateway). Typed edges with protocol semantics (HTTP, TCP, UDP, WebSocket). Packet typing (read requests, write requests, health checks, cache hits). Recipe matching surfaces type labels organically. Architecture templates as importable starting points.

**Definition of done:** A user can assemble a recognizable labeled architecture (e.g. "Nginx → App Server → Postgres") and have it behave differently from an unlabeled graph of the same shape.

### Phase 3 — Scenarios

**Goal:** Structured learning on top of the sandbox. Loom becomes a teaching tool.

**Capabilities:** Scenario mode with guided objectives. Curriculum progression from single-server to distributed. Annotated architectures. Comparison mode (two topologies side-by-side). Challenge mode (design to meet target metrics).

**Definition of done:** A user with no networking background can complete a structured scenario path from a single web server to a multi-region load-balanced system.

### Phase 4 — Simulation Depth

**Goal:** Add consequence. The simulation now models cost, failure, and security.

**Capabilities:** Cost model (per-node, per-edge, per-packet). Failure simulation (stochastic or on-demand). Redundancy mechanics (replication, failover, health-check routing). Security layer (hostile packets, firewalls, rate-limiters). Metrics dashboard derived from observed packet behavior. Budget constraints in scenario mode.

**Definition of done:** A user can observe a cascading failure, diagnose it by watching packet behavior, and redesign the architecture to survive it — within a cost budget.

### Phase 5 — Platform

**Goal:** Loom becomes a shared tool. Users can save, publish, and build on each other's architectures.

**Capabilities:** Save/load architecture files. Export as shareable blueprints. Blueprint library (community-contributed, tagged by domain). Diff and versioning. API/headless mode for programmatic simulation.

**Definition of done:** A user can publish an architecture blueprint and another user can import and extend it without direct coordination.

---

## Philosophy

> Simulation first, visualization second.
> The simulation is never explained — it is observed.
> All complexity must emerge from structure, not logic.
