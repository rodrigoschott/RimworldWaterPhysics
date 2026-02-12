# Issue #001: Vanilla Water Body Integration (Sink/Drain System)

**Status:** Planned  
**Priority:** High  
**Complexity:** ⭐⭐ (Low-Medium)  
**Estimated Hours:** 4-8h  
**Dependencies:** None  
**Created:** 2026-02-11  

---

## Problem Statement

When the mod's `FlowingWater` expands and reaches a cell with vanilla RimWorld water terrain (rivers, lakes, marshes, oceans), it currently treats those cells as "passable" and **spawns a new FlowingWater thing on top of the vanilla water terrain**. This creates several problems:

1. **Visual Duplication** — Mod water overlays on top of natural water visuals
2. **Volume Absurdity** — A river cell now has both vanilla water AND mod water with volume 1-7
3. **Infinite Expansion** — Water keeps spreading across the entire body of water instead of "merging"
4. **No Drain Behavior** — In reality, water from a spring reaching a lake should be absorbed by the lake
5. **Terrain Sync Conflicts** — `TrySyncTerrainToVolume()` replaces vanilla `WaterDeep` with `WaterShallow` when mod volume is 1-4, destroying the natural terrain appearance

---

## Current Code Analysis

### Where vanilla water is already detected

**`FlowingWater.IsCellPassableForWater()` (line ~941):**
```csharp
private bool IsCellPassableForWater(IntVec3 cell)
{
    if (!cell.Walkable(Map))
    {
        TerrainDef t = Map.terrainGrid.TerrainAt(cell);
        if (t != null && (t == TerrainDefOf.WaterShallow || t == TerrainDefOf.WaterDeep))
        {
            return true;  // <-- Allows flow INTO vanilla water
        }
        return false;
    }
    return true;
}
```

**Also in `GameComponent_WaterDiffusion.IsCellPassableForWater()` (similar logic):**
```csharp
private bool IsCellPassableForWater(Map map, IntVec3 cell)
{
    if (cell.Walkable(map)) return true;
    var t = map.terrainGrid?.TerrainAt(cell);
    if (t != null && (t == TerrainDefOf.WaterShallow || t == TerrainDefOf.WaterDeep)) return true;
    return false;
}
```

**Problem:** Both only check `WaterShallow` and `WaterDeep`. Missing: `WaterMovingShallow`, `WaterMovingChestDeep`, `WaterOceanShallow`, `WaterOceanDeep`, `Marsh`.

### Where new FlowingWater is spawned on vanilla water

**`FlowingWater.AttemptLocalDiffusion()` (line ~631-700):**
The "empty cell expansion" logic spawns new FlowingWater at any `validCell` where `existingWaters[i] == null`. Vanilla water cells are "passable" but have no FlowingWater — so the mod sees them as empty and spawns one.

### Where terrain sync conflicts happen

**`FlowingWater.TrySyncTerrainToVolume()` (line ~905-930):**
```csharp
if (band == 1)
    grid.SetTerrain(Position, TerrainDefOf.WaterShallow);  // Overwrites WaterDeep!
else if (band == 2)
    grid.SetTerrain(Position, TerrainDefOf.WaterDeep);
```
If a FlowingWater with volume 2 exists on a `WaterDeep` cell, it overwrites it with `WaterShallow`. On destroy, `TryRestoreOriginalTerrain()` restores it, but the flicker is visible.

---

## Proposed Solution: Sink/Drain System

### Core Concept

Vanilla water cells act as **sinks** — they absorb FlowingWater volume without spawning anything. Water that reaches a natural body of water simply disappears (drains into the body).

### Implementation Details

#### 1. New helper method: `IsVanillaWaterTerrain()`

```csharp
/// <summary>
/// Returns true if the cell has vanilla RimWorld water terrain
/// (not placed by this mod's TrySyncTerrainToVolume).
/// </summary>
private static bool IsVanillaWaterTerrain(Map map, IntVec3 cell)
{
    if (map == null) return false;
    TerrainDef t = map.terrainGrid.TerrainAt(cell);
    if (t == null) return false;
    
    // All vanilla water terrain types
    return t == TerrainDefOf.WaterShallow
        || t == TerrainDefOf.WaterDeep
        || t == TerrainDefOf.WaterMovingShallow
        || t == TerrainDefOf.WaterMovingChestDeep
        || t == TerrainDefOf.WaterOceanShallow
        || t == TerrainDefOf.WaterOceanDeep
        || t == TerrainDefOf.Marsh;
}
```

#### 2. New helper: `IsVanillaWaterSink()`

```csharp
/// <summary>
/// A cell is a vanilla water sink if it has vanilla water terrain 
/// AND no existing FlowingWater thing (i.e., it's a natural body of water,
/// not a cell our mod already claimed).
/// </summary>
private static bool IsVanillaWaterSink(Map map, IntVec3 cell)
{
    if (!IsVanillaWaterTerrain(map, cell)) return false;
    // If there's already FlowingWater here, it's ours, not a sink
    return map.thingGrid.ThingAt<FlowingWater>(cell) == null;
}
```

#### 3. Modify `AttemptLocalDiffusion()` — Sink priority pass

**Insert before the "empty cell expansion" block (before line ~626):**

```csharp
// PRIORITY 0: Drain into vanilla water sinks
if (settings.vanillaWaterAbsorptionEnabled && Volume >= 2)
{
    int sinkIndex = -1;
    int sinkSeen = 0;
    for (int i = 0; i < validCount; i++)
    {
        IntVec3 targetCell = targetCells[i];
        Map tMap = targetMaps[i] ?? Map;
        // Only consider same-map, empty (no FlowingWater), vanilla water terrain
        if (existingWaters[i] == null && IsVanillaWaterSink(tMap, targetCell))
        {
            sinkSeen++;
            if (Rand.Range(0, sinkSeen) == 0)
                sinkIndex = i;
        }
    }
    
    if (sinkIndex >= 0)
    {
        // Determine absorption rate based on water depth
        Map sinkMap = targetMaps[sinkIndex] ?? Map;
        TerrainDef sinkTerrain = sinkMap.terrainGrid.TerrainAt(targetCells[sinkIndex]);
        int absorptionRate = GetAbsorptionRate(sinkTerrain, settings);
        int absorbed = Math.Min(absorptionRate, Volume - 1); // Keep at least 1
        if (absorbed > 0)
        {
            Volume -= absorbed;
            if (debug)
            {
                WaterSpringLogger.LogDebug($"[Sink] Drained {absorbed} into vanilla water at {targetCells[sinkIndex]} ({sinkTerrain.defName})");
            }
            return true;
        }
    }
}
```

#### 4. Absorption rate by water type

```csharp
private static int GetAbsorptionRate(TerrainDef terrain, WaterSpringModSettings settings)
{
    int baseRate = settings.vanillaWaterAbsorptionRate; // default 1
    
    // Deep/ocean water absorbs faster (larger body = faster drain)
    if (terrain == TerrainDefOf.WaterDeep 
        || terrain == TerrainDefOf.WaterOceanDeep
        || terrain == TerrainDefOf.WaterMovingChestDeep)
    {
        return Math.Min(baseRate * 2, FlowingWater.MaxVolume);
    }
    
    // Moving water carries away faster
    if (terrain == TerrainDefOf.WaterMovingShallow
        || terrain == TerrainDefOf.WaterMovingChestDeep)
    {
        return Math.Min(baseRate + 1, FlowingWater.MaxVolume);
    }
    
    // Marsh absorbs slowly
    if (terrain == TerrainDefOf.Marsh)
    {
        return Math.Max(1, baseRate / 2);
    }
    
    return baseRate;
}
```

#### 5. Prevent spawn on vanilla water

**Modify the "empty cell expansion" block (line ~651):**
```csharp
// If we found an empty cell, create new water
if (emptyIndex >= 0)
{
    Map destMap = targetMaps[emptyIndex] ?? Map;
    IntVec3 destCell = targetCells[emptyIndex];
    
    // ❌ Do NOT spawn FlowingWater on vanilla water terrain (it's a sink, handled above)
    if (IsVanillaWaterSink(destMap, destCell))
    {
        // Skip — this cell was already handled by sink logic or will be next tick
        // Fall through to transfer-to-existing pass
    }
    else
    {
        // ... existing spawn logic ...
    }
}
```

#### 6. Update `IsCellPassableForWater()` — Add missing terrain types

```csharp
private bool IsCellPassableForWater(IntVec3 cell)
{
    if (!cell.Walkable(Map))
    {
        TerrainDef t = Map.terrainGrid.TerrainAt(cell);
        if (t != null && IsVanillaWaterTerrain(Map, cell))
        {
            return true;
        }
        return false;
    }
    return true;
}
```

#### 7. New settings fields

```csharp
// Vanilla water integration
public bool vanillaWaterAbsorptionEnabled = true;   // Default: enabled
public int vanillaWaterAbsorptionRate = 1;           // Base units absorbed per check (1-7)
public bool vanillaWaterPreventSpawn = true;         // Prevent FlowingWater spawn on vanilla water
```

#### 8. Settings UI

Add to the "General" or new "Water Interaction" tab:
- Checkbox: "Natural water bodies absorb mod water"
- Slider: "Absorption rate (1-7)"
- Checkbox: "Prevent spawning on natural water"

---

## Files to Modify

| File | Changes |
|------|---------|
| `FlowingWater.cs` | New helper methods, sink logic in `AttemptLocalDiffusion()`, update `IsCellPassableForWater()` |
| `GameComponent_WaterDiffusion.cs` | Update `IsCellPassableForWater()` to match |
| `WaterSpringModSettings.cs` | 3 new settings + `ExposeData` + `ClampAndSanitize` |
| `WaterSpringModMain.cs` | Settings UI widgets |

---

## Decision Points (Pending)

### DP-1: Should vanilla water cells EVER have FlowingWater on them?
- **Option A (Recommended):** Never. Vanilla water is always a sink. Clean and simple.
- **Option B:** Allow it but with different behavior (FlowingWater on vanilla water = "overflow" that drains faster).
- **Implication:** If option A, the current "expansion into vanilla water" behavior becomes entirely replaced by sink drain.

### DP-2: Absorption priority vs. downward flow priority
- When FlowingWater is adjacent to both a vanilla water sink AND a stair/hole going down, which takes priority?
- **Option A:** Gravity first (down > sink > lateral). Consistent with physics.
- **Option B:** Sink first (water naturally flows into bodies of water). More intuitive for rivers.
- **Option C:** Equal priority (random selection from all valid targets). Simplest.
- **Recommendation:** Option A — maintain existing priority order. Sinks go between vertical portals and empty-cell expansion.

### DP-3: Should water also COME from vanilla water bodies?
- Currently: No. Vanilla water is static terrain, not a source.
- **Option A (MVP):** No — vanilla water is only a sink, never a source. Keep it simple.
- **Option B (Future):** Add a "Water Intake" building that draws from adjacent vanilla water (pairs well with Pump in Issue #003).
- **Recommendation:** Option A for now. Option B as separate future feature.

### DP-4: Terrain types — complete list
RimWorld 1.5/1.6 vanilla water terrain types that exist in `TerrainDefOf`:
- `WaterShallow`
- `WaterDeep`  
- `WaterMovingShallow`
- `WaterMovingChestDeep`
- `WaterOceanShallow`
- `WaterOceanDeep`
- `Marsh`
- `MarshyTerrain` (?)
- `Ice` (frozen water — should this count?)

**Questions:**
- Should modded water terrains (from other mods) also be treated as sinks?
- Should we check `terrain.IsWater` property instead of hardcoding types? (more robust, catches mod-added water)
- What about `Bridge` over water? (has vanilla water underneath but is walkable)

### DP-5: What happens to FlowingWater already ON vanilla water in existing saves?
- If `vanillaWaterPreventSpawn` is enabled, existing FlowingWater on vanilla water from before the update will remain.
- **Option A:** Do nothing — they'll eventually evaporate or stabilize.
- **Option B:** On game load, scan and remove FlowingWater on vanilla water cells.
- **Option C:** Let them drain naturally via the sink system (they'll reduce volume until destroyed).
- **Recommendation:** Option C — cleanest migration path.

---

## Open Questions

1. **Does `TerrainDef` have a `.IsWater` or `.isWater` bool property?** If yes, using it instead of hardcoding all defs would be far more robust and compatible with terrain mods. Need to verify in RimWorld source/docs.

2. **Marsh behavior:** Marsh terrain is shallow muddy water. Should it act as a full sink, a partial sink (slower absorption), or not a sink at all (just passable)?

3. **Ocean edge interaction:** Ocean water extends to map edges. If FlowingWater reaches ocean, draining into it is correct. But what about the visual — will there be a visible "edge" between mod water and ocean? Might need visual blending.

4. **Save compatibility:** The new settings default to `true` (absorption enabled). This changes behavior for existing saves where water may have already spread across vanilla water. Is a one-time cleanup needed, or is gradual drain OK?

5. **TrySyncTerrainToVolume interaction:** If we prevent spawning FlowingWater on vanilla water, we also prevent `TrySyncTerrainToVolume` from overwriting natural terrain. This is correct behavior, but verify there are no edge cases where FlowingWater is spawned by other code paths (e.g., `Building_WaterSpring.SpawnFlowingWater()` — what if the spring is ON vanilla water?).

6. **Performance:** The sink check adds a terrain lookup per valid cell. `Map.terrainGrid.TerrainAt()` is O(1) (array access), so this should be negligible. But confirm there's no deferred terrain grid on MF upper levels that might be slower.

---

## Test Scenarios

| # | Scenario | Expected Behavior |
|---|----------|-------------------|
| T1 | FlowingWater reaches river bank | Water drains into river, no FlowingWater spawns on river cells |
| T2 | FlowingWater reaches deep lake | Faster absorption rate (2x base) |
| T3 | FlowingWater reaches marsh | Slower absorption (half rate) |
| T4 | FlowingWater reaches ocean edge | Full absorption, no visual artifacts |
| T5 | Spring placed ON shallow water | Spring still functions; FlowingWater spawns at spring but adjacent vanilla water acts as sink |
| T6 | Spring placed NEXT TO river | Water spawns, flows toward river, drains into it naturally |
| T7 | Disable absorption in settings | Revert to current behavior (water expands over vanilla water) |
| T8 | Existing save with FlowingWater on river | FlowingWater gradually drains via sink system |
| T9 | Vanilla water + MF hole below | Gravity (hole) takes priority over sink |
| T10 | Moving water (river) | Higher absorption rate than still water |
| T11 | Bridge over water | Water passes through bridge? Or bridge blocks it? |
| T12 | Frozen water (ice) | Should NOT be a sink — ice blocks flow |

---

## Visual Mockup (ASCII)

```
Before (current behavior):
  S = Spring, F = FlowingWater, ~ = vanilla river

  [S][F][F][F][~+F][~+F][~+F]    ← FlowingWater spawns ON river tiles
                                    Volume stacks absurdly

After (with sink system):
  [S][F][F][F][~][~][~]          ← Water reaches river and drains
  volume:     7  5  3  1           ← Volume decreases toward sink
                   └── absorbed    ← No FlowingWater on river tiles
```

---

## References

- `FlowingWater.cs` — lines 441-800 (`AttemptLocalDiffusion`), 941-955 (`IsCellPassableForWater`)
- `GameComponent_WaterDiffusion.cs` — line ~490 (`IsCellPassableForWater`)
- `WaterSpringModSettings.cs` — all settings fields
- `WaterSpringModMain.cs` — settings UI
- RimWorld wiki: [Terrain](https://rimworldwiki.com/wiki/Terrain) for full terrain type list
