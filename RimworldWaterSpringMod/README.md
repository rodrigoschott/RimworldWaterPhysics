<div align="center">

# RimWorld — Water Physics (Water Spring Mod)

Natural water flow in RimWorld with a performant, moddable diffusion system, a buildable Water Spring, and an always-on debug overlay.

</div>

## TL;DR
- Build a Water Spring to generate Flowing Water tiles that diffuse to neighbors.
- Uses an active-tile diffusion system with stability rules and safeguards against oscillation.
- Low overhead with pooling, frequency gating, and optional chunk-based batching.
- In-game debug overlay (Alt+W): shows active tiles, chunk bounds (optional), and per-cell volumes.

---

## For Players

### Install
1) Place the `RimworldWaterSpringMod` folder into your RimWorld Mods directory, or subscribe via Steam Workshop if available.
2) Enable the mod in RimWorld’s Mod Manager (place after Harmony).

Requirements: Harmony 2.x, RimWorld 1.6 (targeted via Krafs refs 1.6.4566).

### How to use in-game
1) Build a Water Spring (found under Architect once research/buildables are visible).
2) The spring spawns Flowing Water at intervals; water diffuses across walkable terrain.
3) Each tile holds up to 7 units. Tiles stabilize when they stop changing (see Stability below).

### Debug visuals (optional)
- Toggle overlay: Alt+W (Dev Mode recommended).
- You’ll see:
	- White field-edges over active cells (always visible while enabled).
	- Yellow numbers (tile volumes), capped at 256 labels per frame to protect performance.
	- Green chunk outlines if chunk-based processing is enabled.

### Settings (Gameplay and performance)
Open Settings > Mod Settings > Water Physics. Highlights:
- Spawn interval for springs.
- Active Tile Management: stability cap, processed tiles per tick.
- Diffusion rule: minimum volume difference for transfers (prevents ping-pong).
- Anti-backflow (optional): discourage immediate reverse flow after an outbound transfer.
- Spring backlog (optional): buffer production when the source tile is full.
- Reactivation wave: radius, immediate transfers on wake.
- Chunk-based batching (optional) and checkerboard stepping.
- Frequency gate and adaptive TPS throttle (optional).
- Debug & visualization toggles.

---

## For Developers

### Repository layout
- About/ … RimWorld metadata
- Assemblies/ … built DLLs
- Defs/ … Thing/Thought defs for the spring and water tile
- Languages/ … English text
- Source/WaterSpringMod/ … solution and code
	- WaterSpring/ … main implementation
	- WaterSpring/Legacy/ … legacy multi-phase manager (not compiled)
	- Textures/ … simple sprites for spring and water

### Build
- Prereqs: .NET SDK (builds with latest C#), Krafs.Rimworld.Ref 1.6.4566, Harmony 2.2.2 (private assets).
- Solution: `Source/WaterSpringMod/WaterSpringMod.sln`.
- Output: `RimworldWaterSpringMod/Assemblies/WaterSpringMod.dll`.

Optional (PowerShell):

```powershell
dotnet build .\RimworldWaterSpringMod\Source\WaterSpringMod\WaterSpringMod.sln -c Release
```

### Hotkeys and dev tools
- Alt+W: toggle active water overlay (status label shows active count).
- Alt+P (Dev builds only): spawn a small cluster of springs near the mouse (PerfHarness).

### Coding conventions and symbols
- Debug symbols (Debug only): WATERPHYSICS_DEV, WATERPHYSICS_VERBOSE.
- Logger: use `WaterSpringLogger.DebugEnabled` to guard hot-path logs.
- Avoid allocations in ticks/OnGUI; reuse scratch buffers.

---

## Technical Manual

### Architecture overview

- FlowingWater (Thing):
	- Holds `Volume` [0..7], stability counter, simple diffusion mechanics.
	- Spreads to neighbors when volume allows; respects min-diff transfer rule.
	- Prevents equal-volume oscillation; optional anti-backflow cooldown.
	- Inspector string is clean (no empty lines).

- Building_WaterSpring (Thing):
	- Periodically spawns or increments FlowingWater on its own tile.
	- Optional backlog buffer when the tile is full; injects back into the map later.

- GameComponent_WaterDiffusion:
	- Central registry of active water tiles per map.
	- Optional chunk-based indexing and spatial index for faster lookups.
	- Processes active tiles within configured per-tick limits.
	- Stability rule: tiles become “stable” (explicitly deregistered) only after reaching the stability cap of consecutive no-change attempts.
	- Reactivation systems:
		- Neighbor activation upon change.
		- Terrain-change handling (BFS wave with bounds).
		- Radius-based wake-up with optional immediate one-step transfer.
	- OnGUI debug overlay (Repaint-only) with pooled buffers:
		- Field edges for actives, chunk outlines (optional), volume labels (capped), and overlay status label.

Notes
- Multi-phase manager is kept under `WaterSpring/Legacy` and excluded from build.
- Overlay is drawn from GameComponent, not a MapComponent, to guarantee visibility.

### Diffusion rules (summary)
- Max tile capacity: 7.
- Transfers are 1 unit by default.
- A transfer requires a minimum volume difference (configurable) to avoid ping-pong.
- Preference order:
	1) Expand into empty cells if volume >= 2 (create new FlowingWater).
	2) Otherwise transfer to the lowest-volume neighbor meeting the min-diff rule.
- Anti-backflow (optional): after an outbound transfer, immediate backflow to the sender requires an extra difference for a short cooldown.

### Stability and performance
- Stability: a tile is marked stable only after `stabilityCounter >= stabilityCap` with repeated no-change attempts.
- Active tiles are randomly sampled each tick up to `maxProcessedTilesPerTick`.
- Optional chunk-based processing (size N): per-tick caps on chunks and per-chunk tiles; optional checkerboard stepping.
- Optional frequency gate (global N-tick step) and adaptive TPS throttling.
- Pooled buffers and zero-alloc OnGUI paths.

### Debug overlay details
- Toggle: Alt+W; shows a label like `[WaterPhysics] Active tiles: N (overlay ON)`.
- Visuals:
	- Field edges: white outline of active cells.
	- Volumes: yellow tiny labels, capped at 256 per frame; screen-space fallback ensures visibility at any zoom.
	- Fallback flashes: subtle debug flashes each frame for visibility (debug drawer).
	- Chunk outlines: green rectangles when chunk mode is enabled.

### Settings reference (key items)

General
- `waterSpringSpawnInterval`: ticks between spring spawns.
- `debugModeEnabled`: enables logging; cached into `WaterSpringLogger.DebugEnabled`.

Active Tile Management
- `useActiveTileSystem`: enable the active tile registry and processing.
- `stabilityCap`: attempts with no change before a tile stabilizes.
- `maxProcessedTilesPerTick`: per-tick limit for processed tiles.
- `localCheckIntervalMin/Max`: randomized per-tile delay between checks.

Diffusion & Flow Control
- `minVolumeDifferenceForTransfer`: min difference required to transfer.
- `antiBackflowEnabled`, `backflowCooldownTicks`, `backflowMinDiffBonus`.

Spring Behavior
- `springUseBacklog`, `springBacklogCap`, `springBacklogInjectInterval`.
- `springPrioritizeTiles`, `springNeverStabilize`.

Reactivation Wave
- `reactivationRadius`, `reactivationMaxTiles`, `reactivationImmediateTransfers`.

Chunk-based Processing (optional)
- `useChunkBasedProcessing`, `chunkSize`, `maxProcessedChunksPerTick`, `maxProcessedTilesPerChunk`, `useCheckerboardPattern`.

Update Frequency (optional)
- `useFrequencyBasedProcessing`, `globalUpdateFrequency`, `useAdaptiveTPS`, `minTPS`.

Debug & Visualization
- `showPerformanceStats`, `showDetailedDebug` (e.g., stable tiles in blue).

All settings are range-clamped on load to safe bounds.

### Public surfaces and hooks (lightweight)
- `GameComponent_WaterDiffusion`:
	- `GetActiveWaterTiles(Map)` — HashSet of active tiles.
	- `RegisterActiveTile(Map, IntVec3)` / `UnregisterActiveTile(Map, IntVec3)`.
	- `NotifyTerrainChanged(Map, IntVec3)` — wake neighbors and run a bounded BFS.
	- `ReactivateInRadius(Map, IntVec3)` — bounded radius wake + optional immediate transfer.
	- `GetWaterAt(Map, IntVec3)` — lookup via spatial index or ThingGrid.

---

## Compatibility
- RimWorld 1.6 (compiled against Krafs 1.6.4566).
- Harmony 2.x required.
- Load order: after Harmony; otherwise flexible.

## Troubleshooting
- Overlay not visible: press Alt+W. Look for the status label `[WaterPhysics] Active tiles: ...`.
- Active tiles = 0: ensure water is present and `useActiveTileSystem` is enabled.
- Performance issues: reduce `maxProcessedTilesPerTick`, enable frequency gating or chunk-based processing, turn off overlay and detailed debug.

## Credits
Inspired by diffusion mechanics from classic simulation games. Built with Harmony and Krafs RimWorld references.
