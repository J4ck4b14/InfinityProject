# Infinity

Infinity is a work-in-progress systemic world simulation built in Unity 6. The project is centered on one rule: higher-level behaviour should come from lower-level world state instead of being faked by isolated random systems.

The current simulation reaches from terrain generation to a persistent ecological loop with living vegetation and mobile deer populations.

## Current simulation chain

```text
Geography
├── Hydrology
└── Climate
      ↓
Ground conditions
      ↓
Flora potential
      ↓
Living flora
      ↓
Fauna
      └── grazing feeds back into living flora
```

Presentation is kept separate from simulation state. Unity Terrain, vegetation instances, river meshes and animal prefabs visualize the world; they do not define it.

## Current status

| System | Status | Notes |
| --- | --- | --- |
| Geography | Working | Deterministic terrain generation in physical metres. |
| Hydrology | Working | Priority-Flood conditioning, routing, contributing area, basins and channel candidates. |
| Climate | Working | Static temperature and precipitation context. |
| Ground | Working | Run-on, water availability and soil-retention response. |
| Flora potential | Working | Species suitability and carrying capacity from environmental constraints. |
| Living flora | Working | Persistent biomass, growth, dieback, disturbance and recovery. |
| Fauna | In active development | Deer movement is ECS-owned; feeding and physiology still use the managed simulation path. |
| Time | Working | Simulation time is independent from world/calendar time. |
| World presentation | Experimental | Terrain shading, instanced flora, river ribbons and procedural deer posing are still being refined. |
| People / society | Planned | Not part of the current implementation. |

## Core ideas

### Causality before decoration

The project tries to preserve a readable chain of cause and effect. Water availability comes from climate and hydrology; flora carrying capacity comes from environmental conditions; animals consume actual living biomass; grazing changes the biomass available later.

### Physical units

World dimensions, slope-analysis radii, movement distances and ecological quantities are expressed in physical units where practical. Resolution controls sampling density rather than redefining the scale of the world.

### Deterministic generation

Generated world layers use explicit seeds and settings so the same configuration can be reproduced and compared across resolutions.

### Simulation and presentation are separate

Agent state lives in the simulation. Prefabs interpolate and pose themselves from that state. Vegetation rendering samples living-flora density instead of spawning a GameObject for every plant.

## World systems

### Geography

Geography generates elevation from seeded macro landforms, mountain structure, domain warping and local detail. The generated elevation field is persisted in `Assets/InfinityGenerated/Geography/` and presented through Unity Terrain.

### Hydrology

Hydrology derives flow routing from geography without rewriting the source terrain. The current implementation includes:

- Priority-Flood depression conditioning;
- flow receivers and direction;
- physical contributing area;
- outlet basins;
- raw depression diagnostics;
- channel candidates and conditioned transit.

Large derived rasters are cached under `Library/InfinityHydrologyCache/` and are intentionally not committed.

### Climate and ground

Climate currently provides static temperature and precipitation context. Ground combines terrain, hydrology and climate into ecological inputs such as run-on, water availability and soil-retention potential.

### Flora

Static flora evaluates species-specific environmental suitability and carrying capacity. The current diagnostic species are:

- Temperate Grass;
- Riparian Shrub;
- Cold-Tolerant Conifer.

Living flora keeps mutable biomass state over time, including growth, competition, dieback, disturbance and recovery.

### Fauna

The current fauna model simulates deer herds with:

- deterministic spawning;
- terrain-constrained movement;
- forage perception;
- herd cohesion;
- physical biomass consumption;
- hunger, energy and health;
- starvation and death.

Movement runs through Unity ECS/Burst-backed systems. Feeding and physiology are still being migrated from the managed reference implementation.

## Time model

Infinity uses two related clocks:

- **Simulation Clock** controls physical progression: movement, metabolism, ecology and presentation speed.
- **World Clock** maps simulation time to time-of-day using a configurable world-day duration.

Changing the length of a world day changes schedules and calendar time without changing the physical speed of an animal or its procedural gait.

## Procedural presentation

The doe rig is driven procedurally rather than through a library of baked clips. The presentation layer currently derives pose state from simulation data such as movement, heading, forage state and terrain contact.

This layer is still experimental. Planned improvements include better gait generation, hoof placement/IK, body grounding and richer behavioural poses.

Flora is rendered with GPU instancing and bounded sampling from living biomass. The simulation can represent far more vegetation than is drawn directly.

## Editor tools

The main workflow is exposed through Scene View overlays:

- **Terrain Tool**
- **Hydrology Tool**
- **Climate Tool**
- **Ground Conditions Tool**
- **Flora Tool**
- **Ecology Lab**
- **World Data Debug**

`World Data Debug` projects generated data onto the terrain without changing simulation state, making it possible to inspect height, slope, drainage, climate, ground conditions and flora diagnostics in place.

The fauna tools also include a runtime/debug inspector and a procedural animation-state view.

## Observer controls

When Play Mode starts on the configured simulation scene, the main camera gets the observer controller automatically.

| Input | Action |
| --- | --- |
| `WASD` | Move |
| `Q / E` | Down / up |
| Right mouse | Look |
| `Shift` | Fast movement |
| `Ctrl` | Slow movement |
| Mouse wheel | Adjust movement speed |

## Getting started

### Requirements

- Unity **6000.3.5f2**
- Entities **1.4.2**
- Entities Graphics **1.4.15**
- Burst **1.8.27**
- Universal Render Pipeline **17.3.0**

### Open the project

1. Clone the repository.
2. Open it with Unity `6000.3.5f2`.
3. Open `Assets/Scenes/Infinity.unity`.
4. Enable the Scene View overlays listed above as needed.

Heavy derived data is stored in `Library/` caches and is not versioned. On a fresh clone, regenerate any missing data in dependency order:

```text
Geography
→ Hydrology + Climate
→ Ground
→ Flora potential
→ Living flora
→ Fauna
```

The editor tools report missing or stale cache data instead of treating it as simulation truth.

## Tests

The repository currently contains **80 EditMode test cases** covering:

- geography;
- hydrology;
- climate;
- ground conditions;
- static flora;
- living flora;
- fauna;
- ECS state/movement;
- simulation/world time.

Run them through Unity Test Runner before changing simulation semantics.

## Project layout

```text
Assets/
├── Editor/             # world-building and debug tools
├── FBX/                # source meshes
├── InfinityGenerated/  # persistent generated metadata/sample world state
├── Materials/
├── Prefabs/
├── Scenes/
├── Scripts/
│   └── World/
│       ├── Climate/
│       ├── Ecology/
│       ├── Fauna/
│       ├── Flora/
│       ├── Geography/
│       ├── Ground/
│       ├── Hydrology/
│       ├── Observation/
│       ├── Presentation/
│       └── Time/
├── Settings/
├── Shaders/
├── Tests/
└── Textures/
```

## Known limitations

This is a research/development build, not a finished game. In particular:

- animal lifecycle, reproduction and predation are not implemented in the current fauna path;
- feeding and physiology have not yet been fully migrated to ECS;
- procedural animal animation is early and does not yet have final hoof IK;
- visible rivers are a presentation of hydrology rather than carved riverbeds;
- lakes and larger standing-water bodies are not yet represented by the current world presenter;
- vegetation presentation is intentionally approximate and optimized independently from biomass state;
- people, settlements and social systems are still future work.

## Direction

The long-term goal is a reusable simulation framework where the same underlying world can support different kinds of games without replacing its causal model. The next major areas are fauna lifecycle/predation, stronger procedural presentation and, later, human agents and social organization.
