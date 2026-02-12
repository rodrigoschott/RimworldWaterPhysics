# Issue #005: Floodgate Building (Water Flow Control)

**Status:** Planned  
**Priority:** 🔴 High  
**Complexity:** ⭐⭐⭐ (Medium)  
**Estimated Hours:** 10-16h  
**Dependencies:** None  
**Inspired by:** Dwarf Fortress Floodgate + Lever system  
**Created:** 2026-02-11  

---

## Problem Statement

There's no way to **stop and start** water flow on demand. Players can place walls (permanent) or channels (directional, Issue #002), but nothing that toggles. In DF, floodgates linked to levers are fundamental to every water system — dams, reservoirs, flood defenses, cistern fill valves.

---

## DF Reference

- **Floodgate:** Building that toggles between wall (closed, blocks all flow) and open (passable)
- **Linked to levers:** Player pulls lever → floodgate opens/closes
- **Hatches:** Same concept but vertical (over stairs/channels, blocks vertical flow)
- **Doors:** Similar but can be opened by pawns walking through
- **Source:** [DF2014:Floodgate](https://dwarffortresswiki.org/index.php/DF2014:Floodgate)

---

## Design

### Building: `WS_Floodgate`

A 1x1 building that **blocks water flow when closed**, allows it when open. Toggled manually or via power.

| State | Water Flow | Pawn Passage | fillPercent |
|-------|:---:|:---:|:---:|
| **Open** | ✅ Passes | ✅ Walks through | 0.0 |
| **Closed** | ❌ Blocked | ❌ Impassable | 1.0 |

### Toggle Mechanism

RimWorld doesn't have DF-style levers, but has:
- **CompFlickable** — manual on/off toggle (colonist walks over and flips)
- **Power-gated** — opens only when powered (CompPowerTrader)
- **Manual gizmo** — player clicks button in UI

**Recommended:** CompFlickable (simplest, no power needed). Future: power-gated variant.

### Implementation

```csharp
public class Building_WaterFloodgate : Building
{
    private CompFlickable flickComp;
    public bool IsOpen => flickComp?.SwitchIsOn ?? true;
    
    // Dynamic fillPercent based on state
    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        flickComp = GetComp<CompFlickable>();
        UpdatePassability();
    }
    
    public override void Tick()
    {
        base.Tick();
        // Check if state changed
        bool open = IsOpen;
        if (lastState != open)
        {
            lastState = open;
            UpdatePassability();
            // Notify water system
            var dm = Current.Game?.GetComponent<GameComponent_WaterDiffusion>();
            dm?.NotifyTerrainChanged(Map, Position);
        }
    }
    
    private void UpdatePassability()
    {
        // RimWorld doesn't support dynamic fillPercent easily
        // Alternative: check in FlowingWater's passability check
    }
}
```

**Integration in `FlowingWater.AttemptLocalDiffusion()`:**

```csharp
// After edifice check, before adding to validCells:
Building_WaterFloodgate gate = Map.thingGrid.ThingAt<Building_WaterFloodgate>(adjacentCell);
if (gate != null && !gate.IsOpen)
{
    if (debug) WaterSpringLogger.LogDebug($"[Floodgate] Blocked at {adjacentCell}");
    continue; // Water cannot pass closed floodgate
}
```

### Performance Impact

Minimal. One additional `ThingAt<>` check per cardinal neighbor per diffusion tick. Same O(1) cost as existing edifice check. **No BFS, no pathfinding, no allocation.**

---

## Variant: `WS_Hatch` (Vertical Flow Control)

Same concept but for vertical flow — placed over `WS_Hole` to block/allow water falling through.

```xml
<defName>WS_Hatch</defName>
<label>water hatch</label>
<description>A hatch cover that blocks vertical water flow through holes when closed.</description>
```

Integration in `VerticalPortalBridge.IsHoleAt()`:
```csharp
// Check for hatch over hole
Building_WaterFloodgate hatch = map.thingGrid.ThingAt<Building_WaterFloodgate>(cell);
if (hatch != null && !hatch.IsOpen) return false; // Hatch blocks the hole
```

---

## Files to Create

| File | Description |
|------|-------------|
| `Building_WaterFloodgate.cs` | Building class with CompFlickable toggle |
| `Defs/ThingDefs/Buildings_WaterFloodgate.xml` | ThingDef for floodgate + hatch |
| `Textures/Things/Building/WS_Floodgate.png` | Floodgate texture (open/closed states) |
| `Textures/Things/Building/WS_Hatch.png` | Hatch texture |

## Files to Modify

| File | Changes |
|------|---------|
| `FlowingWater.cs` | Floodgate check in cardinal neighbor loop |
| `VerticalPortalBridge.cs` | Hatch check in `IsHoleAt()` |
| `GameComponent_WaterDiffusion.cs` | Same floodgate check in `IsCellPassableForWater()` |

---

## Decision Points

### DP-1: Toggle mechanism
- **CompFlickable (recommended):** Manual toggle, colonist walks over. Simple, no power.
- **Power-gated:** Opens when powered, closes on power loss. More "automatic" but requires electricity.
- **Both:** Base = flickable, variant `WS_FloodgatePowered` = power-gated. Future.

### DP-2: Can water destroy a closed floodgate?
- **No (MVP):** Floodgate is indestructible by water. Simple.
- **Yes (future):** High pressure (7/7 on multiple sides) damages the gate over time. Gameplay depth.

### DP-3: Pawn passage when open
- **Option A:** Passable when open (pawns walk through). More useful.
- **Option B:** Always impassable (water-only passage). Simpler but less intuitive.
- **Recommendation:** Option A.

### DP-4: Visual feedback
- Should the texture change between open/closed? Yes — critical for UX.
- Use `Graphic_Multi` with state-based texture swap, or overlay icon.

---

## Test Scenarios

| # | Scenario | Expected |
|---|----------|----------|
| T1 | Floodgate closed, water approaches | Water stops at gate |
| T2 | Floodgate opens | Water flows through, neighbors reactivate |
| T3 | Floodgate closes while water flowing | Water stops; tiles downstream evaporate eventually |
| T4 | Hatch over hole, closed | Water doesn't fall through hole |
| T5 | Hatch opens | Water falls through to level below |
| T6 | Floodgate + channel | Works together (gate blocks channel flow) |
| T7 | Floodgate + pressure (Issue #004) | Pressure BFS stops at closed gate |
| T8 | Save/load with gate state | Open/closed preserved |

---

## References

- [DF2014:Floodgate](https://dwarffortresswiki.org/index.php/DF2014:Floodgate)
- `FlowingWater.cs` — cardinal neighbor loop
- `VerticalPortalBridge.cs` — `IsHoleAt()`
- `Building_WSHole.cs` — building pattern reference
