# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build (from repo root)
dotnet build RimworldWaterSpringMod/Source/WaterSpringMod/WaterSpringMod.csproj -c Debug
dotnet build RimworldWaterSpringMod/Source/WaterSpringMod/WaterSpringMod.csproj -c Release

# Output goes to: RimworldWaterSpringMod/Assemblies/WaterSpringMod.dll
```

- **Target**: .NET Framework 4.8 (`net48`)
- **Dependencies**: `Krafs.Rimworld.Ref 1.6.4566` (RimWorld API), `Lib.Harmony 2.2.2`
- **MultiFloors**: Compile-time reference (`Reference/1.6/Assemblies/MultiFloors.dll`), `Private=false` — not copied to output
- **RimWorld Decompiled Source**: `Reference/1.6/RimWorldDecompiled/` — full decompiled RimWorld 1.6 C# source (namespaces: `RimWorld/`, `Verse/`)
- **Debug defines**: `WATERPHYSICS_VERBOSE`, `WATERPHYSICS_DEV`

## RimWorld Source Reference (IMPORTANT)

**Always consult the decompiled RimWorld 1.6 source** at `RimworldWaterSpringMod/Reference/1.6/RimWorldDecompiled/` before:
- Planning new features or writing specs — verify vanilla API signatures, default values, and behavior
- Using vanilla comps, buildings, or systems — read the actual implementation, don't guess from memory
- Debugging integration issues — check exact method signatures, property access, and default field values

Key directories:
- `RimWorld/` — Game logic (comps, buildings, jobs, AI, etc.)
- `Verse/` — Engine layer (Things, Maps, defs, XML loading, etc.)

Example: Before using `CompFacility`, read `RimWorld/CompFacility.cs` and `RimWorld/CompProperties_Facility.cs` to see that `requiresLOS` defaults to `true` and `linkableBuildings` is auto-populated via `ResolveReferences`.

## Architecture

### Water Physics Model

Cellular automata diffusion inspired by Dwarf Fortress. Each `FlowingWater` Thing holds `Volume` 0-7 and self-diffuses to cardinal neighbors + vertical portals.

**Core flow per tick:**
1. `GameComponent_WaterDiffusion.GameComponentTick()` — iterates active tile registry
2. For each active tile, calls `FlowingWater.AttemptLocalDiffusion()` when its local timer expires
3. **Gravity-first**: If tile is on a hole, bulk-transfer ALL possible volume downward immediately (keep 1). Early return.
4. **Pressure BFS**: If volume == 7 and all cardinal neighbors are also 7, BFS through connected full tiles to find nearest non-full outlet. Teleports 1 unit per event. Depth-limited (32), cooldown-gated (10 ticks), same-map only. See `PressurePropagation.cs`.
5. **Horizontal scan**: 4 cardinal neighbors + vertical portals (stairs, elevators, holes) using 12-element arrays
6. **Multi-neighbor diffusion**: Transfer to ALL eligible neighbors per tick (not just one). Each uses equilibrium math: `transferAmount = diff / 2`. Diff=1 means equilibrium, no transfer. Volume recalculated after each transfer (self-limiting).
7. **Expansion**: Spawn to ONE random empty neighbor per tick (spawn is expensive), giving half volume.

### Key Classes (all in `Source/WaterSpringMod/WaterSpring/`)

| Class | Role |
|-------|------|
| `FlowingWater` | Water tile Thing. Volume 0-7, stability counter, gravity-first/pressure/multi-neighbor diffusion, evaporation, terrain sync |
| `PressurePropagation` | Static BFS utility: traces through connected 7/7 tiles to find nearest outlet. Depth-limited, cooldown-gated |
| `GameComponent_WaterDiffusion` | Central tick manager. Active tile registry (per-map `HashSet<IntVec3>`), tile-based or chunk-based processing, debug overlay (Alt+W), reactivation waves |
| `Building_WaterSpring` | Spawns water units at configurable interval. Optional backlog buffer |
| `MultiFloorsIntegration` | Direct MF API access via Prepatcher fields. Void terrain detection, stair/elevator water flow, cross-level activation |
| `VerticalPortalBridge` | Fallback reflection-based cross-level detection. `IsHoleAt()` checks MF void terrain then `WS_Hole` building |
| `ChunkBasedSpatialIndex` | `Dictionary<ChunkCoordinate, HashSet<IntVec3>>` spatial lookup for water tiles |
| `WaterSpringModSettings` | All mod settings with `ClampAndSanitize()` validation |
| `HarmonyPatches` | Postfix on `Thing.SpawnSetup`, prefix on `Thing.DeSpawn` — reactivates water when buildings change |

### MultiFloors Integration (Critical)

The mod is tightly coupled with MultiFloors. The integration uses **Mono lazy JIT** safety: MF types are compile-time referenced but only JIT-compiled when methods containing them are actually called. All MF code paths are guarded behind `MultiFloorsIntegration.IsAvailable`.

**Key MF types used (decompiled source in `MultiFloors_Decompiled/`):**

| MF Type | What We Use |
|---------|-------------|
| `PrepatcherFields` | Extension methods: `map.Level()`, `map.UpperMap()`, `map.LowerMap()`, `map.GroundMap()`, `map.LevelMapComp()` |
| `MF_LevelMapComp` | Level controller. `MapByLevel` dict, `VerticallyOutwardLevels`, `ValidStairsOnLevel`, `UpperLevelTerrainGrid` |
| `UpperLevelTerrainGrid` | `GroundsOnLevel` — `Dictionary<int, HashSet<IntVec3>>`. Cell NOT in grounds = void (water falls through) |
| `Stair` (abstract) | Base for `StairEntrance`/`StairExit`. Has `Direction` (Up/Down), `ConnectedStair`, `CurrentLevel` |
| `StairEntrance` | Top of stairs. `Direction.Down` = gravity flow downward |
| `StairExit` | Bottom of stairs. Compare levels to determine flow direction |
| `Elevator` | Extends `Stair`. `Functional` = has net + connected + not working + powered. `ElevatorNet.GetElevatorOnMap(destMap)` |
| `ElevatorNet` | Links elevators across levels. `LinkedElevators` HashSet |
| `LevelUtility` | `ConnectedToOtherLevel()`, `TryGetLevelControllerOnCurrentTile()`, `GetOtherMapLevelVerticallyOutward()` |

**Void terrain detection** (how water knows where to fall through upper floors):
```
controller.UpperLevelTerrainGrid.GetGroundsAtLevel(level) → HashSet<IntVec3>
Cell NOT in this set = void = water can fall through
```
This is biome-agnostic — never hardcode terrain defNames.

### Processing Modes

- **Tile-based** (default): Random sample up to `maxProcessedTilesPerTick` (500) from active set
- **Chunk-based** (optional): 8x8 spatial chunks, checkerboard pattern, up to `maxProcessedChunksPerTick` (20)

### Stability System

Tiles increment `stabilityCounter` on no-change ticks. At `stabilityCap` (100) they deregister from active processing. Reactivated by: neighbor volume change, building spawn/despawn (BFS wave), or `ReactivateInRadius()`.

## Open Issues & Roadmap

13 planned features in `issues/` directory (see `issues/README.md` for full roadmap):

- **Phase 1** (MVP+): #001 Vanilla Water Sink, #005 Floodgate, #002 Channels, #008 Grate/Bars
- **Phase 2** (Active): #003 Water Pump, #007 Spring Variants, #009 Hatch Cover
- **Phase 3** (Physics): #004 Pressure Propagation ✅, #012 Pump Stack
- **Phase 4** (Gameplay): #010 Water Wheel, #011 Seasonal Freezing
- **Phase 5** (Deep): #006 Purification, #013 Contamination

Remaining performance work: Strategies 5-6 in `todo.md` (update frequency optimization, data structure optimization).

## Conventions

- Logger: use `WaterSpringLogger.DebugEnabled` to guard hot-path logs
- Zero allocations in tick/OnGUI paths; reuse scratch buffers
- Mod package ID: `rodrigoschott.WaterSpringMod`
- XML Defs in `RimworldWaterSpringMod/Defs/`, English strings in `Languages/English/`
- `WaterSpring/Legacy/` contains old `WaterTransferManager` — excluded from build
- Debug hotkeys: Alt+W (water overlay), Alt+P (perf harness, dev builds only)
