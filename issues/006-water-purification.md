# Issue #006: Water Purification via Pump

**Status:** Planned  
**Priority:** 🟡 Medium  
**Complexity:** ⭐⭐ (Low-Medium)  
**Estimated Hours:** 6-10h  
**Dependencies:** Issue #003 (Pump), Issue #013 (Contamination system)  
**Inspired by:** DF screw pump purifying salt/stagnant water  
**Created:** 2026-02-11  

---

## Concept

In DF, pumping water through a screw pump **purifies** it — salt water becomes fresh, stagnant water becomes clean. This is a key mechanic: players must build pump-based purification systems to make unsafe water drinkable.

For our mod, this becomes relevant **only if we implement a contamination system** (Issue #013). Without contamination, there's nothing to purify.

### Prereq: Water Contamination (Issue #013)

Before this issue is actionable, we need FlowingWater to carry contamination data:
```csharp
public enum WaterQuality { Fresh, Stagnant, Salty, Muddy, Contaminated }
public WaterQuality quality = WaterQuality.Fresh;
```

### Purification Mechanic

When `Building_WaterPump` transfers water:
```csharp
// In TryPumpHorizontal/TryPumpVertical:
if (settings.pumpPurifiesWater && destWater != null)
{
    destWater.quality = WaterQuality.Fresh; // Pumped water is always clean
}
```

### Gameplay Value

- Salty water (from ocean/map edge) → pump → fresh FlowingWater
- Stagnant water (from swamp terrain) → pump → clean
- Creates meaningful reason to build pumps even on flat maps (purification station)

---

## Decision Points

- **DP-1:** Is contamination worth implementing? Adds complexity for gameplay depth. Defer decision.
- **DP-2:** Should purification be pump-only, or also work with channels (slow purification over distance)?
- **DP-3:** Colonist effects — should contaminated water cause mood debuffs? Only relevant if mod adds drinking mechanics.

---

## References

- [DF2014:Screw_pump](https://dwarffortresswiki.org/index.php/DF2014:Screw_pump) — "Pumping salt water can produce clean water"
- [DF2014:Water](https://dwarffortresswiki.org/index.php/DF2014:Water) — Salt, stagnant, mud contamination
- Issue #003 (Pump) — prerequisite building
- Issue #013 (Contamination) — prerequisite system
