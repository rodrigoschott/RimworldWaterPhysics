# Issue #002: Water Channel / Aqueduct System

**Status:** Planned  
**Priority:** Medium  
**Complexity:** ⭐⭐⭐ (Medium)  
**Estimated Hours:** 12-20h  
**Dependencies:** None (but benefits from Issue #001 for river integration)  
**Created:** 2026-02-11  

---

## Problem Statement

Without MultiFloors, the mod's water spreads equally in all 4 cardinal directions. There's no way to **guide water** in specific directions. Players need to build walls to create corridors for water, which:

1. **Looks ugly** — walls aren't water channels, they're walls
2. **Blocks pawns** — walls are impassable, so pawns can't cross the water path
3. **Wastes resources** — building full walls just to guide water is expensive
4. **No overflow mechanic** — real channels overflow when full; walls never do

A purpose-built **water channel** building would solve all of this, adding strategic depth to water management without requiring MultiFloors.

---

## Design Concept

### Core Mechanics

A **Water Channel** (`WS_Channel`) is a 1x1 building with rotation that:

- **Restricts water flow to 2 directions** (along its axis: N↔S or E↔W)
- **When volume reaches 7/7 (MaxVolume):** Overflow — flow opens to all 4 directions
- **Pawns can walk through** (`PassThroughOnly` with low pathCost)
- **Water still follows normal diffusion rules** within the allowed directions
- **Visually:** A trench/gutter in the ground

### Overflow Behavior

```
Volume 1-6:  Flow restricted to channel axis only
             ╔═══╗
        ← ── ║ ≈ ║ ── →     (E↔W channel)
             ╚═══╝
             
Volume 7:    Overflow! All 4 directions open
               ↑
             ╔═══╗
        ← ── ║ ≈ ║ ── →     (water spills over sides)
             ╚═══╝
               ↓
```

### Variants (Planned)

| Variant | defName | Description |
|---------|---------|-------------|
| **Channel** | `WS_Channel` | Basic 2-direction channel (MVP) |
| **Junction** | `WS_ChannelJunction` | 4-way intersection (always allows all 4 dirs) |
| **Gate** | `WS_ChannelGate` | Openable/closable (blocks/allows flow, like a door for water) |
| **Covered Channel** | `WS_ChannelCovered` | Roofed variant (prevents evaporation) |

**MVP scope:** Only `WS_Channel` and optionally `WS_ChannelJunction`.

---

## Current Code Analysis

### How flow direction is determined

**`FlowingWater.AttemptLocalDiffusion()` — Cardinal direction scan (line ~445-600):**

```csharp
foreach (IntVec3 neighbor in GenAdj.CardinalDirections)
{
    IntVec3 adjacentCell = pos + neighbor;
    if (!adjacentCell.InBounds(Map)) continue;
    
    // ... stair check, elevator check, hole check ...
    
    // Non-void neighbor: require passability on this map
    if (!IsCellPassableForWater(adjacentCell)) continue;
    
    // Check for solid buildings
    Building ed = adjacentCell.GetEdifice(Map);
    if (ed != null && ed.def != null && ed.def.fillPercent > 0.1f) continue;
    
    // Store valid cell
    validCells[validCount] = adjacentCell;
    // ...
    validCount++;
}
```

**Key observation:** There's NO direction filtering. Every cardinal neighbor that's passable and not blocked by a building is added to `validCells`. The channel needs to intervene here.

### How buildings are detected

The existing code checks `adjacentCell.GetEdifice(Map)` and skips cells with `fillPercent > 0.1f`. A channel would need `fillPercent = 0` (or very low) so water still considers it passable.

### Where to inject channel logic

**Two injection points needed:**

1. **Source cell check:** When FlowingWater is ON a channel, filter which directions are valid
2. **Destination cell check:** When FlowingWater is ADJACENT to a channel, determine if it can enter from that direction

---

## Proposed Implementation

### Phase 1: Building Definition (XML)

```xml
<!-- Defs/ThingDefs/Buildings_WaterChannel.xml -->
<Defs>
  <ThingDef ParentName="BuildingBase">
    <defName>WS_Channel</defName>
    <label>water channel</label>
    <description>A carved trench that guides water flow in a specific direction. 
Water flows along the channel's axis. If the channel fills completely (7/7), 
water overflows to all sides.</description>
    <thingClass>WaterSpringMod.WaterSpring.Building_WaterChannel</thingClass>
    <category>Building</category>
    <graphicData>
      <texPath>Things/Building/WS_Channel</texPath>
      <graphicClass>Graphic_Multi</graphicClass> <!-- 4 rotations -->
      <drawSize>(1,1)</drawSize>
    </graphicData>
    <altitudeLayer>FloorEmplacement</altitudeLayer> <!-- Below buildings -->
    <passability>PassThroughOnly</passability>
    <pathCost>14</pathCost> <!-- Slightly slows pawns, like walking through a shallow trench -->
    <fillPercent>0.0</fillPercent> <!-- Does NOT block water passability check -->
    <rotatable>true</rotatable>
    <statBases>
      <MaxHitPoints>100</MaxHitPoints>
      <WorkToBuild>400</WorkToBuild>
      <Flammability>0</Flammability>
      <Beauty>-2</Beauty>
      <Mass>5</Mass>
    </statBases>
    <size>(1,1)</size>
    <designationCategory>Structure</designationCategory> <!-- Same category as WS_Hole -->
    <costList>
      <Steel>10</Steel>
    </costList>
    <constructEffect>ConstructMetal</constructEffect>
    <terrainAffordanceNeeded>Light</terrainAffordanceNeeded>
    <building>
      <isEdifice>false</isEdifice> <!-- Does NOT count as an edifice -->
      <canPlaceOverImpassablePlant>false</canPlaceOverImpassablePlant>
      <ai_chillDestination>false</ai_chillDestination>
    </building>
  </ThingDef>
  
  <ThingDef ParentName="BuildingBase">
    <defName>WS_ChannelJunction</defName>
    <label>water channel junction</label>
    <description>A junction where water channels meet, allowing flow in all four directions. 
Useful for creating intersections in water channel networks.</description>
    <thingClass>WaterSpringMod.WaterSpring.Building_WaterChannel</thingClass>
    <category>Building</category>
    <graphicData>
      <texPath>Things/Building/WS_ChannelJunction</texPath>
      <graphicClass>Graphic_Single</graphicClass> <!-- No rotation needed -->
      <drawSize>(1,1)</drawSize>
    </graphicData>
    <altitudeLayer>FloorEmplacement</altitudeLayer>
    <passability>PassThroughOnly</passability>
    <pathCost>14</pathCost>
    <fillPercent>0.0</fillPercent>
    <rotatable>false</rotatable>
    <statBases>
      <MaxHitPoints>120</MaxHitPoints>
      <WorkToBuild>500</WorkToBuild>
      <Flammability>0</Flammability>
      <Beauty>-2</Beauty>
      <Mass>8</Mass>
    </statBases>
    <size>(1,1)</size>
    <designationCategory>Structure</designationCategory>
    <costList>
      <Steel>15</Steel>
    </costList>
    <constructEffect>ConstructMetal</constructEffect>
    <terrainAffordanceNeeded>Light</terrainAffordanceNeeded>
    <building>
      <isEdifice>false</isEdifice>
      <canPlaceOverImpassablePlant>false</canPlaceOverImpassablePlant>
      <ai_chillDestination>false</ai_chillDestination>
    </building>
  </ThingDef>
</Defs>
```

### Phase 2: Building Class (C#)

```csharp
// Building_WaterChannel.cs
namespace WaterSpringMod.WaterSpring
{
    public class Building_WaterChannel : Building
    {
        /// <summary>
        /// Is this channel a junction (allows all 4 directions)?
        /// Check defName to determine variant.
        /// </summary>
        public bool IsJunction => def.defName == "WS_ChannelJunction";
        
        /// <summary>
        /// Returns allowed flow directions based on rotation.
        /// North/South rotation: allows N and S
        /// East/West rotation: allows E and W
        /// Junction: all 4
        /// </summary>
        public bool AllowsDirection(IntVec3 direction)
        {
            if (IsJunction) return true;
            
            // Rot4: North=0, East=1, South=2, West=3
            // N/S channel (rotated N or S): allows North and South flow
            // E/W channel (rotated E or W): allows East and West flow
            if (Rotation == Rot4.North || Rotation == Rot4.South)
            {
                // N/S axis
                return direction == IntVec3.North || direction == IntVec3.South;
            }
            else // East or West
            {
                // E/W axis
                return direction == IntVec3.East || direction == IntVec3.West;
            }
        }
    }
}
```

### Phase 3: Flow Logic Integration

**New static helper method (in FlowingWater.cs or new utility class):**

```csharp
/// <summary>
/// Check if water flow from sourceCell in the given direction is allowed by channels.
/// Returns true if flow is permitted, false if blocked by channel direction rules.
/// 
/// Rules:
/// 1. If source cell has a channel: only allow flow along channel axis (unless overflow at MaxVolume)
/// 2. If destination cell has a channel: only allow entry along channel axis
/// 3. If neither has a channel: always allow (normal behavior)
/// </summary>
private bool IsFlowAllowedByChannel(IntVec3 sourceCell, IntVec3 direction, int sourceVolume)
{
    Map map = this.Map;
    
    // Check: does SOURCE cell have a channel?
    Building_WaterChannel sourceChannel = map.thingGrid.ThingAt<Building_WaterChannel>(sourceCell);
    if (sourceChannel != null && !sourceChannel.IsJunction)
    {
        // Source is on a channel — check direction
        bool overflow = sourceVolume >= FlowingWater.MaxVolume;
        if (!overflow && !sourceChannel.AllowsDirection(direction))
        {
            return false; // Blocked: not in channel axis and not overflowing
        }
    }
    
    // Check: does DESTINATION cell have a channel?
    IntVec3 destCell = sourceCell + direction;
    if (destCell.InBounds(map))
    {
        Building_WaterChannel destChannel = map.thingGrid.ThingAt<Building_WaterChannel>(destCell);
        if (destChannel != null && !destChannel.IsJunction)
        {
            // Destination is a channel — can only enter along its axis
            // (direction from source to dest must align with channel axis)
            if (!destChannel.AllowsDirection(direction))
            {
                return false; // Blocked: trying to enter channel from the side
            }
        }
    }
    
    return true; // No channel restrictions
}
```

**Integration point in `AttemptLocalDiffusion()` — inside the cardinal direction loop:**

```csharp
foreach (IntVec3 neighbor in GenAdj.CardinalDirections)
{
    IntVec3 adjacentCell = pos + neighbor;
    if (!adjacentCell.InBounds(Map)) continue;
    
    // NEW: Channel direction filter
    if (!IsFlowAllowedByChannel(pos, neighbor, this.Volume))
    {
        if (debug)
            WaterSpringLogger.LogDebug($"[Channel] Flow blocked {pos} → {adjacentCell}: not aligned with channel axis");
        continue;
    }
    
    // ... rest of existing logic (stairs, elevators, holes, passability) ...
}
```

### Phase 4: Settings

```csharp
// In WaterSpringModSettings.cs
public bool channelFlowRestrictionEnabled = true;  // Default: channels restrict flow
public int channelOverflowVolume = 7;               // Volume at which channels overflow (1-7, default MaxVolume)
```

### Phase 5: Textures

Minimum viable textures needed:

| Asset | Description | Variants |
|-------|-------------|----------|
| `Things/Building/WS_Channel_north.png` | Channel texture (N/S axis) | 64x64 or 128x128 |
| `Things/Building/WS_Channel_east.png` | Channel texture (E/W axis) | Same |
| `Things/Building/WS_Channel_south.png` | Reuse north (mirrored) | Auto |
| `Things/Building/WS_Channel_west.png` | Reuse east (mirrored) | Auto |
| `Things/Building/WS_ChannelJunction.png` | 4-way junction (no rotation) | 64x64 or 128x128 |

**Visual concept:** Parallel grooves in the ground with slight depth shading. Could start with placeholder (colored rectangle) and refine later.

**Alternative:** Use `Graphic_Multi` which auto-handles 4 rotations from a single base texture + naming convention (`_north`, `_east`, `_south`, `_west`).

---

## Files to Create

| File | Type | Description |
|------|------|-------------|
| `Source/WaterSpringMod/WaterSpring/Building_WaterChannel.cs` | C# | Building class with direction logic |
| `Defs/ThingDefs/Buildings_WaterChannel.xml` | XML | ThingDef for channel and junction |
| `Textures/Things/Building/WS_Channel_north.png` | PNG | Channel texture (N/S) |
| `Textures/Things/Building/WS_Channel_east.png` | PNG | Channel texture (E/W) |
| `Textures/Things/Building/WS_ChannelJunction.png` | PNG | Junction texture |
| `Languages/English/DefInjected/ThingDef/Buildings_WaterChannel.xml` | XML | Localization (optional, can use inline) |

## Files to Modify

| File | Changes |
|------|---------|
| `FlowingWater.cs` | `IsFlowAllowedByChannel()` helper + call in `AttemptLocalDiffusion()` cardinal loop |
| `WaterSpringModSettings.cs` | 2 new settings + `ExposeData` + `ClampAndSanitize` |
| `WaterSpringModMain.cs` | Settings UI for channel options |

---

## Decision Points (Pending)

### DP-1: Building vs. Terrain approach

- **Option A (Recommended): Building** — `Building_WaterChannel` as a placeable building
  - ✅ Can detect with `ThingAt<Building_WaterChannel>()` (fast, typed)
  - ✅ Has rotation built-in (`Rot4`)
  - ✅ Can be constructed/deconstructed by pawns
  - ✅ Follows same pattern as `WS_Hole`
  - ❌ Might conflict with FlowingWater on same cell (both are Things)
  
- **Option B: Terrain** — Custom `TerrainDef` for channels
  - ✅ No Thing stacking issues
  - ✅ Visually "part of the floor" (more natural)
  - ❌ Terrain has no rotation in vanilla (would need workaround)
  - ❌ Can't detect terrain type as easily (string comparison vs. type check)
  - ❌ Terrain placement UI is different from building placement

- **Option C: Floor overlay** — Like a floor tile but with metadata
  - ❌ RimWorld floors don't carry rotation or custom data easily

### DP-2: `isEdifice` — true or false?

- `isEdifice = true`: Channel would be THE edifice on that cell. No other edifice (wall, door, etc.) could coexist. This prevents players from building a wall on a channel cell.
- `isEdifice = false` (recommended): Channel is like furniture. Could coexist with an edifice. More flexible but potentially weird combos (wall + channel?).
- **Recommendation:** `false` — treat it like furniture that sits on the floor.

### DP-3: Can FlowingWater and Building_WaterChannel coexist on the same cell?

- **Must yes** — the whole point is water flows THROUGH the channel.
- RimWorld allows multiple things on a cell as long as they don't conflict.
- `FlowingWater` is `category = Item` (or similar), channel is `category = Building`.
- **Need to verify:** Does the existing `GetEdifice` check in `AttemptLocalDiffusion` skip the channel? Yes, because `fillPercent = 0.0`.

### DP-4: Overflow threshold — always MaxVolume or configurable?

- **Option A:** Always 7 (MaxVolume) — simple, intuitive
- **Option B:** Configurable per-channel (requires CompProperties) — overengineered for MVP
- **Option C:** Global setting `channelOverflowVolume` (1-7) — middle ground
- **Recommendation:** Option C (global setting, default 7)

### DP-5: Channel + MultiFloors interaction

- On MF upper levels, channels should still work (restrict directions).
- If water is on a channel AND adjacent to a stair/hole: which takes priority?
- **Proposed:** Vertical portals (stairs/holes/elevators) bypass channel direction restrictions. Gravity > channel walls.
- **Rationale:** A channel on a cliff edge would still let water fall down.

### DP-6: Channel pathfinding cost for pawns

- `pathCost = 0`: pawns walk through freely (channel is invisible to pathfinding)
- `pathCost = 14` (~1 extra cell): slight penalty, nudges pawns to avoid channels
- `pathCost = 50` (like WaterSpring): significant penalty
- **Recommendation:** `pathCost = 14` (slight avoidance, not blocking)

### DP-7: Can water enter a channel from a non-channel cell?

- Example: FlowingWater at (5,5), Channel at (5,6) oriented E↔W.
  - Water tries to flow North from (5,5) to (5,6).
  - Direction is North, but channel is E↔W — perpendicular.
  - **Block or allow?**
- **Option A (Strict):** Block — water can only enter a channel from its open ends. Realistic but potentially frustrating.
- **Option B (Lenient):** Allow — once water enters, it's then restricted. Easier to use.
- **Recommendation:** Option A (strict). This makes channels behave like real aqueducts with entry points.

### DP-8: What happens when a channel is destroyed while containing water?

- The FlowingWater on that cell suddenly has no channel → flow opens to 4 directions.
- **Desired:** Water "breaks free" and flows in all directions (like a dam breaking).
- **Implementation:** Channel despawn triggers `NotifyTerrainChanged` → reactivates neighbors → water redistibutes naturally.
- **Already handled:** `HarmonyPatches.Thing_DeSpawn_Prefix` catches building removal and calls `NotifyTerrainChanged`.

---

## Open Questions

1. **altitudeLayer priority:** If channel is `FloorEmplacement` and FlowingWater is `Item`, will the channel render BELOW the water overlay? Need to test visual stacking.

2. **Graphic_Multi vs Graphic_Single:** `Graphic_Multi` expects `_north`, `_east`, `_south`, `_west` texture variants. For a simple channel, maybe only 2 unique textures (N↔S and E↔W), with south=north and west=east. Verify RimWorld's auto-mirroring behavior.

3. **Blueprint placement:** When placing a channel, can we show a direction arrow in the ghost? RimWorld's `Rot4` placement already rotates the ghost, which should suffice.

4. **Designation dropdown:** Should channels go in a dropdown group with WS_Hole? (`DesignatorDropdownGroupDef`) This keeps the architect menu clean.

5. **Research requirement:** Should channels require a research project? Probably not for MVP (they're simple structures), but could add one later for balancing.

6. **Material variants:** Stone channels? Wood channels? Different costs and HP? Future feature, not MVP.

7. **Water visual inside channel:** When FlowingWater is on a channel cell, should it render differently? E.g., narrower water strip following the channel axis? This would be beautiful but requires custom drawing code.

8. **Channel + evaporation:** Should covered channels prevent evaporation? If `WS_ChannelCovered` is future work, the MVP `WS_Channel` is open-air and subject to normal evaporation.

---

## Test Scenarios

| # | Scenario | Expected |
|---|----------|----------|
| T1 | Place E↔W channel, water approaches from East | Water enters and flows through to West |
| T2 | Place E↔W channel, water approaches from North | Water CANNOT enter (perpendicular) |
| T3 | Channel at vol 7 (max) | Overflow: water spills to all 4 directions |
| T4 | Channel at vol 6, then source adds 1 | Vol becomes 7, overflow activates |
| T5 | Three channels in a row (E↔W) | Water flows through all three as aqueduct |
| T6 | L-shaped turn: E↔W channel → junction → N↔S channel | Water navigates the turn |
| T7 | Channel destroyed while full | Water bursts to all 4 directions |
| T8 | Pawn pathfinding through channel | Pawn walks through with slight slowdown |
| T9 | Channel next to stair (MF) | Stair takes priority, water goes vertical |
| T10 | Channel on MF upper level | Channel works normally on upper levels |
| T11 | Channel with evaporation enabled | Low-volume water on open channel evaporates normally |
| T12 | Junction with 4 connecting channels | Water flows freely in all directions through junction |
| T13 | Channel + vanilla water sink (Issue #001) | Water in channel reaches river and drains |
| T14 | Save/load with channels | Channel rotation and water volume preserved |

---

## Visual Mockup (ASCII)

```
Legend: [C→] = Channel E↔W, [C↑] = Channel N↔S, [J] = Junction, [S] = Spring, ≈ = water

Simple aqueduct:
  [S] [C→] [C→] [C→] [C→] [C→] [~river~]
   7    6    5    4    3    2      → drain

L-shaped channel:
  [S] [C→] [C→] [J]
                   [C↑]
                   [C↑]
                   [C↑]
                   [~lake~] → drain

Overflow scenario (volume 7 everywhere):
                   ↑ overflow
  [S] [C→]≈≈≈[C→]≈≈≈[C→]  → continues
                   ↓ overflow
```

---

## References

- `FlowingWater.cs` — lines 445-600 (cardinal direction loop in `AttemptLocalDiffusion`)
- `Building_WSHole.cs` — building pattern to follow
- `Buildings_WaterHole.xml` — XML def pattern to follow
- `HarmonyPatches.cs` — already handles building spawn/despawn notifications
- RimWorld modding wiki: [Graphic_Multi](https://rimworldwiki.com/wiki/Modding_Tutorials/Graphic_Multi)
