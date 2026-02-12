# Issue #011: Seasonal Freezing & Ice Mechanics

**Status:** Planned  
**Priority:** 🟢 Future  
**Complexity:** ⭐⭐⭐ (Medium)  
**Estimated Hours:** 12-18h  
**Dependencies:** None  
**Inspired by:** DF freezing/thawing system  
**Created:** 2026-02-11  

---

## Concept

In DF, outdoor water freezes in winter (instant, deadly) and thaws to 7/7 regardless of original depth (flood risk). In RimWorld, temperature already affects gameplay. FlowingWater could:

1. **Freeze in winter** — FlowingWater at outdoor, unroofed tiles freezes to ice when temperature drops below 0°C
2. **Thaw in spring** — Ice melts back to FlowingWater (at original volume? or always 7/7 like DF?)
3. **Ice as terrain** — Frozen water becomes walkable ice floor
4. **No flow when frozen** — Frozen tiles are excluded from diffusion

### Performance Considerations

- Temperature check: piggyback on existing evaporation tick (every `evaporationIntervalTicks`)
- Freeze/thaw: change state flag on FlowingWater, skip diffusion when frozen
- No additional per-tick cost beyond the periodic check already happening

### Key Challenge

RimWorld already has temperature and seasons. Need to check `map.mapTemperature.OutdoorTemp` and `map.roofGrid.Roofed(pos)`. Integration is straightforward.

---

## DF Reference

- 10000°U freezing point (equivalent to 0°C for us)
- Outdoor only — underground doesn't freeze
- Instant freeze/thaw (same tick)
- Thaw always produces 7/7 (DF quirk — dangerous)
- Ice is mineable, chunks melt into water items
- Pawns on freezing water die instantly; pawns on thawing ice fall in
- **Source:** [DF2014:Water](https://dwarffortresswiki.org/index.php/DF2014:Water), [DF2014:Ice](https://dwarffortresswiki.org/index.php/DF2014:Ice)

---

## Decision Points

- **DP-1:** Thaw to original volume or always 7/7 (DF-style flood risk)?
- **DP-2:** Gradual freeze (volume decreases) or instant (DF-style)?
- **DP-3:** Should frozen water block pawns or be walkable?
- **DP-4:** Interaction with roofed areas — does roofing prevent freezing?

---

## References

- [DF2014:Water](https://dwarffortresswiki.org/index.php/DF2014:Water) — Freezing section
- [DF2014:Ice](https://dwarffortresswiki.org/index.php/DF2014:Ice)
- `FlowingWater.cs` — evaporation tick (reuse for freeze check)
