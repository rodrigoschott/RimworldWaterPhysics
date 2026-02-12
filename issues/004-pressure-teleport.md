# Issue #004: Pressure Propagation (DF-Inspired "Teleport")

**Status:** Implemented
**Priority:** 🔴 High
**Complexity:** ⭐⭐⭐⭐ (Medium-High)
**Estimated Hours:** 20-35h (actual: implemented as part of core rewrite)
**Dependencies:** None (but synergizes with Issue #002 Channels and Issue #003 Pump)
**Inspired by:** Dwarf Fortress "lazy pressure model"
**Created:** 2026-02-11
**Implemented:** 2026-02-12 (as part of water physics core rewrite)
**Plan:** `docs/plans/2026-02-12-water-physics-core-rewrite.md`

> **Implementation Notes:** Pressure BFS was implemented as Task 5 of a 6-task core rewrite that also included gravity-first bulk transfer, equilibrium-seeking diff/2, multi-neighbor diffusion, and anti-backflow removal. The implementation uses the proactive trigger approach (check on diffusion tick when all neighbors are full) with M1 (depth limit 32), M2 (cooldown 10 ticks), and M5 (dedicated scratch buffers). Same-map only for MVP. See `PressurePropagation.cs`.

---

## Problem Statement

Currently, water in our mod diffuses **one tile at a time** per tick cycle. A FlowingWater tile at volume 7 can only transfer 1 unit to an adjacent tile, which then transfers 1 to the next, and so on. This means:

1. **Long channels fill very slowly** — a 20-tile aqueduct takes 20+ cycles to reach the end
2. **No sense of pressure** — a column of 7/7 water behind a wall behaves identically to a puddle of 2/7
3. **Unrealistic reservoir behavior** — opening a dam doesn't produce a rush of water, just slow diffusion
4. **Connected bodies don't equalize** — two pools connected by a 1-tile tunnel don't equalize quickly

In Dwarf Fortress, water that falls onto a full (7/7) tile **traces a path through connected full tiles** and teleports to the first open space. This creates near-instant propagation through full pipes and realistic pressure behavior.

---

## Dwarf Fortress Reference

### The "Lazy Pressure Model"

DF's pressure system has 3 rules:

1. **Pressure is NOT a property of water** — it's a movement rule
2. **Pressure triggers when:** water falls onto 7/7 water, river source generates, or pump outputs
3. **Propagation:** traces orthogonal path through 7/7 tiles (down → horizontal → up, never diagonal). Water "teleports" to first open space along this path
4. **Height limit:** water never rises above the Z-level of the pressure source
5. **Diagonal reset:** forcing flow through a diagonal gap kills all pressure

### Key Behaviors

- **U-bend:** Water in one side pushes up the other side to source height
- **Long pipes:** Full pipe of 7/7 propagates instantly end-to-end  
- **Teleportation:** Skipped tiles don't generate "flow" — it's truly instant
- **Source types:** Only falling-onto-full, river sources, and pumps create pressure; lakes/oceans don't

**Source:** [DF2014:Pressure](https://dwarffortresswiki.org/index.php/DF2014:Pressure)

---

## Proposed Solution for Water Spring Mod

### Concept: "Pressure Propagation"

Instead of DF's teleport model (which is hard to reconcile with our per-tile diffusion), we implement a **fast-path propagation** system: when a tile at MaxVolume (7) receives more water, it traces a path through adjacent MaxVolume tiles and deposits the water at the end of the chain.

### Algorithm: BFS Pressure Trace

```
PressurePropagate(source, amount):
    // Only triggers when source is at MaxVolume and tries to receive more
    if source.Volume < MaxVolume: return false  // Normal diffusion handles this
    
    // BFS through connected 7/7 tiles (cardinal only)
    queue = [source]
    visited = {source}
    candidates = []  // open tiles at the frontier
    
    while queue not empty:
        current = queue.dequeue()
        for each cardinal neighbor of current:
            if neighbor in visited: continue
            visited.add(neighbor)
            
            water = GetFlowingWater(neighbor)
            if water != null AND water.Volume == MaxVolume:
                queue.enqueue(neighbor)  // Continue through full tiles
            else if water != null AND water.Volume < MaxVolume:
                candidates.add((neighbor, water))  // Partial fill = endpoint
            else if IsPassableForWater(neighbor) AND water == null:
                candidates.add((neighbor, null))  // Empty passable = endpoint
    
    if candidates empty: return false  // No open space found, pressure trapped
    
    // Select best candidate: lowest volume, then gravity preference (lower Z first)
    target = SelectBestCandidate(candidates)
    
    // Transfer
    if target.water != null:
        transfer = Min(amount, MaxVolume - target.water.Volume)
        target.water.AddVolume(transfer)
    else:
        SpawnFlowingWater(target.cell, amount=1)
    
    return true
```

### Where to Inject

**`FlowingWater.AttemptLocalDiffusion()` — before the existing expansion logic:**

```csharp
// PRIORITY -1: Pressure propagation through full tiles
if (settings.pressurePropagationEnabled && Volume >= MaxVolume)
{
    // Check if any incoming transfer would overflow
    // This is called from the Volume setter when volume would exceed max
    // Or: check if all neighbors are also at 7 — pressure is building
    bool allNeighborsFull = true;
    foreach (var dir in GenAdj.CardinalDirections)
    {
        IntVec3 adj = pos + dir;
        if (!adj.InBounds(Map)) continue;
        FlowingWater nw = Map.thingGrid.ThingAt<FlowingWater>(adj);
        if (nw == null || nw.Volume < MaxVolume) { allNeighborsFull = false; break; }
    }
    
    if (allNeighborsFull)
    {
        // All neighbors full — try pressure propagation
        if (TryPressurePropagate(Map, pos, 1, settings))
        {
            return true; // Pressure handled it
        }
    }
}
```

**Alternative injection point — `Volume` setter:**

When `AddVolume()` is called on a tile already at MaxVolume, instead of clamping, trigger pressure propagation. This is more DF-like (pressure on overflow).

```csharp
public int Volume
{
    set
    {
        int clamped = Math.Max(0, Math.Min(value, MaxVolume));
        int overflow = value - clamped;
        
        if (overflow > 0 && settings.pressurePropagationEnabled)
        {
            // Water is being pushed into a full tile — pressure!
            TryPressurePropagate(Map, Position, overflow, settings);
        }
        
        _volume = clamped;
        // ... rest of setter
    }
}
```

---

## Performance Analysis ⚡

### Current System Characteristics

| Metric | Current Value |
|--------|:---:|
| Max active tiles per tick | 500 (setting) |
| Diffusion per tile | O(4) cardinal neighbors |
| Transfer per tick per tile | 1 unit max |
| Propagation speed | 1 tile/tick |

### Impact of Pressure Propagation

#### Worst Case: Large Connected Body at 7/7

**Scenario:** 200 tiles of 7/7 water connected (e.g., flooded room). Spring adds 1 unit → triggers BFS.

| Step | Cost |
|------|------|
| BFS traversal | O(N) where N = connected 7/7 tiles |
| HashSet visited | O(N) memory |
| Per-tile check | `ThingAt<FlowingWater>()` = O(1) amortized |
| Total for 200 tiles | ~200 * O(1) = **~200 operations** |

**Risk: HIGH.** A 200-tile body triggers 200-node BFS every time pressure fires. If this happens every tick for an active spring, it's 200 ops/tick just for pressure.

#### Mitigation Strategies

**M1: BFS Depth Limit**
```csharp
public int pressureMaxSearchDepth = 32; // Don't BFS more than 32 tiles deep
```
Caps worst case at 32 operations regardless of body size. Pressure "dies" after 32 tiles — reasonable physically (friction loss).

**M2: Pressure Cooldown Per Tile**
```csharp
private int pressureCooldownRemaining = 0;
// Only allow pressure propagation every N ticks per tile
if (pressureCooldownRemaining > 0) { pressureCooldownRemaining--; return; }
pressureCooldownRemaining = settings.pressureCooldownTicks; // e.g., 10
```
Prevents high-frequency BFS spam from active springs.

**M3: Lazy Evaluation — Only Propagate on Overflow**
Don't run BFS proactively. Only trigger when `AddVolume()` would overflow MaxVolume. This means pressure only fires when water is actively being pushed in, not on every diffusion tick.

**M4: Cached Pressure Paths**
Cache the BFS result for a tile. Invalidate when any tile in the path changes volume. Avoids repeated BFS for static configurations.

```csharp
// In GameComponent_WaterDiffusion:
private Dictionary<(Map, IntVec3), (IntVec3 target, int pathLength, int cachedTick)> pressurePathCache;

// Cache hit: reuse path if still valid (all intermediate tiles still 7/7)
// Cache invalidation: on any volume change in the path
```

**M5: Reuse Scratch Buffers**
BFS already has scratch buffers in `GameComponent_WaterDiffusion` (`bfsFrontier`, `bfsVisited`). Reuse them for pressure BFS to avoid GC allocation.

#### Performance Benchmarks (Estimated)

| Scenario | Tiles | BFS Depth | Ops/Tick | Impact |
|----------|:---:|:---:|:---:|:---:|
| Short channel (5 tiles) | 5 | 5 | ~5 | ✅ Negligible |
| Medium aqueduct (20 tiles) | 20 | 20 | ~20 | ✅ Low |
| Depth-limited (32 cap) | 200 | 32 | ~32 | ✅ Acceptable |
| Unlimited (200 tiles) | 200 | 200 | ~200 | ⚠️ Noticeable |
| Unlimited + every tick | 200 | 200 | ~12000/sec | ❌ Bad |
| With cooldown (10 ticks) | 200 | 32 | ~192/sec | ✅ Fine |

#### Recommendation

**Use M1 + M2 + M3 + M5 together:**
- Depth limit 32 (caps BFS)
- Cooldown 10 ticks (rate limit)
- Only on overflow (lazy trigger)
- Reuse scratch buffers (no GC)

**Estimated overhead:** < 1% of frame time for typical gameplay (1-3 springs, 50-100 water tiles).

#### Potential Performance IMPROVEMENT 📈

Pressure propagation can actually **improve** performance by reducing the number of active tiles:

- **Without pressure:** 200 tiles of 7/7 water are all "active" trying to diffuse but can't move → wasted ticks checking, incrementing stability
- **With pressure:** Overflow is handled by BFS, tiles stay stable faster. Only the pressure source and destination are active.
- **Net effect:** Fewer active tiles in steady-state = fewer per-tile operations = **faster overall**

---

## Settings

```csharp
// Pressure propagation (DF-inspired)
public bool pressurePropagationEnabled = true;     // Default: enabled
public int pressureMaxSearchDepth = 32;             // Max BFS depth (tiles)
public int pressureCooldownTicks = 10;              // Min ticks between pressure events per tile
public bool pressureGravityPriority = true;         // Prefer lower Z-levels as target
public bool pressureDiagonalReset = false;          // Future: diagonal gaps reset pressure (DF-style)
```

---

## Files to Create

| File | Description |
|------|-------------|
| `Source/.../PressurePropagation.cs` | Static utility class with BFS pressure logic |

## Files to Modify

| File | Changes |
|------|---------|
| `FlowingWater.cs` | Overflow trigger in Volume setter or AttemptLocalDiffusion |
| `GameComponent_WaterDiffusion.cs` | Scratch buffer sharing, pressure path cache |
| `WaterSpringModSettings.cs` | 5 new settings |
| `WaterSpringModMain.cs` | Settings UI |

---

## Decision Points

### DP-1: Trigger mechanism — Overflow vs. Proactive
- **Overflow (recommended):** Only triggers when `AddVolume()` exceeds MaxVolume. Lazy, efficient.
- **Proactive:** Checks on every diffusion tick if all neighbors are full. More responsive but costlier.

### DP-2: Should pressure propagate across Z-levels?
- **DF does this** (down → horizontal → up, to source height).
- **For us:** With MultiFloors, this means BFS across maps. Much more complex.
- **Recommendation:** Same-map only for MVP. Cross-level pressure via stairs/holes as future enhancement.

### DP-3: Diagonal reset (DF's signature mechanic)
- In DF, diagonal gaps kill pressure. This enables "pressure regulators."
- **For us:** We don't have diagonal flow at all (cardinal only). Would need to add diagonal passability first.
- **Recommendation:** Skip for MVP. Add as `pressureDiagonalReset` setting later.

### DP-4: Should springs generate pressure?
- DF's river sources generate pressure. Our springs could too.
- **If yes:** Spring at 7/7 with backlog triggers pressure propagation into the pipe.
- **Recommendation:** Yes — this is the primary use case. Spring → full channel → pressure pushes to end.

### DP-5: Interaction with channels (Issue #002)
- Should pressure respect channel direction restrictions?
- **Option A:** Yes — pressure follows channel axis only. Realistic.
- **Option B:** No — pressure ignores channels (it's "force"). More DF-like (pressure ignores most things).
- **Recommendation:** Option A for consistency.

---

## Test Scenarios

| # | Scenario | Expected |
|---|----------|----------|
| T1 | Spring → 10-tile full channel → empty area | Water appears at end within 1-2 ticks (not 10+) |
| T2 | U-bend: water one side, empty other | Water rises on other side quickly |
| T3 | BFS hits depth limit (32) | Propagation stops, doesn't scan further |
| T4 | All tiles 7/7, no open space | Pressure trapped, no transfer (no crash) |
| T5 | Pressure + channel direction | Respects channel axis |
| T6 | Pressure cooldown active | No BFS for 10 ticks after last propagation |
| T7 | Breaking wall near pressured body | Water rushes to new opening via pressure |
| T8 | 200-tile body, TPS baseline | < 1% frame time impact (with mitigations) |
| T9 | Pressure disabled in settings | Normal diffusion behavior |
| T10 | Spring with backlog → pressure | Backlog drains faster through full pipe |

---

## References

- [DF2014:Pressure](https://dwarffortresswiki.org/index.php/DF2014:Pressure) — Complete DF pressure mechanics
- [DF2014:Flow](https://dwarffortresswiki.org/index.php/DF2014:Flow) — Flow and teleportation rules
- `FlowingWater.cs` — `AttemptLocalDiffusion()`, `Volume` setter
- `GameComponent_WaterDiffusion.cs` — BFS scratch buffers (`bfsFrontier`, `bfsVisited`)
