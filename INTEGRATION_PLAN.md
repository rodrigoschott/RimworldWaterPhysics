# Water Physics × MultiFloors: Direct Integration Plan

**Author:** Luna (AI Assistant)  
**Date:** 2026-02-11  
**Version:** 2.2 (Final Review)  
**Target:** RimWorld Water Physics Mod integration with MultiFloors mod

---

## 🔄 Revision Notes (v2.1 - Code Review)

**Date:** 2026-02-11  
**Reviewer:** Code-level analysis of decompiled MultiFloors source

### Critical Fixes Applied:

1. **Void Terrain Detection (BREAKING)** ⚠️  
   - **Old:** Hardcoded defName checks (`"MF_SurfaceVoid"`, `"MF_TransparentFoundation"`)
   - **New:** Dynamic check using `UpperLevelTerrainGrid.GetGroundsAtLevel(level).Contains(cell)`
   - **Why:** Void terrain is biome-dependent via `MF_UpperLevelSettings.GetTerrainFor()`. Hardcoding breaks with custom biomes and Odyssey.
   - **Impact:** All code sections that checked terrain defNames have been updated to use GroundsAtLevel.

2. **Elevator API Semantics (CRITICAL)** 🚨  
   - **Confirmed:** `Elevator.GetDestinationLocation()` returns `IntVec3.Invalid` (does NOT throw)
   - **Confirmed:** `Elevator.GetOtherMap()` returns `null` (does NOT throw)
   - **Required:** Add null/Invalid guards before using elevator destinations
   - **Destination:** Must use `ElevatorNet.GetElevatorOnMap(destMap).Position` to get correct landing cell
   - **Impact:** Updated Phase 5 implementation guidance with proper guards.

3. **Elevator.Functional Property Logic (COUNTERINTUITIVE)** ⚙️  
   - **Confirmed:** Property EXISTS (contrary to initial assumption)
   - **Logic:** Returns `false` when `ElevatorNet.Working == true`
   - **Meaning:** `Working` = currently animating/transporting (NOT "operational")
   - **Correct usage:** Water should ONLY flow when `Functional == true` (elevator idle + powered)
   - **Impact:** Updated all elevator flow logic to check `Functional` properly.

4. **StairExit as Water Portal (FEATURE PARITY)** 🚪  
   - **Old:** Plan only mentioned checking `StairEntrance` for water flow
   - **Confirmed:** `StairExit` also implements `GetOtherMap()` and `GetDestinationLocation()`
   - **Reason:** StairExit represents "bottom of stairs" where water naturally pools/drains
   - **Impact:** Updated Phase 3 to treat StairExit as equal portal candidate to StairEntrance.

### Defensive Improvements Incorporated:

5. **Array Overflow Prevention** 🛡️  
   - Changed `validCells` array size from 8 to 12 elements (or recommend `List<IntVec3>`)
   - **Reason:** With stairs + elevators + void, 8 slots can overflow
   - **Impact:** Updated code examples in Phase 3.

6. **Safe Dictionary Access** 🔒  
   - Ensured all `VerticallyOutwardLevels` access uses `TryGetValue`
   - **Reason:** Defensive coding against mod version mismatches
   - **Impact:** Verified all code samples use TryGetValue pattern.

7. **Architecture Recommendation Strengthened** 🏗️  
   - **Primary recommendation:** Single DLL with runtime MF detection
   - **Method:** `GenTypes.GetTypeInAnyAssembly("MultiFloors.MF_LevelMapComp") != null`
   - **Reason:** This is standard RimWorld modding practice
   - **Impact:** Moved single-DLL approach to PRIMARY recommendation in Section 5.

8. **Time Estimate Adjusted** ⏱️  
   - **Old:** 40-60 hours
   - **New:** 60-80 hours (80-120h with full polish + Workshop + extensive testing)
   - **Reason:** More realistic given code complexity revealed by decompilation
   - **Impact:** Updated Section 10 summary.

### Confirmed Correct (No Changes):
✅ StairDirection logic for downward water flow  
✅ NOT integrating with `StairExit.SyncWithEntrance()` loop (water is directional, not passive)  
✅ Cross-map reentrancy assessment (low risk due to single-threaded RimWorld)  
✅ Overall hybrid architecture approach

---

## Revision History

**v2.2 (2026-02-11):** Final review. Two targeted fixes:
- StairExit flow now checks `exitMap.Level()` vs `Map.Level()` to determine direction (downward = gravity/free, upward = pressure-gated). Previous version assumed all StairExit flow was upward.
- Added explicit Mono lazy-JIT compilation note in Section 5.1 — documents why single-DLL compiles with MF reference but runs without it, and the critical rule of keeping MF types behind guarded methods.

**v2.1 (2026-02-11):** Code review revision incorporating decompiled source analysis. Fixed void terrain detection, elevator guards, Functional semantics, StairExit portal parity, defensive coding, and time estimates.

**v2.0 (2026-02-11):** Revised after code-level review of decompiled MultiFloors source. Key corrections:
- Fixed void terrain detection (use GroundsOnLevel, not hardcoded defNames)
- Corrected Elevator.Functional semantics (exists but inverted logic)
- Added StairExit as portal candidate
- Clarified why water should NOT use SyncWithEntrance
- Downgraded reentrancy risk assessment
- Adjusted time estimates and architecture recommendations

**v1.0 (2026-02-11):** Initial integration plan based on MultiFloors API documentation.

---

## Executive Summary

This document outlines a comprehensive plan to replace the generic `VerticalPortalBridge` reflection-based cross-level water flow system with direct integration against the MultiFloors API. The goal is to enable water to flow naturally through stairs, elevators, and holes between multiple vertical levels while maintaining performance and compatibility.

---

## 1. Current Water Physics Architecture

### 1.1 Core Water System

The Water Physics mod implements a cellular automata-style water diffusion system:

- **`FlowingWater` (Thing)**: Individual water tile entity with:
  - Volume range: 0-7 (MaxVolume)
  - Stability counter for performance optimization
  - Local diffusion logic that transfers water to adjacent tiles
  - Terrain sync (optional): mirrors volume to WaterShallow (1-4) or WaterDeep (5-7)

- **`GameComponent_WaterDiffusion`**: Centralized water management system
  - Active tile registry per map
  - Optional chunk-based spatial indexing
  - Frequency-based processing throttle
  - Evaporation system for stable tiles

- **`Building_WaterSpring`**: Water source that produces 1 volume per configurable interval (default 200 ticks)
  - Optional backlog system when source tile reaches MaxVolume

- **`Building_WSHole`**: Simple hole/void building (currently minimal implementation)

### 1.2 Current Cross-Level Flow (VerticalPortalBridge)

**File:** `VerticalPortalBridge.cs`

**Strategy:** Generic reflection-based system that:
1. Detects `WS_Hole` buildings at cell positions
2. Uses reflection to scan MapComps for `Map`-typed fields/properties to find linked maps
3. Implements heuristic fallback using biome names and level detection:
   - Detects levels via MapComp names (`MultiFloors.MF_UpperLevelMapComp`, `MF_BasementMapComp`)
   - Biome detection (`MF_UpperLevelBiome`, `MF_BasementBiome`, `Orbit`)
4. Caches map relationships with 1200-tick TTL
5. Maintains inverse index (`_uppersByLower`) for efficient upward activation

**Key Methods:**
- `IsHoleAt(Map, IntVec3)`: Checks for WS_Hole building (checks edifice + thing list + frames/blueprints)
- `TryGetLowerMap(Map, out Map)`: Finds the map below current map via reflection + heuristics
- `PropagateVerticalActivationIfHole(Map, IntVec3)`: Wakes upper maps when lower cell changes
- `PropagateVerticalActivationForCellAndCardinals(Map, IntVec3)`: Convenience method for center + 4 cardinals

**Water Flow Logic (in FlowingWater.AttemptLocalDiffusion):**
- Checks if current tile is a hole → adds lower map same-cell as candidate
- For each cardinal neighbor that's a hole → adds lower map neighbor-cell as candidate
- Transfers water cross-map manually (1 unit at a time, respects capacity + min-diff)
- Triggers vertical activation on volume changes

**Limitations:**
- No direct API calls to MultiFloors
- Relies on reflection and naming conventions
- No integration with stairs/elevators (only holes)
- Heuristic level detection may fail with custom configurations
- No access to MultiFloors' terrain grid or cross-level features

---

## 2. MultiFloors API Surface

### 2.1 Prepatcher Extensions (PrepatcherFields.cs)

**Direct map-level access via extension methods:**

```csharp
public static extern ref int Level(this Map map);
public static extern ref Map UpperMap(this Map map);
public static extern ref Map LowerMap(this Map map);
public static extern ref Map GroundMap(this Map map);
public static extern MF_LevelMapComp LevelMapComp(this Map map);
```

**Usage:** These are injected fields, accessible as properties/methods. Essential for:
- Getting level integer (ground=0, upper>0, basement<0)
- Direct linked map references (no reflection needed)
- Accessing central controller (`MF_LevelMapComp`)

### 2.2 Level Controller (MF_LevelMapComp)

**File:** `MF_LevelMapComp.cs`

**Primary API:**

```csharp
public class MF_LevelMapComp : MapComponent
{
    // Map registry
    public Dictionary<int, Map> MapByLevel { get; }
    public List<(int level, Map map)> SortedLevels { get; }
    public Dictionary<int, List<(int, Map)>> VerticallyOutwardLevels { get; }
    
    // Stair/elevator registry
    public Dictionary<int, HashSet<Stair>> ValidStairsOnLevel { get; }
    
    // Terrain grid (upper level transparency)
    public UpperLevelTerrainGrid UpperLevelTerrainGrid { get; }
    
    // Methods
    public IEnumerable<Map> GetAllLevelMaps(Map except = null);
    public bool HasMultiLevels => MapByLevel.Count > 1;
    public void RegisterStair(Stair stair, bool addToGraph = true);
    public void DeRegisterStair(Stair stair, bool removeFromGraph = true);
}
```

**Usage:** Central controller for all levels on a tile. Access via `map.GroundMap().LevelMapComp()`.

### 2.3 Stair & Elevator API

**Base class: Stair (abstract)**

```csharp
public abstract class Stair : MapPortal
{
    public StairDirection Direction { get; }  // Up or Down
    public abstract Stair ConnectedStair { get; }
    public Map ConnectedMap { get; }
    public int CurrentLevel => Map.Level();
    
    public override IntVec3 GetDestinationLocation();
    public override Map GetOtherMap();
}
```

**Concrete types:**
- `StairEntrance`: Initiates level change (spawns `StairExit` on destination)
- `StairExit`: Landing on destination level (links back to `StairEntrance`)
- `Elevator`: Network-based, links multiple elevators across levels via `ElevatorNet`

**StairExit cross-map methods:**
- `StairExit.GetOtherMap()` returns `ConnectedMap` (works correctly)
- `StairExit.GetDestinationLocation()` calculates based on Entrance position (works correctly)
- Water should check BOTH StairEntrance (top of stairs) and StairExit (bottom of stairs) as portals
- StairExit represents "the bottom of the stairs" where water naturally pools/drains

**Elevator.Functional property (IMPORTANT):**
```csharp
public bool Functional {
    get {
        if (ElevatorNet == null || !Map.ConnectedToOtherLevel() || ElevatorNet.Working)
            return false;
        if (PowerRequired) return CompPowerTrader.PowerOn;
        return true;
    }
}
```
**Note:** `ElevatorNet.Working` means "currently animating/transporting" — so `Functional` returns `true` when the elevator is idle+powered+connected. Water should only flow when `Functional == true` (elevator not in use).

**Key features:**
- Heat/gas/vacuum synchronization between entrance/exit via `StairExit.SyncWithEntrance()`
- Storage settings for auto-transfer
- Power requirements (via `CompPowerTrader`)

**Why water should NOT use SyncWithEntrance():**
- Heat/gas/vacuum are passive equalization systems that converge to average
- Water is directional transfer governed by gravity
- Water flow should remain in `GameComponent_WaterDiffusion` which ticks every frame
- SyncWithEntrance runs at 250+ tick intervals (too slow for fluid dynamics)

**Usage for water:**
- Stairs are explicit portal pairs with known positions
- `GetDestinationLocation()` gives exact landing cell
- Can iterate `ValidStairsOnLevel` to find all stairs on a map
- Only `Elevator.GetOtherMap()` returns null/Invalid — needs guard check

### 2.4 Upper Level Terrain Grid

**File:** `UpperLevelTerrainGrid.cs`

```csharp
public class UpperLevelTerrainGrid
{
    // Terrain tracking for upper levels
    public Dictionary<int, HashSet<IntVec3>> GroundsOnLevel { get; }
    public Dictionary<int, HashSet<IntVec3>> DeckCellsOnLevel { get; }
    
    // Visibility tracking
    public IEnumerable<(int, int)> GetVisibleSectionAtLevel(int level);
    
    // Modification
    public void AddRoofedGround(int level, IEnumerable<IntVec3> cells);
    public void RemoveRoofedGround(int level, IEnumerable<IntVec3> cells);
    public void AddDeckCells(int level, IEnumerable<IntVec3> cells);
    public void RemoveDeckCells(int level, IEnumerable<IntVec3> cells);
}
```

**CRITICAL: Void Terrain Detection** ⚠️

**⛔ DO NOT HARDCODE TERRAIN DEFNAMES** — Void terrain is biome-dependent and configured dynamically via `MF_UpperLevelSettings.GetTerrainFor(tile, layerDef)`. Hardcoded checks for `"MF_SurfaceVoid"`, `"MF_TransparentFoundation"`, etc. WILL BREAK with:
- Custom biomes
- RimWorld Odyssey (different void terrain)
- User-configured terrain overrides
- Future MultiFloors updates

**✅ CORRECT APPROACH (MANDATORY):**
```csharp
// Primary method: Check if cell is NOT in GroundsOnLevel
bool isVoid = !controller.UpperLevelTerrainGrid.GetGroundsAtLevel(level).Contains(cell);
```
**Why this works:**
- MultiFloors maintains `GroundsOnLevel` as cached HashSet per level
- Cells NOT in this set are void (transparent/passable)
- Already computed and optimized by MultiFloors
- Works with ALL biomes and configurations

**Alternative approach (only if needed):**
```csharp
// Check against configured list (less efficient)
bool isTransparent = MiscDefOfs.MF_UpperLevelSettings.IsTransparentTerrain(terrain);
```
**Use PRIMARY method in all water flow logic.**

**Usage:**
- `GroundsOnLevel[level]` contains all non-void cells on upper level
- Void cells should allow water to pass through to lower level
- DeckCells are substructure (Odyssey) — treat as solid unless intentionally made passable

### 2.5 Level Utilities

**File:** `LevelUtility.cs`

```csharp
public static class LevelUtility
{
    public static bool ConnectedToOtherLevel(this Map map);
    public static bool TryGetLevelControllerOnCurrentTile(this Map map, out MF_LevelMapComp controller);
    
    // Vertical iteration utilities
    public static List<int> GetOtherMapLevelVerticallyOutward(this Map map, List<int> levels, int maxMapsToExplore = -1);
    public static IEnumerable<Map> GetOtherMapVerticallyOutward(this Map map, MF_LevelMapComp controller, int maxMapsToExplore = -1);
}
```

**Usage:**
- Check if map is multi-level before attempting cross-level logic
- Iterate vertically outward from current level (sorted by distance)

### 2.6 Terrain Change Notifications

**File:** `HarmonyPatch_NotifyTerrainChanged.cs`

MultiFloors hooks `TerrainGrid.SetFoundation` and `RemoveFoundation` to:
- Track foundation/deck additions/removals
- Notify upper level terrain grid
- Mark renderer as dirty

**Integration point:** Water should similarly hook terrain changes to wake adjacent water when walls/doors/foundations change on any level.

---

## 3. Integration Points

### 3.1 Direct Map Linkage (Replace Reflection)

**Current:** `VerticalPortalBridge.TryGetLowerMap()` — 120+ lines of reflection + heuristics

**New:** Use MultiFloors Prepatcher fields directly

**File:** `VerticalPortalBridge.cs` (or new `MultiFloorsIntegration.cs`)

**Changes:**

```csharp
// OLD (VerticalPortalBridge.cs:line ~80-150)
public static bool TryGetLowerMap(Map current, out Map lower)
{
    // ... 80 lines of reflection scanning MapComps ...
    // ... fallback heuristics with biome/level detection ...
}

// NEW
public static bool TryGetLowerMap(Map current, out Map lower)
{
    lower = null;
    if (current == null) return false;
    
    // Direct access via Prepatcher
    lower = current.LowerMap();
    
    // Fallback: check controller if LowerMap is null
    if (lower == null && current.TryGetLevelControllerOnCurrentTile(out var controller))
    {
        int currentLevel = current.Level();
        if (controller.MapByLevel.TryGetValue(currentLevel - 1, out lower))
        {
            return lower != null;
        }
    }
    
    return lower != null;
}
```

**Similar changes for:**
- `TryGetUpperMap()` (new method)
- Remove `MapLevel` enum and `DetectLevel()` — use `map.Level()` directly
- Remove `_lowerMapCache` and `_uppersByLower` — MultiFloors maintains authoritative links

### 3.2 Hole Detection Integration

**Current:** `IsHoleAt()` checks for `WS_Hole` ThingDef

**New:** Also check MultiFloors void terrains using GroundsOnLevel (NO HARDCODED DEFNAMES)

**File:** `VerticalPortalBridge.cs:IsHoleAt()` (line ~45)

**Changes:**

```csharp
public static bool IsHoleAt(Map map, IntVec3 cell)
{
    // Existing WS_Hole check (keep for backward compat)
    if (WS_HoleExists(map, cell)) return true;
    
    // NEW: Check for MultiFloors void terrain
    // ⚠️ CRITICAL: Do NOT check terrain.defName - void is biome-dependent!
    if (map.Level() > 0)  // Only upper levels have void
    {
        if (!map.TryGetLevelControllerOnCurrentTile(out var controller))
            return false;
        
        // ✅ PRIMARY METHOD: Check if cell is NOT in GroundsOnLevel
        // This is the ONLY correct way to detect void terrain
        var grounds = controller.UpperLevelTerrainGrid.GetGroundsAtLevel(map.Level());
        if (grounds != null && !grounds.Contains(cell))
        {
            return true;  // Not in grounds = void (transparent/passable)
        }
    }
    
    return false;
}
```

**Rationale:** 
- Uses MultiFloors' authoritative terrain grid (GroundsOnLevel)
- Works with ALL biomes (vanilla, Odyssey, custom)
- No hardcoded defName checks that break with configuration changes
- Data is already cached by MultiFloors (no performance cost)

### 3.3 Stair-Based Water Flow (New Feature)

**Current:** Water only flows through holes

**New:** Water flows down stairs (StairEntrance) and through StairExit (bottom of stairs)

**File:** `FlowingWater.cs:AttemptLocalDiffusion()` (line ~425)

**Critical Changes:**

⚠️ **ARRAY SIZE:** Increase `validCells` from 8 to **12 elements** (or use `List<IntVec3>`) to prevent overflow when adding stairs + elevators + void portals.

**Changes:**

```csharp
// ⚠️ INCREASED from 8 to 12 to accommodate stairs+elevators+void
IntVec3[] validCells = new IntVec3[12];
IntVec3[] targetCells = new IntVec3[12];
Map[] targetMaps = new Map[12];
FlowingWater[] existingWaters = new FlowingWater[12];
int validCount = 0;

// In AttemptLocalDiffusion(), after checking for holes
foreach (IntVec3 neighbor in GenAdj.CardinalDirections)
{
    IntVec3 adjacentCell = pos + neighbor;
    if (!adjacentCell.InBounds(Map)) continue;
    
    // NEW: Check for downward stairs (StairEntrance) at neighbor
    var stairDown = Map.thingGrid.ThingAt<StairEntrance>(adjacentCell);
    if (stairDown != null && stairDown.Direction == StairDirection.Down && stairDown.ConnectedMap != null)
    {
        IntVec3 destCell = stairDown.GetDestinationLocation();
        Map destMap = stairDown.ConnectedMap;
        
        if (destCell.InBounds(destMap) && destCell.Walkable(destMap))
        {
            FlowingWater lowerWater = destMap.thingGrid.ThingAt<FlowingWater>(destCell);
            validCells[validCount] = adjacentCell;
            targetCells[validCount] = destCell;
            targetMaps[validCount] = destMap;
            existingWaters[validCount] = lowerWater;
            validCount++;
            continue;  // Don't also treat as normal neighbor
        }
    }
    
    // NEW: Check for StairExit (bottom of stairs - water pools/drains here)
    // 🚪 StairExit has GetOtherMap() and GetDestinationLocation() - treat as portal
    var stairExit = Map.thingGrid.ThingAt<StairExit>(adjacentCell);
    if (stairExit != null)
    {
        // StairExit.GetOtherMap() returns ConnectedMap (the Entrance's map)
        Map exitMap = stairExit.GetOtherMap();
        
        // StairExit.GetDestinationLocation() calculates from entrance position
        IntVec3 exitDest = stairExit.GetDestinationLocation();
        
        if (exitMap != null && exitDest.InBounds(exitMap))
        {
            // ⚠️ DIRECTION CHECK: Determine if flow is downward or upward
            // StairExit lives on the DESTINATION map. GetOtherMap() returns the
            // Entrance's map. So:
            //   - If exitMap.Level() > Map.Level() → flow goes UP (needs pressure)
            //   - If exitMap.Level() < Map.Level() → flow goes DOWN (gravity, free)
            //   - If exitMap.Level() == Map.Level() → same level (shouldn't happen, skip)
            int destLevel = exitMap.Level();
            int currentLevel = Map.Level();
            
            if (destLevel == currentLevel) continue;  // Same level, not a vertical portal
            
            bool isUpwardFlow = destLevel > currentLevel;
            
            FlowingWater exitWater = exitMap.thingGrid.ThingAt<FlowingWater>(exitDest);
            int exitVol = exitWater?.Volume ?? 0;
            
            if (isUpwardFlow)
            {
                // Upward flow: only if current volume is very high (pressure/flooding)
                if (!WaterSpringMod.Settings.upwardStairFlowEnabled) continue;
                if (Volume < WaterSpringMod.Settings.minVolumeForUpwardFlow) continue;
                if (exitVol >= 3) continue;  // Destination already has significant water
            }
            // Downward flow: always allowed (gravity) — same rules as StairEntrance
            
            validCells[validCount] = adjacentCell;
            targetCells[validCount] = exitDest;
            targetMaps[validCount] = exitMap;
            existingWaters[validCount] = exitWater;
            validCount++;
            continue;
        }
    }
    
    // Existing hole check...
    if (VerticalPortalBridge.IsHoleAt(Map, adjacentCell))
    {
        // ... existing logic ...
    }
    
    // Existing same-map logic...
}
```

**Why StairExit matters:**
- Represents "bottom of stairs" where water naturally pools
- Has same cross-map methods as StairEntrance (GetOtherMap, GetDestinationLocation)
- Water falling through stair opening should consider BOTH entrance (top) and exit (bottom) as portals

**Complexity:** Moderate — requires understanding stair placement + destination calculation

### 3.4 Elevator Water Flow (Optional Enhancement)

**File:** `FlowingWater.cs:AttemptLocalDiffusion()`

**Strategy:** Elevators are more complex (network-based). Requires careful null/Invalid guards.

**⚠️ CRITICAL ELEVATOR API SEMANTICS:**

```csharp
// ❌ WRONG ASSUMPTIONS:
// - GetDestinationLocation() does NOT throw (returns IntVec3.Invalid)
// - GetOtherMap() does NOT throw (returns null)

// ✅ CORRECT APPROACH:
var elevator = Map.thingGrid.ThingAt<Elevator>(adjacentCell);
if (elevator != null && elevator.ElevatorNet != null)
{
    // Check Functional: true when idle+powered+connected
    // (Functional returns FALSE when ElevatorNet.Working == true)
    // Working means "currently animating/transporting" NOT "operational"
    if (!elevator.Functional) continue;  // Skip if animating or unpowered
    
    // GetOtherMap() returns null if not connected
    Map destMap = elevator.GetOtherMap();
    if (destMap == null) continue;  // Not connected to other level
    
    // GetDestinationLocation() returns IntVec3.Invalid if problem
    IntVec3 destPos = elevator.GetDestinationLocation();
    if (!destPos.IsValid || !destPos.InBounds(destMap)) continue;
    
    // ⚠️ WRONG: Do NOT use elevator.Position for destination
    // ✅ CORRECT: Use ElevatorNet to find actual destination elevator
    var destElevator = elevator.ElevatorNet.GetElevatorOnMap(destMap);
    if (destElevator == null) continue;
    
    IntVec3 elevDest = destElevator.Position;  // This is the correct landing cell
    
    FlowingWater destWater = destMap.thingGrid.ThingAt<FlowingWater>(elevDest);
    
    // Add as candidate (respect array bounds!)
    if (validCount < validCells.Length)
    {
        validCells[validCount] = adjacentCell;
        targetCells[validCount] = elevDest;
        targetMaps[validCount] = destMap;
        existingWaters[validCount] = destWater;
        validCount++;
    }
}
```

**Understanding Elevator.Functional:**

```csharp
// Decompiled logic (counterintuitive!):
public bool Functional {
    get {
        if (ElevatorNet == null || !Map.ConnectedToOtherLevel() || ElevatorNet.Working)
            return false;  // Note: Working = animating, so Functional is FALSE during use
        if (PowerRequired) return CompPowerTrader.PowerOn;
        return true;
    }
}
```

**Key points:**
- `Functional == true` means elevator is **idle** + powered + connected
- `ElevatorNet.Working == true` means **currently transporting** (NOT "operational")
- Water should only flow when `Functional == true` (elevator not in use)

**Recommendation:** 
- Start with simple implementation (Option A above)
- Add setting `elevatorWaterFlowEnabled` (default false — opt-in feature)
- Add setting `elevatorRequiresPower` (default true)

### 3.5 Cross-Level Activation (Enhance Existing)

**Current:** `PropagateVerticalActivationIfHole()` maintains `_uppersByLower` inverse index via reflection

**New:** Use MultiFloors' `VerticallyOutwardLevels` directly with defensive TryGetValue

**File:** `VerticalPortalBridge.cs:PropagateVerticalActivationIfHole()` (line ~285)

**Changes:**

```csharp
public static void PropagateVerticalActivationIfHole(Map map, IntVec3 pos)
{
    if (map == null) return;
    
    // NEW: Use MF controller directly
    if (!map.TryGetLevelControllerOnCurrentTile(out var controller)) return;
    
    int currentLevel = map.Level();
    
    // 🔒 DEFENSIVE: Use TryGetValue instead of direct indexer access
    // Protects against mod version mismatches or incomplete level rebuild
    if (!controller.VerticallyOutwardLevels.TryGetValue(currentLevel, out var outwardLevels))
    {
        // Level not in dictionary (shouldn't happen after ReBuildSortedLevels, but defensive)
        return;
    }
    
    // Iterate upward levels (vertically outward)
    // outwardLevels is List<(int level, Map map)> sorted by distance from currentLevel
    foreach (var (level, upperMap) in outwardLevels)
    {
        if (level <= currentLevel) continue;  // Only upward propagation
        if (upperMap == null) continue;  // Null guard
        if (!pos.InBounds(upperMap)) continue;
        
        // Only wake if upper map has hole/void at this position
        if (!IsHoleAt(upperMap, pos)) continue;
        
        // Wake water on upper level
        var gameComp = Current.Game?.GetComponent<GameComponent_WaterDiffusion>();
        var water = upperMap.thingGrid.ThingAt<FlowingWater>(pos);
        
        if (water != null && water.IsExplicitlyDeregistered)
        {
            water.Reactivate();
        }
        
        gameComp?.RegisterActiveTile(upperMap, pos);
        gameComp?.ActivateNeighbors(upperMap, pos);
        gameComp?.ReactivateInRadius(upperMap, pos);
    }
}
```

**Remove (cleanup reflection-based code):**
- `_uppersByLower` dictionary
- `_lastLowerOfUpper` tracking
- `RegisterUpperForLower()` method

**Why TryGetValue:**
- Defensive coding against mod version mismatches
- Handles edge case where level rebuild is incomplete
- Prevents KeyNotFoundException if MultiFloors state is inconsistent

### 3.6 Terrain Change Hooks

**Current:** `GameComponent_WaterDiffusion.NotifyTerrainChanged()` only handles same-map changes

**New:** Trigger cross-level activation when terrain changes on upper levels

**File:** `GameComponent_WaterDiffusion.cs:NotifyTerrainChanged()` (line ~475)

**Add after existing BFS wave:**

```csharp
public void NotifyTerrainChanged(Map map, IntVec3 position)
{
    // ... existing same-map logic ...
    
    // NEW: Propagate to levels above/below
    if (map.ConnectedToOtherLevel())
    {
        VerticalPortalBridge.PropagateVerticalActivationForCellAndCardinals(map, position);
        
        // Also check lower level if this is a foundation change on upper level
        if (map.Level() > 0)
        {
            Map lowerMap = map.LowerMap();
            if (lowerMap != null && position.InBounds(lowerMap))
            {
                // Wake water below that might now have outlet
                var waterBelow = lowerMap.thingGrid.ThingAt<FlowingWater>(position);
                if (waterBelow != null && waterBelow.IsExplicitlyDeregistered)
                {
                    waterBelow.Reactivate();
                }
                
                RegisterActiveTile(lowerMap, position);
                ActivateNeighbors(lowerMap, position);
            }
        }
    }
}
```

### 3.7 WS_Hole as Void Terrain (Optional)

**Strategy:** Make `WS_Hole` place void terrain on upper levels instead of being a building

**Benefits:**
- More natural integration (uses MF's terrain system)
- No extra building to manage
- Consistent with MF's design

**Changes:**

1. **New TerrainDef** (in mod's Defs XML):
```xml
<TerrainDef ParentName="MF_VoidTerrainBase">
  <defName>WS_WaterVoid</defName>
  <label>water passage</label>
  <description>An opening that allows water to flow between levels.</description>
  <texturePath>Terrain/Surfaces/WaterVoid</texturePath>
  <renderPrecedence>340</renderPrecedence>
  <affordances>
    <li>Walkable</li>
  </affordances>
  <designationCategory>Structure</designationCategory>
  <costList>
    <Steel>15</Steel>
  </costList>
</TerrainDef>
```

2. **Change `Building_WSHole` to PlaceWorker** that places terrain
3. **Update `IsHoleAt()`** to check using GroundsOnLevel method

**Complexity:** Medium — requires understanding RimWorld terrain system + MF's terrain hierarchy

**Recommendation:** Phase 2 enhancement after basic stair flow works

---

## 4. New Features Enabled

### 4.1 Water Flowing Down Stairs

**Behavior:**
- Water at volume ≥2 adjacent to downward stair entrance flows to stair exit on lower level
- Natural "waterfall" effect through multi-level bases
- Respects min volume difference settings

**Visual:**
- Optional: Custom graphic for water-on-stairs (future enhancement)
- Current: Water appears at bottom of stairs

**Use cases:**
- Drain water from flooded upper floors
- Create decorative waterfalls
- Emergency drainage during roof breaches

### 4.2 Water Draining Through Void Terrain

**Behavior:**
- Upper level void/transparent terrain allows water to drop to lower level
- Large areas of void = rapid drainage (each cell is independent portal)
- Natural flooding of lower levels from above

**Use cases:**
- Open-air courtyards with drainage to basement
- Multi-story atriums
- Space station water management (Odyssey)

### 4.3 Flooding Mechanics Across Levels

**Scenario 1: Burst Pipe on Upper Level**
- Water spreads on upper level
- Reaches void/stair → begins draining
- Lower level floods from "above"
- Visual: rain-like effect if many void cells

**Scenario 2: Rising Water from Below**
- Ground level floods (river breach, heavy rain)
- High volume (≥5) allows water to flow UP stairs
- Upper levels flood from below (realistic pressure)

**Settings to control:**
- `minVolumeForUpwardFlow` (default 5)
- `upwardFlowEnabled` (bool toggle)

### 4.4 Interaction with MF Terrain System

**Foundation Changes:**
- Placing foundation on upper level → removes void → stops drainage
- Removing foundation → creates void → resumes drainage
- Water reacts immediately (via terrain change hook)

**Temperature/Gas Sync:**
- MF already syncs heat/gas through stairs (StairExit.SyncWithEntrance)
- Water flow through stairs is complementary (physical matter vs energy/gas)
- Could add temperature exchange: cold water on upper level cools lower level room

**Odyssey Substructure:**
- Substructure (deck) is solid → water doesn't pass through
- Non-substructure void → water passes through
- Space level flooding (vacuum interaction?)

---

## 5. Architecture Recommendation

### 5.1 Single DLL with Runtime Detection ⭐ **PRIMARY RECOMMENDATION**

**✅ This is the STANDARD approach in RimWorld modding and should be used.**

**Strategy:** Use runtime type detection to switch behavior dynamically

```csharp
private static bool? _hasMultiFloors;

public static bool HasMultiFloors()
{
    if (_hasMultiFloors == null)
    {
        // ✅ STANDARD RimWorld pattern for soft dependencies
        _hasMultiFloors = GenTypes.GetTypeInAnyAssembly("MultiFloors.MF_LevelMapComp") != null;
    }
    return _hasMultiFloors.Value;
}

public static bool TryGetLowerMap(Map current, out Map lower)
{
    if (HasMultiFloors())
    {
        return TryGetLowerMap_MultiFloors(current, out lower);
    }
    else
    {
        return TryGetLowerMap_Reflection(current, out lower);
    }
}
```

**Why this is the PRIMARY recommendation:**
- ✅ **Industry standard** — Used by majority of RimWorld mods
- ✅ **Single DLL** — Users install ONE file
- ✅ **Graceful degradation** — Works with or without MultiFloors
- ✅ **Simple for users** — No need to choose versions
- ✅ **Fast detection** — One-time reflection check (cached)
- ✅ **Easy maintenance** — Single codebase to test

**Pros:**
- Single DLL (standard in RimWorld modding community)
- Graceful degradation
- Easy for users
- Detection is simple and fast (one-time reflection check)
- No dual-build complexity
- Works in all scenarios (vanilla, MF, custom mods)

**Cons:**
- Slightly more complex code paths (negligible)
- Minimal reflection overhead (cached after first check, <1ms)

**⚠️ COMPILATION NOTE — Compile-time vs Runtime Dependency:**

This approach requires `MultiFloors.dll` as a **compile-time reference** (`copy-local = false`) so the C# compiler can resolve `map.LowerMap()`, `map.Level()`, and other Prepatcher extension methods. However, at **runtime**, these methods are never JIT-compiled unless `IsAvailable == true`.

**Why this works on Mono (RimWorld's runtime):**
- Mono uses **method-level lazy JIT** — a method body is only compiled when first called
- All MF-specific calls live in `MultiFloorsIntegration.cs` methods (e.g., `TryGetLowerMap_MultiFloors`)
- These methods are only invoked when `HasMultiFloors()` returns true
- If MF isn't loaded, those methods are never called → never JIT'd → no `TypeLoadException`

**Critical rule:** Never put MF type references in a method that gets called unconditionally. Keep ALL MF-specific code behind the `IsAvailable` guard, in dedicated methods.

```csharp
// ✅ SAFE: MF types only in guarded method
if (MultiFloorsIntegration.IsAvailable)
    MultiFloorsIntegration.DoMFStuff();  // JIT'd only here

// ❌ UNSAFE: MF type in always-called method
public void Tick()
{
    var comp = map.LevelMapComp();  // BOOM — JIT fails if MF not loaded
}
```

**Implementation pattern:**

```csharp
// MultiFloorsIntegration.cs
public static class MultiFloorsIntegration
{
    private static bool? _available;
    
    public static bool IsAvailable
    {
        get
        {
            if (_available == null)
            {
                _available = GenTypes.GetTypeInAnyAssembly("MultiFloors.MF_LevelMapComp") != null;
            }
            return _available.Value;
        }
    }
    
    // All MF-specific methods here
    // Called only when IsAvailable == true
}
```

### 5.2 Alternative: Hybrid Approach (C# Preprocessor)

**Strategy:** Optional hard dependency via C# preprocessor

```csharp
#if MULTIFLOORS_INTEGRATION
    // Direct MultiFloors API calls
    lower = current.LowerMap();
    int level = current.Level();
#else
    // Fallback to VerticalPortalBridge reflection
    VerticalPortalBridge.TryGetLowerMap(current, out lower);
#endif
```

**Build Configurations:**
- `WaterPhysics.dll` (standalone): No MF dependency, uses reflection
- `WaterPhysics_MF.dll` (integrated): Hard dependency on MultiFloors, uses direct API

**Pros:**
- Users without MultiFloors get working mod (holes only)
- Users with MultiFloors get full integration (stairs, elevators, void terrain)
- Clean codebase (no reflection in integrated build)

**Cons:**
- Requires two build outputs
- Testing complexity (2x matrix)
- Documentation must explain difference

### 5.3 Alternative: Full Hard Dependency

**Strategy:** Single build that requires MultiFloors

**Pros:**
- Simpler codebase (single code path)
- Easier testing
- No reflection overhead

**Cons:**
- Forces users to install MultiFloors
- Breaks existing users who don't use MF
- May limit adoption

**Recommendation:** Only if Rodrigo is committed to MF-only development

### 5.4 Final Recommendation ⭐

**✅ USE SINGLE DLL WITH RUNTIME DETECTION (5.1)** — This is the ONLY recommended approach.

**Implementation checklist:**

1. ✅ **Create `MultiFloorsIntegration.cs`** with all direct API calls
2. ✅ **Use runtime detection** with `GenTypes.GetTypeInAnyAssembly()`
3. ✅ **Keep `VerticalPortalBridge.cs`** as fallback for non-MF environments
4. ✅ **Provide single build** that works with or without MultiFloors
5. ✅ **Use mod dependency metadata** for load order

**About.xml configuration:**

```xml
<!-- About.xml -->
<ModMetaData>
  <name>Water Physics</name>
  <loadAfter>
    <li>MultiFloors</li>  <!-- Optional soft dependency -->
  </loadAfter>
  <description>
Water physics simulation with multi-level support.
Works standalone or with MultiFloors for enhanced integration.
  </description>
</ModMetaData>
```

**MultiFloorsIntegration.cs (complete pattern):**

```csharp
// MultiFloorsIntegration.cs (new file)
using Verse;
using System.Collections.Generic;

namespace WaterSpringMod.WaterSpring
{
    /// <summary>
    /// MultiFloors integration via runtime detection (standard RimWorld pattern)
    /// </summary>
    public static class MultiFloorsIntegration
    {
        private static bool? _hasMF;
        
        /// <summary>
        /// Check if MultiFloors is loaded (cached after first call)
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                if (_hasMF == null)
                {
                    // Standard RimWorld soft dependency detection
                    _hasMF = GenTypes.GetTypeInAnyAssembly("MultiFloors.MF_LevelMapComp") != null;
                    
                    if (_hasMF.Value)
                    {
                        Log.Message("[WaterPhysics] MultiFloors integration ACTIVE");
                    }
                    else
                    {
                        Log.Message("[WaterPhysics] Standalone mode (generic portal bridge)");
                    }
                }
                return _hasMF.Value;
            }
        }
        
        public static bool TryGetLowerMap(Map map, out Map lower)
        {
            lower = null;
            if (!IsAvailable || map == null) return false;
            
            // Direct Prepatcher field access (MF provides this)
            lower = map.LowerMap();
            return lower != null;
        }
        
        public static bool TryGetUpperMap(Map map, out Map upper)
        {
            upper = null;
            if (!IsAvailable || map == null) return false;
            
            // Direct Prepatcher field access
            upper = map.UpperMap();
            return upper != null;
        }
        
        /// <summary>
        /// Check if cell is void terrain (NOT in GroundsOnLevel)
        /// ⚠️ Do NOT use hardcoded defNames - void is biome-dependent
        /// </summary>
        public static bool IsVoidTerrain(Map map, IntVec3 cell)
        {
            if (!IsAvailable || map.Level() <= 0)
                return false;
            
            if (!map.TryGetLevelControllerOnCurrentTile(out var controller))
                return false;
            
            // ✅ PRIMARY METHOD: Check if cell NOT in GroundsOnLevel
            var grounds = controller.UpperLevelTerrainGrid.GetGroundsAtLevel(map.Level());
            return grounds != null && !grounds.Contains(cell);
        }
        
        // ... other MF-specific methods ...
    }
}
```

**Why NOT dual-DLL approach:**
- ❌ Doubles testing matrix (2x complexity)
- ❌ User confusion (which version do I download?)
- ❌ Maintenance burden (two builds to release)
- ❌ NOT standard practice in RimWorld modding
- ✅ Single-DLL is universally preferred

---

## 6. Implementation Phases

### Phase 0: Preparation (1-2 hours)

**Tasks:**
- [ ] Create `MultiFloorsIntegration.cs` skeleton with runtime detection
- [ ] Add MultiFloors.dll as reference (copy-local = false)
- [ ] Test that code compiles and detects MF correctly

**Complexity:** Low  
**Risk:** None (no runtime changes)

### Phase 1: Direct Map Linkage (3-4 hours)

**Tasks:**
- [ ] Implement `MultiFloorsIntegration.TryGetLowerMap()` using `map.LowerMap()`
- [ ] Implement `TryGetUpperMap()` using `map.UpperMap()`
- [ ] Replace `VerticalPortalBridge.TryGetLowerMap()` call sites with runtime check
- [ ] Remove cache logic from `VerticalPortalBridge` when MF integration active
- [ ] Test in-game: water still flows through WS_Hole with and without MF

**Complexity:** Low  
**Risk:** Low (existing functionality unchanged)  
**Success Criteria:** Water flows through holes exactly as before, but uses direct API when MF is present

### Phase 2: Void Terrain Detection (2-3 hours)

**Tasks:**
- [ ] Implement `MultiFloorsIntegration.IsVoidTerrain()`
  - Check `map.Level() > 0`
  - Access `UpperLevelTerrainGrid.GetGroundsAtLevel()`
  - Return true if cell NOT in GroundsOnLevel (= void)
- [ ] Update `IsHoleAt()` to check void terrain when MF active
- [ ] Test: Place water on upper level near natural void → should drain

**Complexity:** Low-Medium  
**Risk:** Low (additive feature)  
**Success Criteria:** Water drains through MultiFloors' native void terrain without needing WS_Hole

### Phase 3: Stair Water Flow (8-12 hours)

**Tasks:**
- [ ] ⚠️ **CRITICAL:** Increase `validCells` array from 8 to **12 elements** (or use `List<IntVec3>`)
  - Required to prevent overflow with stairs+elevators+void portals
  - Update all related arrays (targetCells, targetMaps, existingWaters)
- [ ] Add stair detection in `FlowingWater.AttemptLocalDiffusion()`:
  - Check for `StairEntrance` at adjacent cell
  - Verify `Direction == StairDirection.Down`
  - Get destination via `stair.GetDestinationLocation()`
  - Add to valid candidates
- [ ] Add `StairExit` detection (water pools at bottom of stairs)
  - StairExit has same cross-map methods as StairEntrance
  - Treat as equal portal candidate
  - Allow upward flow only under high pressure (volume ≥ minVolumeForUpwardFlow)
- [ ] Implement cross-map transfer for stairs (reuse existing hole logic)
- [ ] Add upward stair flow (pressure-based):
  - Only allow if `Volume >= settings.minVolumeForUpwardFlow`
  - Check upper level has capacity
- [ ] Add setting `upwardStairFlowEnabled` (default true)
- [ ] Add setting `minVolumeForUpwardFlow` (default 5, range 1-7)
- [ ] Test scenarios:
  - Water source on upper level near downward stairs → drains
  - Flooded ground level near upward stairs → backs up to upper level
  - Water at various volumes (1-7) behaves correctly
  - No array overflow with 10+ simultaneous portal candidates

**Complexity:** Medium-High  
**Risk:** Medium (new mechanic, may have edge cases)  
**Success Criteria:** 
- Water flows down stairs naturally at ≥2 volume
- Water flows up stairs only when flooded (≥5 volume by default)
- No duplication or loss of water volume
- No IndexOutOfRangeException with many portals

### Phase 4: Cross-Level Activation (4-6 hours)

**Tasks:**
- [ ] Refactor `PropagateVerticalActivationIfHole()`:
  - Remove `_uppersByLower` inverse index
  - Use `controller.VerticallyOutwardLevels[currentLevel]` with TryGetValue
  - Iterate levels in distance order (MF provides this)
- [ ] Update `NotifyTerrainChanged()`:
  - Add cross-level propagation after same-map BFS
  - Wake lower level when upper foundation removed
  - Wake upper level when lower void created
- [ ] Test:
  - Remove wall on upper level → water on lower level reacts
  - Place foundation over void → water on lower level stops flowing up

**Complexity:** Medium  
**Risk:** Low-Medium (performance implications if too aggressive)  
**Success Criteria:** Water reacts to terrain changes on adjacent levels within 1-2 ticks

### Phase 5: Elevator Water Flow (6-8 hours)

**Tasks:**
- [ ] Add elevator detection in `AttemptLocalDiffusion()`:
  - Check for `Elevator` at adjacent cell
  - ⚠️ **MANDATORY:** Verify `elevator.Functional` returns true
    - `Functional == true` means idle + powered + connected
    - `Functional == false` when `ElevatorNet.Working == true` (animating)
  - ⚠️ **MANDATORY:** Guard `GetOtherMap()` for null (does NOT throw)
  - ⚠️ **MANDATORY:** Guard `GetDestinationLocation()` for IntVec3.Invalid (does NOT throw)
  - ✅ **CORRECT:** Get target elevator via `ElevatorNet.GetElevatorOnMap(destMap)`
  - ❌ **WRONG:** Do NOT use `elevator.Position` for destination
  - Add destination cell to candidates (respect array bounds!)
- [ ] Add setting `elevatorWaterFlowEnabled` (default false — opt-in)
- [ ] Add setting `elevatorRequiresPower` (default true)
- [ ] Optional: Add animation delay (simulate travel time)
- [ ] Test scenarios:
  - Water flows through powered, idle elevator network
  - Water STOPS when elevator is in use (Working == true)
  - Water STOPS when elevator loses power
  - Multiple elevators in network behave correctly
  - Null/Invalid guards prevent crashes

**Complexity:** High  
**Risk:** Medium-High (ElevatorNet is complex, guards are mandatory)  
**Success Criteria:** 
- Water flows through elevator shafts when enabled, powered, and idle
- No crashes from null/Invalid returns
- Proper destination calculation via ElevatorNet

### Phase 6: Polish & Documentation (4-6 hours)

**Tasks:**
- [ ] Add in-game descriptions for new features
- [ ] Update mod settings UI:
  - Section: "Multi-Level Integration (MultiFloors)"
  - Toggle: Enable stair water flow
  - Toggle: Enable upward stair flooding
  - Slider: Min volume for upward flow (1-7, default 5)
  - Toggle: Enable elevator water flow
  - Toggle: Elevators require power
- [ ] Write user documentation:
  - How to set up multi-level water features
  - Stair placement for drainage
  - Void terrain vs WS_Hole
  - Performance tips (too many open voids = lag)
- [ ] Create example scenarios / test maps
- [ ] Update Steam Workshop description

**Complexity:** Medium  
**Risk:** None  
**Success Criteria:** Users can understand and configure all new features

### Phase 7: Testing & Refinement (8-12 hours)

**Tasks:**
- [ ] Performance testing:
  - Large base with 3+ levels
  - Many stairs/voids active simultaneously
  - Monitor TPS with active tile system
- [ ] Edge case testing:
  - Water at stair on map edge
  - Elevator destroyed while water flowing
  - Map deleted while water in-transit
  - Save/load with cross-level water
- [ ] Balance testing:
  - Is upward flow too aggressive/weak?
  - Do stairs drain too fast/slow?
  - Does evaporation work cross-level?
- [ ] Bug fixing based on findings
- [ ] Optimization if needed (chunk-based processing for cross-level?)

**Complexity:** Variable  
**Risk:** High (unknown unknowns)  
**Success Criteria:** No crashes, stable TPS, intuitive behavior

---

## 7. Risk Assessment

### 7.1 MultiFloors Version Coupling

**Risk:** MultiFloors API may change in future updates

**Mitigation:**
- Use only Prepatcher fields (stable interface)
- Avoid internal/private members
- Test against MultiFloors version range (1.0-1.2)
- Document tested MF version in mod description
- Add version check at runtime:
  ```csharp
  var mfVersion = LoadedModManager.GetMod<MultiFloorsMod>()?.GetVersion();
  if (mfVersion < new Version(1, 0) || mfVersion >= new Version(2, 0))
  {
      Log.Warning("[WaterPhysics] MultiFloors version mismatch");
  }
  ```

**Severity:** Medium  
**Likelihood:** Medium (MF is mature but active development)

### 7.2 Prepatcher Dependency

**Risk:** Prepatcher (0PrepatcherAPI.dll) may have compatibility issues

**Mitigation:**
- Prepatcher is widely used (stable)
- Only use documented `[PrepatcherField]` attributes
- Don't directly depend on Prepatcher.dll (only MF)
- Test without Prepatcher loaded (should fail gracefully)

**Severity:** Low  
**Likelihood:** Low (Prepatcher is stable infrastructure)

### 7.3 Performance Impact

**Risk:** Cross-level water checks add CPU overhead

**Concerns:**
- Iterating stairs on each diffusion attempt
- Cross-map Thing lookups (different Map.thingGrid)
- Activation waves across multiple levels

**Mitigation:**
- Cache stair positions per-chunk (spatial index)
- Only check stairs when adjacent to stair cell (not every tile)
- Limit vertical propagation depth (settings)
- Reuse existing chunk-based processing
- Profile before/after with DevMode performance tools

**Severity:** Medium  
**Likelihood:** Medium (depends on base complexity)

### 7.4 Compatibility with Other Mods

**Risk:** Other mods may conflict with cross-level water

**Scenarios:**
- Mods that change terrain (expect same-map only)
- Mods that modify stairs (might break water flow)
- Mods that add new map types (won't have MF integration)

**Mitigation:**
- Keep VerticalPortalBridge fallback functional
- Add mod compatibility settings (blacklist certain maps)
- Coordinate with other mod authors (test with common mods)
- Document known incompatibilities

**Severity:** Low-Medium  
**Likelihood:** Low (water mod is fairly isolated)

### 7.5 Save/Load Corruption

**Risk:** Water in-transit between levels during save

**Concern:**
- Water entity on Map A references position on Map B
- Maps load in undefined order
- Water volume might duplicate/vanish

**Mitigation:**
- Don't persist cross-map transfers (complete or cancel on save)
- Add validation in `FlowingWater.ExposeData()`:
  ```csharp
  public override void ExposeData()
  {
      base.ExposeData();
      
      if (Scribe.mode == LoadSaveMode.PostLoadInit)
      {
          // Validate position is on correct map
          if (!Position.InBounds(Map))
          {
              Log.Error($"[WaterPhysics] Water at invalid position after load");
              Destroy();
          }
      }
  }
  ```
- Extensive save/load testing with multi-level setups

**Severity:** High (corrupts saves)  
**Likelihood:** Low (if properly handled)

### 7.6 Map Deletion While Water Flowing

**Risk:** User destroys upper/lower level while water is flowing

**Scenario:**
- Water on Map A queued to transfer to Map B
- User removes Map B via dev tools / mod / caravan leaving
- Transfer attempts to spawn on null map → crash

**Mitigation:**
- Check map validity before cross-map operations:
  ```csharp
  if (targetMap == null || targetMap.Disposed || !Find.Maps.Contains(targetMap))
  {
      // Cancel transfer, keep water on source map
      return false;
  }
  ```
- Subscribe to map removal events (if available)
- Add try-catch around cross-map spawns

**Severity:** High (crash)  
**Likelihood:** Low (unusual player action)

### 7.7 Cross-Map Reentrancy (MINOR)

**Risk:** Modifying HashSet while iterating over it

**Concern:**
- `AttemptLocalDiffusion()` iterates active water tiles on Map A
- Cross-level transfer adds water to Map B
- Map B's water then triggers back to Map A

**Analysis:**
- RimWorld is single-threaded
- Cross-map writes modify the OTHER map's HashSet, not the one being iterated
- Iterator invalidation is unlikely in practice

**Mitigation:**
- Note for testing: monitor for collection modified exceptions
- If issues arise, consider snapshot pattern (copy active tiles before iteration)

**Severity:** Low  
**Likelihood:** Low (unlikely due to single-threaded nature)

---

## 8. Testing Strategy

### 8.1 Unit Test Scenarios (Manual)

**Test 1: Hole Flow (Baseline)**
- Place WS_Hole on upper level
- Place water source above hole
- Verify: Water drains to lower level
- Measure: Transfer rate (units/tick)

**Test 2: Void Terrain Flow**
- Create upper level with natural MF void
- Place water source on upper level
- Verify: Water drains through void
- Compare to Test 1 (should be identical)

**Test 3: Stair Downward Flow**
- Place downward stairs (StairEntrance)
- Place water source near stair entrance
- Verify: Water flows to stair exit on lower level
- Check: No water duplication
- Check: Water appears at correct destination cell

**Test 4: Stair Upward Flow (Pressure)**
- Place upward stairs (StairExit on ground, Entrance on upper)
- Flood ground level (volume 7 everywhere)
- Verify: Water backs up stairs to upper level
- Check: Only happens when volume ≥ minVolumeForUpwardFlow

**Test 5: Elevator Flow**
- Build elevator network (3 elevators, 3 levels)
- Power the elevators
- Place water source at top elevator
- Verify: Water flows to bottom elevator
- Unpower elevators → verify flow stops (if setting enabled)

**Test 6: Cross-Level Activation**
- Set up stable water on lower level
- Remove foundation on upper level above water
- Verify: Lower water reactivates and starts flowing up
- Time: Should happen within 2 ticks

**Test 7: Save/Load Persistence**
- Set up multi-level water flow (active transfer)
- Save game mid-flow
- Load save
- Verify: Water is in correct positions
- Verify: No duplication or loss
- Verify: Flow resumes correctly

**Test 8: Performance Benchmark**
- Large base (250x250) with 5 levels
- 50+ active water sources across levels
- 100+ stairs/holes connecting levels
- Monitor: TPS (should stay >30 with default settings)
- Profile: CPU time in water diffusion methods

### 8.2 Integration Test Scenarios

**Test 9: Complete Drainage System**
- Build multi-story base with:
  - Water source on level 3 (roof)
  - Stairs connecting 3→2→1→0
  - Collection basin on ground level
- Let run for 1 in-game day
- Verify: Water reaches ground level
- Verify: No water stuck on intermediate levels

**Test 10: Flood Disaster**
- Breach roof on level 2 during rainstorm (if possible)
- Let water accumulate
- Verify: Water flows down to level 1 and 0
- Verify: Flooding spreads realistically

**Test 11: Compatibility - No MultiFloors**
- Run with runtime detection detecting no MF
- Place WS_Hole on single-level map
- Verify: Mod still functions
- Verify: No errors in log

**Test 12: Compatibility - With MultiFloors**
- Run with MF detected
- Load MF scenario with pre-built multi-level base
- Add water sources
- Verify: All integration features work
- Check log for any warnings

### 8.3 Edge Case Tests

**Test 13: Stair at Map Edge**
- Place stair at x=0 or x=mapSize-1
- Place water source adjacent
- Verify: No out-of-bounds errors
- Verify: Water flows correctly or stays in bounds

**Test 14: Elevator During Construction**
- Start building elevator (frame/blueprint)
- Place water near construction site
- Verify: Water doesn't flow through incomplete elevator
- Complete construction → verify flow resumes

**Test 15: Rapid Level Switching**
- Have pawn switch levels via stairs repeatedly
- Have water flowing through same stairs
- Verify: No interaction issues
- Verify: Water and pawn don't collide unexpectedly

**Test 16: Map Deletion**
- Set up water flowing from level 1 → level 0
- Use dev mode to delete level 1
- Verify: No crash
- Verify: Water on level 0 remains stable

---

## 9. Code Organization

### 9.1 File Structure

```
RimworldWaterSpringMod/Source/WaterSpringMod/
├── WaterSpring/
│   ├── FlowingWater.cs              (MODIFY: Add stair/elevator flow logic)
│   ├── GameComponent_WaterDiffusion.cs  (MODIFY: Add cross-level activation)
│   ├── Building_WaterSpring.cs      (no changes)
│   ├── Building_WSHole.cs           (no changes)
│   ├── VerticalPortalBridge.cs      (MODIFY: Add void terrain check)
│   ├── MultiFloorsBridge.cs         (DEPRECATE: Mark obsolete)
│   └── MultiFloorsIntegration.cs    (NEW: Direct MF API wrapper)
├── WaterSpringMod.cs                (MODIFY: Settings UI additions)
└── WaterSpringModSettings.cs        (MODIFY: New settings fields)
```

### 9.2 New Settings Fields (WaterSpringModSettings.cs)

```csharp
public class WaterSpringModSettings : ModSettings
{
    // ... existing settings ...
    
    // Multi-level integration
    public bool stairWaterFlowEnabled = true;
    public bool upwardStairFlowEnabled = true;
    public int minVolumeForUpwardFlow = 5;
    
    public bool elevatorWaterFlowEnabled = false;  // Opt-in
    public bool elevatorRequiresPower = true;
    
    public bool useMultiFloorsVoidTerrain = true;
    public int maxVerticalPropagationDepth = 3;  // Limit activation waves
    
    public override void ExposeData()
    {
        base.ExposeData();
        
        Scribe_Values.Look(ref stairWaterFlowEnabled, "stairWaterFlowEnabled", true);
        Scribe_Values.Look(ref upwardStairFlowEnabled, "upwardStairFlowEnabled", true);
        Scribe_Values.Look(ref minVolumeForUpwardFlow, "minVolumeForUpwardFlow", 5);
        Scribe_Values.Look(ref elevatorWaterFlowEnabled, "elevatorWaterFlowEnabled", false);
        Scribe_Values.Look(ref elevatorRequiresPower, "elevatorRequiresPower", true);
        Scribe_Values.Look(ref useMultiFloorsVoidTerrain, "useMultiFloorsVoidTerrain", true);
        Scribe_Values.Look(ref maxVerticalPropagationDepth, "maxVerticalPropagationDepth", 3);
    }
}
```

### 9.3 Build Configuration

**Single DLL approach (recommended):**

```csharp
// MultiFloorsIntegration.cs
using Verse;

namespace WaterSpringMod.WaterSpring
{
    public static class MultiFloorsIntegration
    {
        private static bool? _hasMF;
        
        public static bool IsAvailable
        {
            get
            {
                if (_hasMF == null)
                {
                    _hasMF = GenTypes.GetTypeInAnyAssembly("MultiFloors.MF_LevelMapComp") != null;
                }
                return _hasMF.Value;
            }
        }
        
        // ... integration methods ...
    }
}
```

**Loader logic (in WaterSpringModMain constructor):**

```csharp
public WaterSpringModMain(ModContentPack content) : base(content)
{
    if (MultiFloorsIntegration.IsAvailable)
    {
        Log.Message("[WaterPhysics] MultiFloors integration ACTIVE");
    }
    else
    {
        Log.Message("[WaterPhysics] Standalone mode (generic portal bridge)");
    }
    
    // ... rest of initialization ...
}
```

---

## 10. Summary

This integration plan provides a roadmap to transform the Water Physics mod from a reflection-based generic cross-level system to a first-class MultiFloors citizen. The single-DLL runtime detection architecture ensures backward compatibility while enabling powerful new features like stair/elevator water flow, void terrain drainage, and cross-level flooding mechanics.

**Key Takeaways:**

1. **Direct API access** via Prepatcher fields eliminates reflection overhead
2. **Void terrain detection** must use GroundsAtLevel (NO hardcoded defNames)
3. **Stair-based flow** is the killer feature (most visible to players)
4. **Elevator guards** are mandatory (GetOtherMap returns null, GetDestinationLocation returns Invalid)
5. **Single DLL with runtime detection** is STANDARD practice in RimWorld modding
6. **Array overflow prevention** — increase validCells from 8 to 12
7. **Phased implementation** allows incremental testing and refinement

**Estimated Total Effort:** 60-80 hours base implementation  
**(80-120 hours with full polish + Workshop + extensive testing)**

**Priority Order:**
1. Phase 1: Direct Map Linkage (foundational) — 3-4h
2. Phase 3: Stair Water Flow (high impact) — 8-12h
3. Phase 2: Void Terrain Detection (critical correctness) — 2-3h
4. Phase 4: Cross-Level Activation (performance) — 4-6h
5. Phase 5: Elevator Flow (optional enhancement) — 6-8h
6. Phase 6: Polish & Documentation — 4-6h
7. Phase 7: Testing & Refinement — 8-12h

**Total base phases:** 35-51 hours  
**With contingency + polish:** 60-80 hours  
**Production-ready with Workshop:** 80-120 hours

**Success Metrics:**
- ✅ Water flows through stairs naturally
- ✅ No performance degradation (<5% TPS impact)
- ✅ Save/load stability (no corruption)
- ✅ Positive user feedback (Steam Workshop)

---

**Next Steps:**

1. Review this plan with Rodrigo
2. Set up build configuration (Phase 0)
3. Begin Phase 1 implementation
4. Create test map for continuous validation
5. Document progress and discoveries

**Questions for Rodrigo:**

1. ✅ **ANSWERED:** Single DLL with runtime detection is CONFIRMED as standard approach
2. ✅ **ANSWERED:** Elevators should be opt-in (default false) — complex feature
3. Any specific MultiFloors version to target? (recommend 1.0-1.2 range)
4. Desired release timeline?
5. Should WS_Hole become void terrain or stay as building? (Phase 2 enhancement)
6. **NEW:** Acceptable TPS impact threshold? (currently targeting <5%)
7. **NEW:** Should upward stair flow be enabled by default? (recommend true)

---

## Code Review Compliance Checklist ✅

- [x] Void terrain detection uses GroundsAtLevel (NO hardcoded defNames)
- [x] Elevator.GetDestinationLocation() documented as returning Invalid (not throw)
- [x] Elevator.GetOtherMap() documented as returning null (not throw)
- [x] Elevator.Functional semantics documented (counterintuitive Working logic)
- [x] StairExit added as equal portal candidate to StairEntrance
- [x] validCells array increased from 8 to 12
- [x] VerticallyOutwardLevels access uses TryGetValue
- [x] Single-DLL approach promoted to PRIMARY recommendation
- [x] Time estimate adjusted to 60-80h (80-120h with polish)
- [x] Confirmed correct: StairDirection logic, NOT using SyncWithEntrance, reentrancy assessment

---

*This plan is a living document. Update as implementation progresses and new information emerges.*

**Last Updated:** 2026-02-11 (v2.1 - Code Review Revision)
