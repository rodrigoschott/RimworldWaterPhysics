# Issue #010: Water Wheel (Power Generation from Flow)

**Status:** Planned  
**Priority:** 🟢 Future  
**Complexity:** ⭐⭐⭐⭐ (Medium-High)  
**Estimated Hours:** 20-30h  
**Dependencies:** Issue #004 (Pressure) for meaningful flow detection  
**Inspired by:** DF Water Wheel  
**Created:** 2026-02-11  

---

## Concept

A water wheel placed adjacent to flowing water generates electrical power. In DF, water wheels require ≥4/7 flowing water below them and generate 100 power (gross). Flow direction matters — the wheel axis must be perpendicular to flow direction.

### RimWorld Adaptation

A `WS_WaterWheel` building (2x1 or 3x1) that generates power when adjacent tiles have actively flowing FlowingWater.

**Power output** proportional to:
- Number of adjacent FlowingWater tiles
- Volume of those tiles
- Whether they're actively flowing (not stable)

### Key Challenges

1. **Flow direction detection** — Our mod doesn't track flow direction (DF does via "natural flow"). Would need to add directional flow tracking or simplify to "any active water = power."

2. **Power integration** — RimWorld's power system uses `CompPowerPlant`. The wheel would use `CompPowerPlant` with dynamic `PowerOutput` based on adjacent water activity.

3. **Performance** — Checking adjacent water state every tick for power calculation. Mitigated by checking every 60 ticks and caching.

### Performance Considerations

- Power recalculation: every 60 ticks (1 sec), scan 2-4 adjacent cells
- Cost: ~4 `ThingAt<FlowingWater>()` calls per recalc = negligible
- Net impact: essentially zero (same cost as a solar panel checking weather)

---

## DF Reference

- Water wheel: 100 gross power, 10 consumed = 90 net
- Requires ≥4/7 naturally flowing water directly below
- Flow direction must be perpendicular to wheel axis
- Water wheels can be chained (each requires its own flow source)
- **Source:** [DF2014:Water_wheel](https://dwarffortresswiki.org/index.php/DF2014:Water_wheel)

---

## Decision Points

- **DP-1:** Power output formula — flat rate when water present, or proportional to volume/flow?
- **DP-2:** Size — 1x1 (compact) or 3x1 (spans water channel)?
- **DP-3:** Requires flowing water or just any FlowingWater with volume ≥ N?

---

## References

- [DF2014:Water_wheel](https://dwarffortresswiki.org/index.php/DF2014:Water_wheel)
- RimWorld: `CompPowerPlant`, `CompPowerTrader`
