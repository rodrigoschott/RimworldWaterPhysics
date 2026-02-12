# Issue #009: Hatch Cover (Vertical Flow Toggle)

**Status:** Planned  
**Priority:** 🟡 Medium  
**Complexity:** ⭐⭐ (Low)  
**Estimated Hours:** 4-6h  
**Dependencies:** Shares implementation with Issue #005 (Floodgate)  
**Inspired by:** DF Hatch covers over stairs/channels  
**Created:** 2026-02-11  

---

## Concept

In DF, **hatches** placed over channels or stairs block vertical fluid flow but still allow the tile to be used by pawns. They're the vertical equivalent of floodgates.

For us, a hatch over `WS_Hole` would block water from falling through while the hatch is closed. Combined with CompFlickable, players can control when vertical flow is active.

### Implementation

This is essentially a variant of Issue #005's `Building_WaterFloodgate` applied to vertical portals:

```csharp
// In VerticalPortalBridge.IsHoleAt():
Building_WaterFloodgate hatch = map.thingGrid.ThingAt<Building_WaterFloodgate>(cell);
if (hatch != null && hatch.def.defName == "WS_Hatch" && !hatch.IsOpen)
    return false; // Hatch blocks the hole
```

### DF Details

- Hatches block **both** water falling down AND pressure pushing up
- Hatches can be linked to levers for remote control
- Hatches still allow the tile to be used as a stair (pawns walk through when open)
- Hatches over pump intake tiles prevent pumping

**Note:** This issue is effectively a sub-task of Issue #005. Consider implementing together.

---

## References

- [DF2014:Pressure](https://dwarffortresswiki.org/index.php/DF2014:Pressure) — "Hatches prevent water from moving vertically"
- `VerticalPortalBridge.cs` — `IsHoleAt()`, `TryGetLowerMap()`
- Issue #005 (Floodgate) — shared building class
