# Issue #012: Pump Stack (Multi-Level Pumping Chain)

**Status:** Planned  
**Priority:** 🟢 Future  
**Complexity:** ⭐⭐ (Low, if Issue #003 is done)  
**Estimated Hours:** 4-8h  
**Dependencies:** Issue #003 (Pump), MultiFloors  
**Inspired by:** DF Pump Stack  
**Created:** 2026-02-11  

---

## Concept

In DF, a **pump stack** is a vertical column of screw pumps, each lifting water 1 Z-level. They chain automatically — pump A's output feeds pump B's intake. Power transmits through adjacent pumps (no extra axles needed).

For our mod with MultiFloors, this means chaining `WS_WaterPump` buildings vertically across multiple levels.

### Implementation

If Issue #003 is implemented correctly (pump pulls from intake, pushes to output), pump stacking should work **automatically**:

1. Pump A (level 0) pulls water from ground, pushes to level 1
2. Pump B (level 1) pulls from level 1 (where pump A deposited), pushes to level 2
3. Each pump operates independently on its own tick cycle

**No additional code needed** — just documentation and testing that the chain works.

### The Only Challenge

**Power transmission between levels.** In DF, adjacent pumps share power. In RimWorld, each pump needs its own power conduit. With MultiFloors, power conduits between levels might need MF's pipe connector system.

**Workaround:** Each pump has its own CompPowerTrader. Player connects power to each level independently. Not as elegant as DF but functional.

---

## DF Reference

- Pump stacks can lift water/magma any height
- Power transmits between adjacent pumps
- Top pump powered → whole stack runs
- Manual operation only drives one pump (no power transmission from dwarf labor)
- **Source:** [DF2014:Screw_pump](https://dwarffortresswiki.org/index.php/DF2014:Screw_pump)

---

## References

- Issue #003 (Pump)
- `Building_WaterPump.cs` — TryPumpVertical()
