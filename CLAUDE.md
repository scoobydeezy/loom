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
| Mechanism    | A physical actor on the path — intercepts packets and applies a rule    |
| Packet       | A unit of work — a moving entity traversing edges                       |
| Throughput   | The capacity of an edge (max concurrent packets)                        |
| Compute time | Distance traveled inside a node's internal graph                        |
| Routing      | A mechanism reading packet destination and selecting an outbound edge   |
| Queuing      | Packets waiting at a saturated edge entry point                         |

---

## Architecture

### Three Primitives

Everything in Loom is built from three primitives:

| Primitive     | What it is                                       | Role                                            |
| ------------- | ------------------------------------------------ | ----------------------------------------------- |
| **Node**      | A place — a position in space                    | Compute locations, clusters, any named thing    |
| **Edge**      | A path — connects two entities                   | Latency, throughput, the strings packets travel |
| **Mechanism** | An actor — intercepts packets and applies a rule | Routing, filtering, rate-limiting, cache lookup |

No other structural concepts exist. A load balancer is nodes and edges with a routing mechanism. A firewall is a mechanism that drops hostile packets. A cache is a mechanism that short-circuits processing on a hit. All complexity emerges from how these three primitives are composed.

### Recursive Simulation Model

The architecture is recursive. Nodes, edges, and mechanisms compose into graphs. Those graphs can be nested inside nodes. The same traversal logic applies at every level.

```
Topology
    └── Node → Edge → Mechanism → Edge → Node
                              └── (Mechanism can contain its own internal graph)
```

A node's internal structure is a graph of child nodes, edges, and mechanisms:

```
Entry → [Mechanism] → Node → [Mechanism] → Node → [Mechanism] → Egress
```

Those inner nodes can themselves contain graphs of the same three primitives. Nesting is unbounded. A "database query processor" isn't a black box with a long path length — it's a parsing mechanism feeding into an index lookup node feeding into a result assembly node, each with their own mechanisms.

There is no special "Part" type. A processing lane, a node interior, a cluster, and a pod are all the same thing at different scales: nodes and edges with mechanisms at the decision points.

**Global Graph**

```
Node → [edges] → Node
```

- Represents the distributed system topology
- Exit lanes wire directly to the destination node's entry point — no inter-node mechanisms
- A node's entry point is its first child (mechanism or plain node)
- Multiple inbound edges arriving at an entry mechanism is correct and expected
- Mechanisms only exist _inside_ nodes as part of their internal recipe

**Internal Node Space**

```
Entry → [Mechanism] → [child nodes + edges + mechanisms] → Exit
```

- Each Node is itself a graph of Nodes, Edges, and Mechanisms
- Compute time = distance traversed inside the node
- Queues emerge from structural congestion, not logic
- Routing decisions emerge from mechanisms, not from packet intelligence

### Exit Points

Most nodes have a single exit point. Nodes with multiple exit children (e.g. a load balancer with 8 processing lanes) have **multiple exit points** — one per last-group child. `SpawnResult.ExitNodes` is an array. The bootstrap loops over all exit nodes when wiring global edges. In the world builder, each exit point will wire to a distinct downstream node.

A plain node with multiple outbound edges and no mechanism is a misconfiguration. `PacketTraverseSystem` only auto-forwards plain nodes with exactly one outbound edge. Multiple outbound edges require a mechanism to make the selection — packets will visibly pile up if this rule is violated, which is the correct observable consequence.

### System Layers (in dependency order)

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
Components:        Node, NodeTransform, NodeType, NodeParent, Edge, Mechanism, MechanismType, Packet, PacketDestination, WaitingAtNode, QueueState, EdgeCapacity
Buffers:           MechanismConnections
Tags:              AwaitingRouting, WaitingAtNode, NodeActiveTag, EdgeSaturatedTag
Systems:           PacketTraverseSystem, MechanismSystem
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
- Routing is not hardcoded — it emerges from mechanisms reading packet destinations and edge congestion.

### Mechanism Rules

- A mechanism is a first-class entity — not a component on a node or edge, but its own thing in the world.
- Mechanisms live **inside nodes** as part of their internal recipe. They are never placed between nodes at the global topology level.
- A packet arriving at a mechanism is stamped `AwaitingRouting`. `MechanismSystem` reads `MechanismType`, selects from `MechanismConnections`, assigns the next edge.
- `MechanismConnections` is an outbound-only buffer. It contains the edges the mechanism can send packets onto. Inbound edges are never registered — a mechanism can have any number of inbound edges.
- **Packets are dumb.** They carry a destination and properties. They make no decisions. All intelligence lives in mechanisms.
- A "router" is not a special concept — it is a node whose internal structure is primarily mechanisms.

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

For `ChildType.Node` children, `outEdgeLength` and `outEdgeCapacity` are read from the child's `NodeTypeDefinition` (`internalPathLength` and `edgeCapacity`). For `ChildType.Mechanism` children, `outEdgeLength` and `outEdgeCapacity` are set directly on the `NodeTypeChild`.

### Leaf vs. Composite Nodes

- **Leaf nodes** have no children. Their behavior is defined by `edgeCapacity` and `internalPathLength`.
- **Composite nodes** have children. Behavior emerges from the graph those children form.

### Entry and Exit Points

- **Entry point** = first child in the recipe, whatever type it is
- **Exit points** = all entities in the last child group
- No dedicated Intake or Egress node types exist — these were removed as redundant. The first child _is_ the entry. The last group _is_ the exit.
- A plain node with a single outbound edge is auto-forwarded by `PacketTraverseSystem`
- A mechanism as the first child is auto-wired as the entry point and handles distribution immediately

### Traversal Rules

- **Plain node, 1 outbound edge:** `PacketTraverseSystem` forwards directly — no mechanism needed
- **Plain node, 0 outbound edges:** packet waits — correct backpressure behavior
- **Plain node, >1 outbound edges:** packets pile up — this is a misconfiguration, observable as congestion
- **Mechanism:** `PacketTraverseSystem` stamps `AwaitingRouting`, `MechanismSystem` selects outbound edge from `MechanismConnections` buffer

### Built-in Node Type Assets

| Asset            | Type      | Recipe                        | Notes                                   |
| ---------------- | --------- | ----------------------------- | --------------------------------------- |
| `ProcessingLane` | Leaf      | —                             | edgeCapacity: 1, pathLength: 3.0        |
| `WebServer`      | Composite | Route x1 → ProcessingLane x16 | Distributes across 16 independent lanes |
| `Database`       | Composite | Route x1 → ProcessingLane x4  | Distributes across 4 independent lanes  |
| `LoadBalancer`   | Composite | Route x1 → ProcessingLane x8  | Distributes across 8 independent lanes  |
| `Cache`          | Composite | ProcessingLane x1             | Single lane, no routing needed          |

All composite nodes end with plain ProcessingLane nodes as exit points. No collector mechanism. Each lane exits independently and wires directly to the destination node's entry point. `Intake.asset` and `Egress.asset` have been deleted — entry and exit are structural positions, not named types.

### Type Recognition (Future)

Node type is not declared at runtime — it is **recognized**. A recipe matcher compares assembled node structure against `NodeTypeDefinition` patterns and surfaces a label when there is a match. This is a UI/presentation concern only. The simulation has no concept of type. Belongs in Phase 2.

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
│   └── NodeTypes/                    # ScriptableObject recipe assets
│       ├── NodeTypeDefinition.cs     # Recipe class — ChildType enum, NodeTypeChild struct
│       ├── ProcessingLane.asset      # Leaf — edgeCapacity:1, pathLength:3.0
│       ├── WebServer.asset           # Route x1 → ProcessingLane x16 → Route x1
│       ├── Database.asset            # Route x1 → ProcessingLane x4 → Route x1
│       ├── LoadBalancer.asset        # Route x1 → ProcessingLane x8
│       └── Cache.asset               # ProcessingLane x1
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

**Rules for new domains:** If a concept needs more than one component or its own system, it gets a domain folder. Shared utilities that serve multiple domains live in a top-level `Shared/` folder.

---

## Known Future Work

These are architectural decisions made but not yet implemented. Do not work around them — implement them when their milestone arrives.

- **Recipe matcher:** Structural pattern recognition that compares assembled node graphs against `NodeTypeDefinition` recipes and surfaces a type label. Belongs in Phase 2.
- **Node dragging:** Nodes will be draggable mid-simulation. `EdgeVisualizer` and `PacketVisualizer` already support this — they read positions every frame. No changes needed to the renderers when this is implemented.
- **Mechanism rendering:** Mechanisms currently have no distinct visual representation — they are invisible points on edges. They need a visual shape (a bead, gate, or knot on the string) at their `NodeTransform` position. Belongs in Milestone 4.
- **`EntryNodes` (plural):** Currently `SpawnResult.EntryNode` is singular. Most node types have a single entry point, but a future node type with multiple entry points (e.g. a merge node) will need this to become an array. Not needed for current node types.
- **Filter and RateLimit mechanisms:** `MechanismSystem` has the dispatch structure in place but Filter and RateLimit are stubs. Implement when hostile packets and rate limiting are introduced in Phase 4.
- **`WaitingAtNode` and `AwaitingRouting` performance:** Both tags cause packets to be skipped each frame via query filters. At 100k+ packets this may need to move to a dedicated waiting queue structure. Profile before optimizing.

---

## What Not To Do (Common AI Mistakes to Avoid)

These patterns are tempting but violate Loom's architecture. Reject them if suggested:

| Anti-Pattern                                   | Why It's Wrong                               | Correct Alternative                                                |
| ---------------------------------------------- | -------------------------------------------- | ------------------------------------------------------------------ |
| `yield return new WaitForSeconds(latency)`     | Hidden timer, not spatial                    | Make the edge longer                                               |
| `packetState = Processing; timer -= dt;`       | Abstract state machine                       | Route packet through internal node graph                           |
| Lerp with a fixed duration                     | Duration is a timer                          | Lerp with a fixed speed; distance determines time                  |
| `if (isProcessing) skip` flags                 | Invisible logic                              | Capacity constraint on the edge                                    |
| Separate "queue list" data structure           | Logic-based queue                            | Packets physically waiting at edge entry                           |
| `Part` component or slot-type enums            | Redundant abstraction                        | Nodes are nodes at every level of nesting                          |
| Declaring node type at spawn time              | Type is recognized, not declared             | Recipe matcher reads structure, surfaces label                     |
| Routing logic on the `Packet` component        | Packets are dumb — they don't decide         | Mechanism entity reads destination, selects edge                   |
| Routing logic on the `Node` component          | Nodes are places — they don't decide         | Mechanism entity sits on the path between nodes                    |
| `switch(mechanismType)` mega-system            | Monolithic hidden logic                      | Dispatch structure with one case per type                          |
| `MechanismConnections` on Node entities        | Nodes don't route — mechanisms do            | Buffer belongs exclusively on Mechanism entities                   |
| Dedicated Intake / Egress node types           | Redundant — entry/exit are structural        | First child is entry, last group is exit                           |
| Plain node with multiple outbound edges        | Misconfiguration — nothing selects           | Add a Route mechanism before the fan-out                           |
| `EnsureWebServer` / validator methods          | Hardcodes recipes in C#                      | Recipes are data assets — no C# guardian needed                    |
| Restoring occupancy on failed forward          | Causes thrashing every frame                 | Stamp `WaitingAtNode`, retry next frame                            |
| Collector mechanism at end of recipe           | Bottleneck — collapses parallel lanes        | End recipe with ProcessingLane — lanes exit independently          |
| Inter-node mechanism as global waypoint        | Creates false spatial convergence            | Wire exit lanes directly to destination entry node                 |
| Adding inbound edges to `MechanismConnections` | Causes packet to loop back onto inbound edge | Connections are outbound-only — inbound edges need no registration |

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
