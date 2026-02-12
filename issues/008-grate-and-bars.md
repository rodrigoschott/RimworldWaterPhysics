# Issue #008: Grate & Bars (Selective Flow Control)

**Status:** Planned  
**Priority:** 🟡 Medium  
**Complexity:** ⭐⭐ (Low)  
**Estimated Hours:** 6-10h  
**Dependencies:** None  
**Inspired by:** DF Fortifications, Grates, and Bars  
**Created:** 2026-02-11  

---

## Concept

In DF, **fortifications**, **grates**, and **bars** allow fluids to pass freely but block creature/pawn movement. This is essential for:

- **Safe wells:** Water accessible but enemies can't enter
- **Flood defense:** Water drains out through grate without pawns falling in
- **Filtering:** Items don't flow through but water does

### Proposed Buildings

| Building | defName | Water | Pawns | Items | Use Case |
|----------|---------|:---:|:---:|:---:|----------|
| **Water Grate** | `WS_Grate` | ✅ Pass | ❌ Block | ❌ Block | Drain covers, well protection |
| **Water Bars** | `WS_Bars` | ✅ Pass | ❌ Block | ✅ Pass | Decorative flow barriers |

### Implementation

Very simple — these buildings have `fillPercent > 0.1` (normally blocks water in our code) but we add an exception:

```csharp
// In FlowingWater.AttemptLocalDiffusion(), edifice check:
Building ed = adjacentCell.GetEdifice(Map);
if (ed != null && ed.def != null && ed.def.fillPercent > 0.1f)
{
    // NEW: Check if it's a water-passable building (grate/bars)
    if (ed.def.IsWaterPassable()) // custom extension method or defModExtension
    {
        // Allow water through, don't skip
    }
    else
    {
        continue; // Normal block
    }
}
```

### Performance Impact

Zero additional cost — replaces a `continue` with an `if` check. Same code path.

---

## Decision Points

- **DP-1:** Use `defModExtension` tag (`<waterPassable>true</waterPassable>`) or check defName? Extension is cleaner and lets modders add water-passable buildings.
- **DP-2:** Should grates work as vertical flow blockers too? (Over holes, like DF's floor grates)
- **DP-3:** Can other mods' buildings be tagged as water-passable? If we use modExtension, yes.

---

## References

- [DF2014:Flow](https://dwarffortresswiki.org/index.php/DF2014:Flow) — "grates, bars, and fortifications allow fluids to pass freely"
- `FlowingWater.cs` — edifice check at line ~586
