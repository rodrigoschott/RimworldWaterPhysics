# Issue #013: Water Contamination System

**Status:** Planned  
**Priority:** 🟢 Future  
**Complexity:** ⭐⭐⭐⭐ (Medium-High)  
**Estimated Hours:** 20-30h  
**Dependencies:** Issue #003 (Pump) for purification  
**Inspired by:** DF salt water, stagnant water, mud contamination  
**Created:** 2026-02-11  

---

## Concept

In DF, water carries quality attributes — fresh, salty, stagnant, muddy, contaminated (blood, vomit, etc.). Each affects gameplay:

- **Salt water:** Undrinkable, needs purification (pump)
- **Stagnant:** Causes unhappy thoughts, infection risk when cleaning wounds
- **Muddy:** From shallow pools over mud, minor debuff
- **Contaminated:** Blood/substances in water spread via flow, cause syndromes on ingestion

### RimWorld Adaptation

FlowingWater gains a `WaterQuality` enum:

```csharp
public enum WaterQuality : byte
{
    Fresh = 0,        // Clean, drinkable
    Stagnant = 1,     // From swamp/marsh biomes — mood debuff
    Salty = 2,        // From ocean map edge — undrinkable
    Muddy = 3,        // From certain terrain — minor debuff
    Contaminated = 4  // Blood/toxins — dangerous
}
```

### Gameplay Implications

- **Colonists drink water** — needs integration with RimWorld's need system or a custom "thirst" system (major scope)
- **Wound cleaning** — doctors using contaminated water increases infection chance
- **Farming** — irrigated farms could benefit from fresh water, suffer from salt
- **Mood** — drinking stagnant water gives negative thought

### Performance Impact

- One byte per FlowingWater tile — negligible memory
- Quality propagation during diffusion: output quality = input quality (follows flow). O(1) per transfer.
- Purification check on pump transfer: O(1)
- **Net impact: essentially zero**

### Scope Warning ⚠️

This is a **large feature** that touches many systems. Consider splitting into:
1. **Phase 1:** Add quality field + save/load + visual indicator (color tint)
2. **Phase 2:** Pump purification (Issue #006)
3. **Phase 3:** Gameplay effects (mood, health, farming)

---

## DF Reference

- Salt: ocean biomes, aquifers near coast
- Stagnant: murky pools, wetlands
- Pumps purify both salt and stagnant
- Contaminants (blood) multiply when water flows over them
- Clean water flowing into stagnant water converts it to fresh
- **Source:** [DF2014:Water](https://dwarffortresswiki.org/index.php/DF2014:Water)

---

## Decision Points

- **DP-1:** Does this mod add drinking mechanics? Massive scope if yes. Might pair better with existing mods like Dubs Bad Hygiene.
- **DP-2:** Visual representation — color tint per quality? Separate overlay?
- **DP-3:** Contamination spreading — does dirty water contaminate clean water on contact?

---

## References

- [DF2014:Water](https://dwarffortresswiki.org/index.php/DF2014:Water) — Contamination section
- Issue #006 (Purification) — downstream feature
- Issue #003 (Pump) — purification mechanism
