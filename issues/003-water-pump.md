# Issue #003: Water Pump Building

**Status:** Planned  
**Priority:** Medium  
**Complexity:** ⭐⭐⭐⭐ (Medium-High)  
**Estimated Hours:** 15-25h  
**Dependencies:** Benefits from Issue #002 (channels) for directed flow, but not required  
**Created:** 2026-02-11  

---

## Problem Statement

Water in the mod only flows via gravity and diffusion. There's no way to:

1. **Move water upward** to higher MultiFloors levels against gravity
2. **Push water in a specific direction** (e.g., from a low area to a high one on the same level)
3. **Create active water circulation systems** (pump from source → distribute → drain)
4. **Extract water from natural bodies** (rivers, lakes) into the mod's FlowingWater system

Without a pump, water management is entirely passive. Players can only place springs, holes, and channels — they can't actively move water where they want it.

---

## MultiFloors Pipe System Analysis

The decompiled MultiFloors code shows `PipeConnectorUtility` — but this is **NOT a fluid pump**:

```csharp
// PipeConnectorUtility.cs (decompiled)
public static IEnumerable<Command> PipeConnectorOptionsForLevel(
    Map destMap, Thing connector, Func<bool> linked, Action onLinked, bool isUpper)
```

This is a **UI gizmo system** for linking heat/gas/power connectors between levels. It provides:
- "Build Upper/Lower Pipe Connector" gizmo buttons
- "Link Pipe Connector" gizmo for manual linking
- "Stop Transmission" toggle
- "Export/Import Mode" toggle

**Key insight:** MF's pipe connectors are for **passive equalization** (heat, gas) between levels — NOT for pumping fluids. Water physics is directional (gravity), so we need our own active pump system.

**However:** We can potentially use MF's `PipeConnectorUtility` UI patterns as inspiration for our pump's level-linking gizmo UI.

---

## Design Concept

### Building: `WS_WaterPump`

A powered 1x1 building that actively transfers water from its **intake side** (back) to its **output side** (front).

```
Orientation: pump faces EAST (Rot4.East)

        [intake] → [PUMP] → [output]
           W          ★         E
           
  Water is PULLED from intake cell
  Water is PUSHED to output cell
  Requires electricity (CompPower)
```

### Two Operating Modes

#### Mode 1: Horizontal Pump (default, always available)
- Pulls water from the cell behind the pump (intake)
- Pushes water to the cell in front (output)
- **Ignores normal diffusion rules** — can push water "uphill" against volume gradient
- Useful for: directing water flow, draining flooded areas, filling reservoirs

#### Mode 2: Vertical Pump (only with MultiFloors)
- Pulls water from the pump's cell on the current level
- Pushes water to the **same cell on the level above**
- Essentially a "reverse hole" — lifts water against gravity
- Useful for: multi-story water systems, rooftop gardens, upper floor defense flooding

### Power & Rate

| Property | Default | Range |
|----------|---------|-------|
| Power consumption | 100W | Fixed (or configurable) |
| Transfer rate | 1 unit/cycle | Setting: 1-3 |
| Cycle interval | 60 ticks (1 sec) | Setting: 30-120 ticks |
| Min source volume | 2 | Setting: 1-7 (won't drain below this) |

---

## Current Code Analysis

### How water transfer works

**`FlowingWater.TransferVolume()` (line ~965-1005):**
```csharp
public bool TransferVolume(FlowingWater neighbor)
{
    // Enforces minVolumeDifferenceForTransfer
    // Transfers exactly 1 unit
    // Registers both tiles for active processing
}
```

The pump would need to **bypass** `minVolumeDifferenceForTransfer` — it forces water to move regardless of volume difference.

### How cross-map transfer works

**`AttemptLocalDiffusion()` — cross-map section (line ~670-700):**
```csharp
// Manual 1-unit transfer across maps
if (this.Volume > 0)
{
    typedWater.AddVolume(1);
    this.Volume -= 1;
    // Register both on their maps
}
```

The vertical pump would use the same pattern for cross-map water movement.

### How buildings with CompPower work

RimWorld's standard pattern for powered buildings:
```csharp
// In ThingDef XML:
<comps>
    <li Class="CompProperties_Power">
        <compClass>CompPowerTrader</compClass>
        <basePowerConsumption>100</basePowerConsumption>
    </li>
    <li Class="CompProperties_Flickable" />
    <li Class="CompProperties_Breakdownable" />
</comps>

// In C# class:
CompPowerTrader powerComp;
powerComp = GetComp<CompPowerTrader>();
if (powerComp != null && !powerComp.PowerOn) return; // No power, no pump
```

---

## Proposed Implementation

### Phase 1: ThingDef (XML)

```xml
<!-- Defs/ThingDefs/Buildings_WaterPump.xml -->
<Defs>
  <ThingDef ParentName="BuildingBase">
    <defName>WS_WaterPump</defName>
    <label>water pump</label>
    <description>An electric pump that actively moves water from its intake (back) 
to its output (front). Requires power to operate.

With MultiFloors installed, the pump can also push water vertically 
to the level above.</description>
    <thingClass>WaterSpringMod.WaterSpring.Building_WaterPump</thingClass>
    <category>Building</category>
    <graphicData>
      <texPath>Things/Building/WS_WaterPump</texPath>
      <graphicClass>Graphic_Multi</graphicClass>
      <drawSize>(1,1)</drawSize>
    </graphicData>
    <altitudeLayer>Building</altitudeLayer>
    <passability>Impassable</passability>
    <fillPercent>0.5</fillPercent>
    <rotatable>true</rotatable>
    <tickerType>Normal</tickerType>
    <statBases>
      <MaxHitPoints>150</MaxHitPoints>
      <WorkToBuild>1200</WorkToBuild>
      <Flammability>0.3</Flammability>
      <Beauty>-5</Beauty>
      <Mass>25</Mass>
    </statBases>
    <size>(1,1)</size>
    <designationCategory>Structure</designationCategory>
    <costList>
      <Steel>50</Steel>
      <ComponentIndustrial>2</ComponentIndustrial>
    </costList>
    <constructEffect>ConstructMetal</constructEffect>
    <terrainAffordanceNeeded>Medium</terrainAffordanceNeeded>
    <comps>
      <li Class="CompProperties_Power">
        <compClass>CompPowerTrader</compClass>
        <basePowerConsumption>100</basePowerConsumption>
        <shortCircuitInRain>true</shortCircuitInRain>
      </li>
      <li Class="CompProperties_Flickable" />
      <li Class="CompProperties_Breakdownable" />
    </comps>
    <building>
      <isEdifice>true</isEdifice>
      <destroySound>BuildingDestroyed_Metal_Small</destroySound>
    </building>
    <researchPrerequisites>
      <li>Electricity</li>
    </researchPrerequisites>
    <minifiedDef>MinifiedThing</minifiedDef>
    <thingCategories>
      <li>BuildingsMisc</li>
    </thingCategories>
  </ThingDef>
</Defs>
```

### Phase 2: Building Class (C#)

```csharp
// Building_WaterPump.cs
using System;
using RimWorld;
using Verse;
using UnityEngine;

namespace WaterSpringMod.WaterSpring
{
    public class Building_WaterPump : Building
    {
        private CompPowerTrader powerComp;
        private CompFlickable flickComp;
        private CompBreakdownable breakdownComp;
        
        private int ticksUntilNextPump = 0;
        private int totalPumped = 0;
        
        // Mode: horizontal (default) or vertical (MF only)
        private bool verticalMode = false;
        
        // Intake and output cells (computed from rotation)
        private IntVec3 IntakeCell => Position - Rotation.FacingCell; // Behind the pump
        private IntVec3 OutputCell => Position + Rotation.FacingCell; // In front of pump
        
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = GetComp<CompPowerTrader>();
            flickComp = GetComp<CompFlickable>();
            breakdownComp = GetComp<CompBreakdownable>();
        }
        
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref verticalMode, "verticalMode", false);
            Scribe_Values.Look(ref ticksUntilNextPump, "ticksUntilNextPump", 0);
            Scribe_Values.Look(ref totalPumped, "totalPumped", 0);
        }
        
        protected override void Tick()
        {
            base.Tick();
            
            // Check power, flickable, breakdown
            if (!IsPoweredAndOperational()) return;
            
            var settings = LoadedModManager.GetMod<WaterSpringModMain>()?.settings;
            if (settings == null) return;
            
            ticksUntilNextPump--;
            if (ticksUntilNextPump <= 0)
            {
                ticksUntilNextPump = Math.Max(1, settings.pumpCycleIntervalTicks);
                
                if (verticalMode && MultiFloorsIntegration.IsAvailable)
                {
                    TryPumpVertical(settings);
                }
                else
                {
                    TryPumpHorizontal(settings);
                }
            }
        }
        
        private bool IsPoweredAndOperational()
        {
            if (powerComp != null && !powerComp.PowerOn) return false;
            if (flickComp != null && !flickComp.SwitchIsOn) return false;
            if (breakdownComp != null && breakdownComp.BrokenDown) return false;
            return true;
        }
        
        /// <summary>
        /// Horizontal pump: pull from intake cell, push to output cell.
        /// </summary>
        private void TryPumpHorizontal(WaterSpringModSettings settings)
        {
            IntVec3 intake = IntakeCell;
            IntVec3 output = OutputCell;
            
            if (!intake.InBounds(Map) || !output.InBounds(Map)) return;
            
            // Find water at intake
            FlowingWater sourceWater = Map.thingGrid.ThingAt<FlowingWater>(intake);
            if (sourceWater == null || sourceWater.Volume < settings.pumpMinSourceVolume)
                return;
            
            int transferAmount = Math.Min(settings.pumpTransferRate, sourceWater.Volume - (settings.pumpMinSourceVolume - 1));
            if (transferAmount <= 0) return;
            
            // Find or create water at output
            FlowingWater destWater = Map.thingGrid.ThingAt<FlowingWater>(output);
            
            if (destWater != null)
            {
                // Push into existing water
                int canReceive = FlowingWater.MaxVolume - destWater.Volume;
                int actual = Math.Min(transferAmount, canReceive);
                if (actual <= 0) return; // Output is full
                
                destWater.AddVolume(actual);
                sourceWater.Volume -= actual;
                OnPumped(actual, settings);
            }
            else
            {
                // Output cell is empty — check if it's valid for water
                if (!output.Walkable(Map))
                {
                    // Check if it's a vanilla water sink (Issue #001 interaction)
                    // If so, drain into it
                    return;
                }
                
                Building ed = output.GetEdifice(Map);
                if (ed != null && ed.def.fillPercent > 0.1f) return;
                
                // Spawn new FlowingWater at output
                ThingDef waterDef = DefDatabase<ThingDef>.GetNamed("FlowingWater", false);
                if (waterDef == null) return;
                
                Thing newWater = ThingMaker.MakeThing(waterDef);
                if (newWater is FlowingWater typedWater)
                {
                    typedWater.Volume = 0;
                    GenSpawn.Spawn(newWater, output, Map);
                    
                    int actual = Math.Min(transferAmount, FlowingWater.MaxVolume);
                    typedWater.AddVolume(actual);
                    sourceWater.Volume -= actual;
                    OnPumped(actual, settings);
                }
            }
        }
        
        /// <summary>
        /// Vertical pump: pull from current cell, push to same cell on level above.
        /// Only available with MultiFloors.
        /// </summary>
        private void TryPumpVertical(WaterSpringModSettings settings)
        {
            if (!MultiFloorsIntegration.IsAvailable) return;
            
            // Get the map above
            if (!MultiFloorsIntegration.TryGetUpperMap(Map, out Map upperMap)) return;
            
            IntVec3 intake = IntakeCell; // Still pull from behind
            IntVec3 destCell = Position; // Push to same position, upper level
            
            if (!intake.InBounds(Map)) return;
            if (!destCell.InBounds(upperMap)) return;
            
            // Check if upper cell is void (no foundation) — can't pump into void
            if (MultiFloorsIntegration.IsVoidTerrain(upperMap, destCell)) return;
            
            // Find water at intake
            FlowingWater sourceWater = Map.thingGrid.ThingAt<FlowingWater>(intake);
            if (sourceWater == null || sourceWater.Volume < settings.pumpMinSourceVolume)
                return;
            
            int transferAmount = Math.Min(settings.pumpTransferRate, sourceWater.Volume - (settings.pumpMinSourceVolume - 1));
            if (transferAmount <= 0) return;
            
            // Find or create water on upper level
            FlowingWater destWater = upperMap.thingGrid.ThingAt<FlowingWater>(destCell);
            
            if (destWater != null)
            {
                int canReceive = FlowingWater.MaxVolume - destWater.Volume;
                int actual = Math.Min(transferAmount, canReceive);
                if (actual <= 0) return;
                
                destWater.AddVolume(actual);
                sourceWater.Volume -= actual;
                OnPumped(actual, settings);
                
                // Register on both maps for active processing
                var diffMgr = Current.Game?.GetComponent<GameComponent_WaterDiffusion>();
                diffMgr?.RegisterActiveTile(Map, intake);
                diffMgr?.RegisterActiveTile(upperMap, destCell);
            }
            else
            {
                // Check passability on upper map
                if (!destCell.Walkable(upperMap)) return;
                
                ThingDef waterDef = DefDatabase<ThingDef>.GetNamed("FlowingWater", false);
                if (waterDef == null) return;
                
                Thing newWater = ThingMaker.MakeThing(waterDef);
                if (newWater is FlowingWater typedWater)
                {
                    typedWater.Volume = 0;
                    GenSpawn.Spawn(newWater, destCell, upperMap);
                    
                    int actual = Math.Min(transferAmount, FlowingWater.MaxVolume);
                    typedWater.AddVolume(actual);
                    sourceWater.Volume -= actual;
                    OnPumped(actual, settings);
                    
                    var diffMgr = Current.Game?.GetComponent<GameComponent_WaterDiffusion>();
                    diffMgr?.RegisterActiveTile(Map, intake);
                    diffMgr?.RegisterActiveTile(upperMap, destCell);
                }
            }
        }
        
        private void OnPumped(int amount, WaterSpringModSettings settings)
        {
            totalPumped += amount;
            
            // Wake nearby water tiles to respond to volume changes
            var diffMgr = Current.Game?.GetComponent<GameComponent_WaterDiffusion>();
            if (diffMgr != null)
            {
                diffMgr.ActivateNeighbors(Map, IntakeCell);
                diffMgr.ActivateNeighbors(Map, OutputCell);
            }
            
            if (settings.debugModeEnabled)
            {
                WaterSpringLogger.LogDebug($"[Pump] Pumped {amount} units. Mode: {(verticalMode ? "vertical" : "horizontal")}. Total: {totalPumped}");
            }
        }
        
        // Gizmo: Toggle horizontal/vertical mode (only if MF available)
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos())
                yield return g;
            
            if (MultiFloorsIntegration.IsAvailable && MultiFloorsIntegration.IsMultiLevel(Map))
            {
                yield return new Command_Toggle
                {
                    defaultLabel = "Vertical Mode",
                    defaultDesc = "Toggle between horizontal pumping (intake→output) and vertical pumping (intake→level above).",
                    icon = /* TODO: icon texture */ ContentFinder<Texture2D>.Get("UI/Commands/ToggleVent", true),
                    isActive = () => verticalMode,
                    toggleAction = () => verticalMode = !verticalMode
                };
            }
        }
        
        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            var settings = LoadedModManager.GetMod<WaterSpringModMain>()?.settings;
            
            if (!string.IsNullOrEmpty(s)) s += "\n";
            s += $"Mode: {(verticalMode ? "Vertical (↑)" : "Horizontal (→)")}";
            s += $"\nTotal pumped: {totalPumped}";
            
            if (!IsPoweredAndOperational())
            {
                s += "\n<color=red>Not operational</color>";
            }
            
            return s;
        }
    }
}
```

### Phase 3: Settings

```csharp
// In WaterSpringModSettings.cs — new fields
public int pumpCycleIntervalTicks = 60;     // Ticks between pump operations (60 = 1/sec)
public int pumpTransferRate = 1;             // Units transferred per cycle
public int pumpMinSourceVolume = 2;          // Won't drain source below this
public int pumpPowerConsumption = 100;       // Watts (also in XML, but handy for reference)
```

### Phase 4: Textures

| Asset | Description |
|-------|-------------|
| `Things/Building/WS_WaterPump_north.png` | Pump facing North |
| `Things/Building/WS_WaterPump_east.png` | Pump facing East |
| `Things/Building/WS_WaterPump_south.png` | Pump facing South |
| `Things/Building/WS_WaterPump_west.png` | Pump facing West |
| `UI/Commands/PumpVertical.png` | Gizmo icon for vertical mode toggle |

**Visual concept:** A mechanical box with visible intake pipe (back) and output pipe (front). Metal housing with some industrial look. Could show small animation indicator (blinking light?) when operating — future enhancement.

---

## Files to Create

| File | Type | Description |
|------|------|-------------|
| `Source/WaterSpringMod/WaterSpring/Building_WaterPump.cs` | C# | Pump building class |
| `Defs/ThingDefs/Buildings_WaterPump.xml` | XML | ThingDef with CompPower |
| `Textures/Things/Building/WS_WaterPump_north.png` | PNG | Pump texture (4 rotations) |
| `Textures/Things/Building/WS_WaterPump_east.png` | PNG | |
| `Textures/Things/Building/WS_WaterPump_south.png` | PNG | |
| `Textures/Things/Building/WS_WaterPump_west.png` | PNG | |

## Files to Modify

| File | Changes |
|------|---------|
| `WaterSpringModSettings.cs` | 4 new settings + `ExposeData` + `ClampAndSanitize` |
| `WaterSpringModMain.cs` | Settings UI for pump options |
| `MultiFloorsIntegration.cs` | Potentially add helper for "push to upper level" if needed |

---

## Decision Points (Pending)

### DP-1: Pump form factor — 1x1 or 1x2?

- **Option A (1x1, recommended for MVP):** Compact. Intake is behind, output is in front. Simple.
- **Option B (1x2):** More realistic — visible intake pipe on one side, motor in center, output on other. Better visuals but more complex placement.
- **Option C (1x1 with separate intake/output buildings):** Most flexible but overengineered. 3 buildings to place for one pump system.

### DP-2: Vertical pump — pull from behind or from below?

**In vertical mode, where does the pump pull water FROM?**

- **Option A (Behind/Intake cell, recommended):** Same as horizontal mode — water comes from the cell behind the pump, gets pushed to the level above at the pump's position. Consistent UX.
  ```
  Level 1: [water] → [PUMP] → (pushes UP to level 2)
  Level 2:            [water appears here]
  ```

- **Option B (Below at pump position):** Pull water from the same XZ position on the level below. More intuitive for "pumping UP" but requires the pump to be placed exactly above the water source.
  ```
  Level 1: [water at pump pos]  ← pulled from here
  Level 2: [PUMP]               ← water appears at output cell
  ```

- **Option C (Configurable):** Let user choose. Adds complexity.

### DP-3: Should the pump create FlowingWater from vanilla water?

- If pump's intake cell has vanilla water terrain but no FlowingWater → should it extract?
- **Option A (No, MVP):** Pump only works with FlowingWater things. Vanilla water is terrain, not a thing.
- **Option B (Yes, future):** Pump can extract from vanilla water bodies. Creates FlowingWater from thin air. Would pair with Issue #001's sink system (extract → pump → channel → sink loop).
- **Implication of Option B:** This is basically a "Water Intake" building — might be better as a separate building (`WS_WaterIntake`).
- **Recommendation:** Option A for MVP. Option B as separate Issue #004 (Water Intake).

### DP-4: Power configuration — hardcoded or setting?

- Power consumption is set in XML (`basePowerConsumption`).
- A runtime setting would require a `CompProperties` override, which is possible but adds complexity.
- **Recommendation:** Hardcode in XML for MVP (100W). Add setting later if needed.

### DP-5: Pump + Channel interaction

- If pump output goes into a channel, should it respect channel direction?
- **Yes:** Pump pushes water into the channel's allowed direction. If perpendicular, water can't enter → backs up.
- **No:** Pump force-places water regardless of channel.
- **Recommendation:** Yes — channel rules apply to all water, including pump output. This gives players control.

### DP-6: Pump sound effects

- Should the pump make noise when operating?
- RimWorld supports `CompProperties_AmbientSound` for persistent machine hum.
- **MVP:** No sound. Add later.
- **Future:** Soft mechanical hum when powered and pumping.

### DP-7: Research prerequisite

- **Electricity** (vanilla) is the minimum requirement — pump needs power.
- Should there be a custom research project like "Water Management" or "Hydraulic Engineering"?
- **MVP:** Just require `Electricity`.
- **Future:** Custom research tree for advanced water features (pump, covered channels, etc.)

### DP-8: Multiple pumps on same line — chaining

- Can you chain pumps? Pump A → water → Pump B → water → etc.
- **Should work naturally** — Pump A outputs water, Pump B's intake reads it next tick.
- **Edge case:** What if Pump B activates before Pump A in the same tick? Ordering matters.
- **Solution:** Each pump has its own tick counter with random offset (already handled by RimWorld's tick system for individual buildings).

### DP-9: Pump and elevator — redundancy?

- The existing elevator integration (Phase 5 from today's work) already moves water vertically down through elevator shafts.
- Pump in vertical mode pushes water UP — the opposite direction.
- **They complement each other:** Elevators = passive downflow, Pump = active upflow.
- **No redundancy:** Elevator is passive (gravity), pump is active (powered).

### DP-10: What if output cell is blocked?

- Pump tries to push water to output cell, but it's blocked (wall, full water, impassable terrain).
- **Behavior:** Pump does nothing, wastes power tick. No damage, no overflow.
- **Alternative:** Show "Output blocked" warning in inspect string.
- **Future:** Allow pump to build pressure (internal backlog like springs)?

---

## Open Questions

1. **Does `Rotation.FacingCell` return the correct IntVec3 for directional computation?** Need to verify that `Position + Rotation.FacingCell` gives the cell the building faces, and `Position - Rotation.FacingCell` gives the cell behind it. RimWorld docs are unclear on this.

2. **Will `tickerType = Normal` cause performance issues with many pumps?** Each pump ticks every game tick (60 ticks/sec at 1x speed). With 20+ pumps, that's 20+ ticks per game tick. Should be fine (they're very lightweight — one `ThingAt` check + one volume transfer), but need to benchmark.

3. **Short circuit in rain:** The XML includes `<shortCircuitInRain>true</shortCircuitInRain>`. Is this appropriate for a water pump? It's next to water by design. Should it be rain-resistant? Or is the risk of short circuit a gameplay tradeoff?

4. **Can the pump be placed ON a FlowingWater cell?** If the pump's own position has water, what happens? The pump is `Impassable` with `fillPercent = 0.5` — would it block FlowingWater from existing there? Need to check Thing stacking rules.

5. **Vertical mode gizmo icon:** Need a suitable icon. Could reuse RimWorld's vent toggle icon temporarily, but should create a custom one.

6. **MF upper level void check:** In vertical mode, the pump checks `IsVoidTerrain(upperMap, destCell)`. What if the void status changes after the pump is built (e.g., foundation added/removed)? The pump should recheck every cycle (already the case since it calls `IsVoidTerrain` per tick).

7. **Save/load of verticalMode:** The `verticalMode` bool is saved via `ExposeData`. If MF is later uninstalled, `verticalMode` remains true but MF checks fail gracefully → falls back to horizontal. Verify this works.

8. **Pump indicator visual:** Can we show an arrow or flow indicator on the pump sprite to make intake/output directions clear? Would need per-rotation texture overlay or dynamic draw.

---

## Test Scenarios

| # | Scenario | Expected |
|---|----------|----------|
| T1 | Place pump, water behind it, empty cell in front | Pump transfers 1 unit per cycle from back to front |
| T2 | Pump with no power | Pump idle, no transfer |
| T3 | Pump intake has volume 1 (below minSourceVolume=2) | No transfer (won't drain source dry) |
| T4 | Pump output cell at MaxVolume (7) | No transfer (output full) |
| T5 | Pump output cell has volume 5 | Transfer continues until output is full |
| T6 | Pump breakdown (CompBreakdownable) | Pump stops until repaired |
| T7 | Pump flickable switch off | Pump stops |
| T8 | Pump in vertical mode, MF installed | Water pushed from intake to upper level |
| T9 | Pump in vertical mode, no MF | Falls back to horizontal mode |
| T10 | Pump vertical, upper cell is void terrain | No transfer (can't pump into void) |
| T11 | Pump vertical, upper cell already has water at vol 6 | Transfer 1 unit → upper becomes vol 7 |
| T12 | Chain: Pump A output → Pump B intake | Water flows through chain |
| T13 | Pump output into channel | Respects channel direction rules |
| T14 | Pump intake from vanilla water | No transfer (MVP: only FlowingWater) |
| T15 | Multiple pumps on same map | Each operates independently |
| T16 | Save/load with pump active | Restores mode, tick counter, total pumped |
| T17 | Pump placed, then wall built at output | Pump stops transferring (output blocked) |
| T18 | Pump + rain + shortCircuitInRain | Potential short circuit — gameplay risk |

---

## Future Enhancements (Not MVP)

### Water Intake Building (`WS_WaterIntake`) — Potential Issue #004

A complementary building that **extracts water from vanilla water bodies**:
- Place adjacent to river/lake
- Creates FlowingWater from vanilla water terrain
- Rate limited (can't drain a river dry — vanilla water is infinite)
- Pairs with pump for: River → Intake → FlowingWater → Channel → Pump → Upper Level

### Advanced Pump Variants

| Variant | Description |
|---------|-------------|
| `WS_WaterPump_Manual` | Hand-cranked (no power). Slower rate, colonist operates it. |
| `WS_WaterPump_Heavy` | 2x2 size, double rate, higher power cost |
| `WS_WaterPump_Submersible` | Can be placed ON water terrain (for deep lake extraction) |

### Visual Enhancements

- Animated pump (moving pistons/gears) when operating
- Water flow particles between intake and output
- Sound effects (mechanical hum, water splashing)
- Status overlay (green = pumping, red = no power/broken, yellow = output full)

---

## Architecture Diagram

```
                    HORIZONTAL MODE
                    
   [Source Water]  →  intake  →  [PUMP]  →  output  →  [Dest Water]
   Volume: 7→6       (back)      ⚡100W     (front)     Volume: 0→1
                                   │
                                   └── registers both tiles for active processing
                                       wakes neighbor tiles


                    VERTICAL MODE (MultiFloors)
                    
   Level 2 (upper):                [Water appears]  ←── pushed here
                                       ▲
                                       │ (cross-map transfer)
                                       │
   Level 1 (current):  [Source] → intake → [PUMP]
                        Volume: 7→6  (back)   ⚡100W
                                              │
                                              └── verticalMode = true
                                                  TryGetUpperMap() for level above
```

---

## Cross-Feature Interactions

| Feature | Interaction |
|---------|-------------|
| Issue #001 (Vanilla Sink) | Pump output into vanilla water → drains (sink). Pump intake from vanilla water → not in MVP (future Issue #004) |
| Issue #002 (Channels) | Pump output into channel → respects channel direction. Pump intake from channel → works normally |
| Stairs (existing) | Pump is separate from stair flow. Both can coexist on same map |
| Elevators (existing) | Elevator = passive down, Pump = active up. Complementary |
| Evaporation (existing) | Pumped water is subject to normal evaporation rules |
| WS_Hole (existing) | Pump can fill hole from above → water falls through. Pump can push water up past holes |

---

## References

- `FlowingWater.cs` — `TransferVolume()` (line ~965), `AddVolume()` (line ~957)
- `GameComponent_WaterDiffusion.cs` — `RegisterActiveTile()`, `ActivateNeighbors()`
- `MultiFloorsIntegration.cs` — `TryGetUpperMap()`, `IsVoidTerrain()`, `IsAvailable`
- `Building_WaterSpring.cs` — Reference pattern for ticking buildings with water
- `Building_WSHole.cs` — Reference for simple water-related building
- RimWorld wiki: [CompPowerTrader](https://rimworldwiki.com/wiki/Modding_Tutorials/Comp), [Graphic_Multi](https://rimworldwiki.com/wiki/Modding_Tutorials/Graphic_Multi)
- MultiFloors decompiled: `PipeConnectorUtility.cs` — UI pattern inspiration only
