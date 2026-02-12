# Issue #007: Spring Variants (Light/Heavy Output)

**Status:** Planned  
**Priority:** 🟡 Medium  
**Complexity:** ⭐⭐ (Low)  
**Estimated Hours:** 4-6h  
**Dependencies:** None  
**Inspired by:** DF Light vs Heavy Aquifer system  
**Created:** 2026-02-11  

---

## Concept

DF has two aquifer types:
- **Light:** ~4 units/month. Manageable, useful for slow cistern fill.
- **Heavy:** 1/7 every ~14 ticks. Brutal, can flood entire bases.

Our `WaterSpring` currently has one type with configurable `waterSpringSpawnInterval` (global setting). We could create **multiple spring building variants** with different output rates and costs.

### Proposed Variants

| Variant | defName | Output | Cost | Research |
|---------|---------|:---:|------|----------|
| **Trickle Spring** | `WS_SpringLight` | 1 unit / 300 ticks (5sec) | 30 Steel | None |
| **Water Spring** (current) | `WaterSpring` | 1 unit / 120 ticks (2sec) | 75 Steel + 1 Component | None |
| **Pressure Spring** | `WS_SpringHeavy` | 1 unit / 30 ticks (0.5sec) | 150 Steel + 3 Components | Electricity |
| **Natural Spring** | `WS_SpringNatural` | Slow, infinite, non-buildable | N/A (map gen) | N/A |

### Implementation

Each variant is a separate `ThingDef` using the same `Building_WaterSpring` class but with `modExtensions` carrying the rate:

```xml
<modExtensions>
    <li Class="WaterSpringMod.WaterSpring.SpringSettings">
        <spawnInterval>30</spawnInterval>
        <backlogCap>14</backlogCap>
    </li>
</modExtensions>
```

The `Building_WaterSpring.Tick()` reads from the extension instead of global settings.

### DF's Absorb Mechanic

DF's heavy aquifer also **absorbs** water (acts as infinite sink). Could add a `WS_Drain` building that absorbs FlowingWater — the inverse of a spring. Simple, useful for preventing floods.

```xml
<defName>WS_Drain</defName>
<label>water drain</label>
<description>Absorbs adjacent water over time. Useful for flood control.</description>
```

---

## Decision Points

- **DP-1:** Per-def rate vs global setting? Per-def allows multiple springs on same map with different rates.
- **DP-2:** Should `WS_Drain` (absorber) be part of this issue or separate?
- **DP-3:** Natural springs in map generation? Requires patching MapGenerator — complex, future.

---

## References

- [DF2014:Aquifer](https://dwarffortresswiki.org/index.php/DF2014:Aquifer) — Light/heavy types, absorption
- `Building_WaterSpring.cs` — existing spring implementation
