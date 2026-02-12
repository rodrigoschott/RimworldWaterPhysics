# RimWorld Water Physics — Issue Tracker

## Open Issues

### 🔴 High Priority

| # | Title | Complexity | Est. Hours | Inspiration |
|---|-------|:---:|:---:|:---:|
| [001](001-vanilla-water-integration.md) | Vanilla Water Body Integration (Sink/Drain) | ⭐⭐ | 4-8h | Original |
| [002](002-water-channels.md) | Water Channel / Aqueduct System | ⭐⭐⭐ | 12-20h | Original |
| [003](003-water-pump.md) | Water Pump Building | ⭐⭐⭐⭐ | 15-25h | Original + DF |
| [004](004-pressure-teleport.md) | Pressure Propagation (DF "Teleport") | ⭐⭐⭐⭐ | 20-35h | DF Pressure |
| [005](005-floodgate.md) | Floodgate & Hatch (Flow Control) | ⭐⭐⭐ | 10-16h | DF Floodgate |

### 🟡 Medium Priority

| # | Title | Complexity | Est. Hours | Inspiration |
|---|-------|:---:|:---:|:---:|
| [006](006-water-purification.md) | Water Purification via Pump | ⭐⭐ | 6-10h | DF Screw Pump |
| [007](007-spring-variants.md) | Spring Variants (Light/Heavy) + Drain | ⭐⭐ | 4-6h | DF Aquifer |
| [008](008-grate-and-bars.md) | Grate & Bars (Selective Flow Control) | ⭐⭐ | 6-10h | DF Fortification |
| [009](009-hatch-cover.md) | Hatch Cover (Vertical Flow Toggle) | ⭐⭐ | 4-6h | DF Hatch |

### 🟢 Future / Backlog

| # | Title | Complexity | Est. Hours | Inspiration |
|---|-------|:---:|:---:|:---:|
| [010](010-water-wheel.md) | Water Wheel (Power Generation) | ⭐⭐⭐⭐ | 20-30h | DF Water Wheel |
| [011](011-seasonal-freezing.md) | Seasonal Freezing & Ice | ⭐⭐⭐ | 12-18h | DF Ice |
| [012](012-pump-stack.md) | Pump Stack (Multi-Level Chain) | ⭐⭐ | 4-8h | DF Pump Stack |
| [013](013-water-contamination.md) | Water Contamination System | ⭐⭐⭐⭐ | 20-30h | DF Contamination |

---

## Suggested Implementation Roadmap

### Phase 1 — Core Water Management (MVP+)
1. **#001** — Vanilla Water Sink *(4-8h, highest ROI)*
2. **#005** — Floodgate + Hatch *(10-16h, essential for control)*
3. **#002** — Channels *(12-20h, directed flow)*
4. **#008** — Grate/Bars *(6-10h, selective barriers)*

### Phase 2 — Active Systems
5. **#003** — Water Pump *(15-25h, active water movement)*
6. **#007** — Spring Variants + Drain *(4-6h, output diversity)*
7. **#009** — Hatch Cover *(4-6h, vertical control)*

### Phase 3 — Advanced Physics
8. **#004** — Pressure Propagation *(20-35h, biggest physics upgrade)*
9. **#012** — Pump Stack *(4-8h, multi-level pumping)*

### Phase 4 — Gameplay Integration
10. **#010** — Water Wheel *(20-30h, power generation)*
11. **#011** — Seasonal Freezing *(12-18h, biome interaction)*

### Phase 5 — Deep Systems
12. **#006** — Water Purification *(6-10h, requires #013)*
13. **#013** — Contamination System *(20-30h, quality tracking)*

---

## Effort Summary

| Phase | Hours (est.) | Features |
|-------|:---:|:---:|
| Phase 1 | 32-54h | 4 features |
| Phase 2 | 23-37h | 3 features |
| Phase 3 | 24-43h | 2 features |
| Phase 4 | 32-48h | 2 features |
| Phase 5 | 26-40h | 2 features |
| **Total** | **137-222h** | **13 features** |

---

## Feature Dependency Graph

```
#001 Vanilla Sink ──────────────────────────────┐
#002 Channels ──────────┬───────────────────────│─── #004 Pressure
#003 Pump ──────────────┤                       │
  ├── #006 Purification ├── #013 Contamination  │
  └── #012 Pump Stack   │                       │
#005 Floodgate ─────────┤                       │
  └── #009 Hatch        │                       │
#007 Spring Variants ───┘                       │
#008 Grate/Bars ────────────────────────────────┘
#010 Water Wheel (standalone)
#011 Freezing (standalone)
```

---

*Last updated: 2026-02-11*
