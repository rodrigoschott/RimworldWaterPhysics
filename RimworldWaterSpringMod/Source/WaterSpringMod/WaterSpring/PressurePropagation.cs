using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WaterSpringMod.WaterSpring
{
    /// <summary>
    /// DF-inspired pressure propagation: BFS through connected 7/7 tiles
    /// to find the nearest non-full outlet and teleport water there.
    /// Supports cross-map traversal through WS_Hole tiles.
    /// </summary>
    public static class PressurePropagation
    {
        // BFS node: position + map (for cross-map traversal)
        private struct BfsNode
        {
            public IntVec3 cell;
            public Map map;
            public BfsNode(IntVec3 c, Map m) { cell = c; map = m; }
        }

        // Splash candidate: a tile with capacity that can receive water
        private struct SplashCandidate
        {
            public IntVec3 cell;
            public Map map;
            public FlowingWater water; // null if empty tile
            public int capacity;
        }

        // Reusable scratch buffers to avoid GC allocation per call
        private static readonly Queue<BfsNode> _frontier = new Queue<BfsNode>(64);
        private static readonly HashSet<long> _visited = new HashSet<long>(256);
        private static readonly List<SplashCandidate> _splashCandidates = new List<SplashCandidate>(32);

        // Equalization scratch buffers (separate from pressure/splash to avoid conflicts)
        private static readonly Queue<IntVec3> _eqFrontier = new Queue<IntVec3>(256);
        private static readonly List<FlowingWater> _eqRegion = new List<FlowingWater>(256);

        /// <summary>
        /// Standard pressure: BFS starts from source tile, volume subtracted from source.
        /// Source must be at MaxVolume. Returns true if water was delivered.
        /// </summary>
        public static bool TryPropagate(FlowingWater source, Map map, WaterSpringModSettings settings, bool debug)
        {
            if (source == null || map == null) return false;
            if (source.Volume < FlowingWater.MaxVolume) return false;
            return TryPropagateCore(source, map, source.Position, settings, debug);
        }

        /// <summary>
        /// Pass-through pressure: BFS starts from a relay tile (e.g. tile below a hole),
        /// but volume is subtracted from actualSource (e.g. the hole tile above).
        /// The relay tile stays at 7/7 — water passes through it.
        /// </summary>
        public static bool TryPassThrough(FlowingWater actualSource, Map relayMap, IntVec3 relayCell,
            WaterSpringModSettings settings, bool debug)
        {
            if (actualSource == null || relayMap == null) return false;
            if (actualSource.Volume <= 0) return false;
            return TryPropagateCore(actualSource, relayMap, relayCell, settings, debug);
        }

        /// <summary>
        /// Core BFS: starts from (bfsStartMap, bfsStartCell), explores connected 7/7 tiles,
        /// delivers to nearest non-full outlet. Volume subtracted from volumeSource.
        /// </summary>
        private static bool TryPropagateCore(FlowingWater volumeSource, Map bfsStartMap, IntVec3 bfsStartCell,
            WaterSpringModSettings settings, bool debug)
        {
            if (!settings.pressurePropagationEnabled) return false;

            var diffusionManager = Current.Game.GetComponent<GameComponent_WaterDiffusion>();
            if (diffusionManager == null) return false;

            var frontier = _frontier;
            var visited = _visited;
            frontier.Clear();
            visited.Clear();

            frontier.Enqueue(new BfsNode(bfsStartCell, bfsStartMap));
            visited.Add(EncodeVisited(bfsStartMap, bfsStartCell));

            int maxDepth = settings.pressureMaxSearchDepth;
            int explored = 0;

            while (frontier.Count > 0 && explored < maxDepth)
            {
                BfsNode current = frontier.Dequeue();
                explored++;

                // === CROSS-MAP TRAVERSAL ===
                // 1. Holes / void terrain → lower map at same position
                if (VerticalPortalBridge.IsHoleAt(current.map, current.cell))
                {
                    if (VerticalPortalBridge.TryGetLowerMap(current.map, out Map lowerMap)
                        && current.cell.InBounds(lowerMap))
                    {
                        bool holeResult = TryCrossMap(volumeSource, lowerMap, current.cell,
                            frontier, visited, diffusionManager, settings, debug, explored);
                        if (holeResult) return true;
                    }
                }

                // 2. Stairs (MultiFloors) → destination cell on connected map
                if (MultiFloorsIntegration.IsAvailable && settings.stairWaterFlowEnabled)
                {
                    if (MultiFloorsIntegration.TryGetStairDestination(current.map, current.cell,
                        FlowingWater.MaxVolume, settings, out Map stairDestMap, out IntVec3 stairDestCell, out bool isDown))
                    {
                        bool stairResult = TryCrossMap(volumeSource, stairDestMap, stairDestCell,
                            frontier, visited, diffusionManager, settings, debug, explored);
                        if (stairResult) return true;
                    }
                }

                // Cardinal neighbors on current map
                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                {
                    IntVec3 neighbor = current.cell + offset;
                    if (!neighbor.InBounds(current.map))
                        continue;

                    long key = EncodeVisited(current.map, neighbor);
                    if (visited.Contains(key))
                        continue;
                    visited.Add(key);

                    // Check passability (walls, solid buildings block pressure)
                    if (!neighbor.Walkable(current.map))
                    {
                        TerrainDef t = current.map.terrainGrid.TerrainAt(neighbor);
                        if (t != TerrainDefOf.WaterShallow && t != TerrainDefOf.WaterDeep)
                            continue;
                    }

                    Building edifice = neighbor.GetEdifice(current.map);
                    if (edifice != null && edifice.def.fillPercent > 0.1f)
                        continue;

                    FlowingWater neighborWater = current.map.thingGrid.ThingAt<FlowingWater>(neighbor);

                    if (neighborWater != null)
                    {
                        if (neighborWater.Volume >= FlowingWater.MaxVolume)
                        {
                            frontier.Enqueue(new BfsNode(neighbor, current.map));
                        }
                        else
                        {
                            return DeliverToExisting(volumeSource, neighborWater, current.map, neighbor,
                                diffusionManager, settings, debug, explored);
                        }
                    }
                    else
                    {
                        return DeliverToEmpty(volumeSource, current.map, neighbor,
                            diffusionManager, settings, debug, explored);
                    }
                }
            }

            if (debug && explored >= maxDepth)
            {
                WaterSpringLogger.LogDebug($"[Pressure] BFS hit depth limit ({maxDepth}) from {bfsStartCell}. Pressure trapped.");
            }

            frontier.Clear();
            visited.Clear();
            return false;
        }

        /// <summary>
        /// Try to cross to another map at the given cell.
        /// </summary>
        private static bool TryCrossMap(FlowingWater volumeSource, Map destMap, IntVec3 destCell,
            Queue<BfsNode> frontier, HashSet<long> visited,
            GameComponent_WaterDiffusion dm, WaterSpringModSettings settings,
            bool debug, int explored)
        {
            long key = EncodeVisited(destMap, destCell);
            if (visited.Contains(key)) return false;
            visited.Add(key);

            FlowingWater destWater = destMap.thingGrid.ThingAt<FlowingWater>(destCell);
            if (destWater != null)
            {
                if (destWater.Volume >= FlowingWater.MaxVolume)
                {
                    frontier.Enqueue(new BfsNode(destCell, destMap));
                    return false;
                }
                else
                {
                    return DeliverToExisting(volumeSource, destWater, destMap, destCell, dm, settings, debug, explored);
                }
            }
            else
            {
                return DeliverToEmpty(volumeSource, destMap, destCell, dm, settings, debug, explored);
            }
        }

        /// <summary>
        /// Bulk-deliver to an existing non-full outlet.
        /// Hole sources can drain to 0; others keep 1.
        /// </summary>
        private static bool DeliverToExisting(FlowingWater source, FlowingWater target,
            Map targetMap, IntVec3 targetCell,
            GameComponent_WaterDiffusion dm, WaterSpringModSettings settings,
            bool debug, int explored)
        {
            _frontier.Clear();
            _visited.Clear();

            int capacity = FlowingWater.MaxVolume - target.Volume;
            bool sourceIsHole = source.Spawned && source.Map != null
                && VerticalPortalBridge.IsHoleAt(source.Map, source.Position);
            int available = sourceIsHole ? source.Volume : Math.Max(1, source.Volume - 1);
            int transfer = Math.Min(available, capacity);
            if (transfer < 1) return false;

            if (debug)
            {
                WaterSpringLogger.LogDebug($"[Pressure] BFS found outlet at {targetCell} map#{targetMap.uniqueID} (vol {target.Volume}) after {explored} tiles. Delivering {transfer} unit(s).");
            }

            target.AddVolume(transfer);
            source.Volume -= transfer;

            if (settings.useActiveTileSystem)
            {
                dm.RegisterActiveTile(targetMap, targetCell);
                target.ResetStabilityCounter();
            }

            return true;
        }

        /// <summary>
        /// Bulk-deliver to an empty passable tile (spawn new water).
        /// </summary>
        private static bool DeliverToEmpty(FlowingWater source,
            Map targetMap, IntVec3 targetCell,
            GameComponent_WaterDiffusion dm, WaterSpringModSettings settings,
            bool debug, int explored)
        {
            _frontier.Clear();
            _visited.Clear();

            ThingDef waterDef = DefDatabase<ThingDef>.GetNamed("FlowingWater", false);
            if (waterDef == null) return false;

            Thing newWater = ThingMaker.MakeThing(waterDef);
            if (newWater is FlowingWater typed)
            {
                bool sourceIsHole = source.Spawned && source.Map != null
                    && VerticalPortalBridge.IsHoleAt(source.Map, source.Position);
                int available = sourceIsHole ? source.Volume : Math.Max(1, source.Volume - 1);
                int transfer = Math.Min(available, FlowingWater.MaxVolume);
                if (transfer < 1) return false;

                typed.Volume = 0;
                GenSpawn.Spawn(newWater, targetCell, targetMap);
                typed.AddVolume(transfer);
                source.Volume -= transfer;

                if (debug)
                {
                    WaterSpringLogger.LogDebug($"[Pressure] BFS found empty outlet at {targetCell} map#{targetMap.uniqueID} after {explored} tiles. Delivered {transfer} unit(s).");
                }

                if (settings.useActiveTileSystem)
                {
                    dm.RegisterActiveTile(targetMap, targetCell);
                    typed.ResetStabilityCounter();
                }

                return true;
            }
            return false;
        }

        /// <summary>
        /// Gravity splash: BFS from startCell on startMap, collecting all tiles with capacity
        /// (non-full water + empty passable tiles). Distributes units equally across candidates.
        /// Full 7/7 tiles are conduits — BFS passes through them to find outlets beyond.
        /// Volume is subtracted from source. Returns true if any water was delivered.
        /// </summary>
        public static bool TrySplashDistribute(FlowingWater source, Map startMap, IntVec3 startCell,
            int units, WaterSpringModSettings settings, bool debug)
        {
            if (source == null || startMap == null || units <= 0) return false;

            var diffusionManager = Current.Game.GetComponent<GameComponent_WaterDiffusion>();
            if (diffusionManager == null) return false;

            var frontier = _frontier;
            var visited = _visited;
            var candidates = _splashCandidates;
            frontier.Clear();
            visited.Clear();
            candidates.Clear();

            int maxCandidates = settings.splashMaxOutlets;
            int maxDepth = settings.splashMaxDepth;
            int explored = 0;

            // Evaluate start cell first
            long startKey = EncodeVisited(startMap, startCell);
            visited.Add(startKey);

            FlowingWater startWater = startMap.thingGrid.ThingAt<FlowingWater>(startCell);
            if (startWater != null)
            {
                if (startWater.Volume < FlowingWater.MaxVolume)
                {
                    // Start cell has capacity — it's a candidate
                    candidates.Add(new SplashCandidate
                    {
                        cell = startCell, map = startMap,
                        water = startWater, capacity = FlowingWater.MaxVolume - startWater.Volume
                    });
                }
                else
                {
                    // Start cell is full — conduit, expand BFS from here
                    frontier.Enqueue(new BfsNode(startCell, startMap));
                }
            }
            else
            {
                // Empty tile — check passability
                if (IsCellPassableForSplash(startMap, startCell))
                {
                    candidates.Add(new SplashCandidate
                    {
                        cell = startCell, map = startMap,
                        water = null, capacity = FlowingWater.MaxVolume
                    });
                }
                else
                {
                    // Start cell is impassable — nothing to do
                    frontier.Clear();
                    visited.Clear();
                    candidates.Clear();
                    return false;
                }
            }

            // Expand BFS: start cell's cardinal neighbors, then conduit neighbors
            // If start cell was a candidate, still explore its neighbors for more outlets
            if (candidates.Count > 0 && candidates.Count < maxCandidates)
            {
                // Enqueue start for neighbor expansion even if it was a candidate
                frontier.Enqueue(new BfsNode(startCell, startMap));
            }

            while (frontier.Count > 0 && explored < maxDepth && candidates.Count < maxCandidates)
            {
                BfsNode current = frontier.Dequeue();
                explored++;

                // Cross-map: holes
                if (VerticalPortalBridge.IsHoleAt(current.map, current.cell))
                {
                    if (VerticalPortalBridge.TryGetLowerMap(current.map, out Map lowerMap)
                        && current.cell.InBounds(lowerMap))
                    {
                        TryAddSplashCandidate(lowerMap, current.cell, frontier, visited,
                            candidates, maxCandidates);
                    }
                }

                // Cross-map: stairs
                if (MultiFloorsIntegration.IsAvailable && settings.stairWaterFlowEnabled)
                {
                    if (MultiFloorsIntegration.TryGetStairDestination(current.map, current.cell,
                        FlowingWater.MaxVolume, settings, out Map stairDestMap, out IntVec3 stairDestCell, out bool isDown))
                    {
                        TryAddSplashCandidate(stairDestMap, stairDestCell, frontier, visited,
                            candidates, maxCandidates);
                    }
                }

                // Cardinal neighbors on current map
                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                {
                    if (candidates.Count >= maxCandidates) break;

                    IntVec3 neighbor = current.cell + offset;
                    if (!neighbor.InBounds(current.map)) continue;

                    TryAddSplashCandidate(current.map, neighbor, frontier, visited,
                        candidates, maxCandidates);
                }
            }

            // Distribute units across candidates
            if (candidates.Count == 0)
            {
                frontier.Clear();
                visited.Clear();
                candidates.Clear();
                return false;
            }

            int totalDelivered = 0;
            int count = candidates.Count;
            int perCandidate = units / count;
            int remainder = units % count;

            ThingDef waterDef = null;

            for (int i = 0; i < count; i++)
            {
                if (units - totalDelivered <= 0) break;

                var c = candidates[i];
                int share = perCandidate + (i < remainder ? 1 : 0);
                int deliver = Math.Min(share, c.capacity);
                deliver = Math.Min(deliver, units - totalDelivered); // don't exceed remaining
                if (deliver <= 0) continue;

                if (c.water != null)
                {
                    c.water.AddVolume(deliver);
                }
                else
                {
                    // Spawn new water on empty tile
                    if (waterDef == null)
                        waterDef = DefDatabase<ThingDef>.GetNamed("FlowingWater", false);
                    if (waterDef == null) continue;

                    Thing newThing = ThingMaker.MakeThing(waterDef);
                    if (newThing is FlowingWater typed)
                    {
                        typed.Volume = 0;
                        GenSpawn.Spawn(newThing, c.cell, c.map);
                        typed.AddVolume(deliver);
                    }
                    else continue;
                }

                totalDelivered += deliver;

                if (settings.useActiveTileSystem)
                {
                    diffusionManager.RegisterActiveTile(c.map, c.cell);
                    c.water?.ResetStabilityCounter();
                }
            }

            if (totalDelivered > 0)
            {
                source.Volume -= totalDelivered;

                if (settings.useActiveTileSystem && source.Spawned && source.Map != null)
                {
                    diffusionManager.RegisterActiveTile(source.Map, source.Position);
                    source.ResetStabilityCounter();
                }

                if (debug)
                {
                    WaterSpringLogger.LogDebug($"[Splash] Distributed {totalDelivered} units from {source.Position} across {count} candidates (explored {explored} tiles).");
                }
            }

            frontier.Clear();
            visited.Clear();
            candidates.Clear();
            return totalDelivered > 0;
        }

        /// <summary>
        /// Evaluate a cell for splash distribution: non-full water or empty passable → candidate.
        /// Full 7/7 → conduit (enqueue for BFS expansion). Walls/impassable → skip.
        /// </summary>
        private static void TryAddSplashCandidate(Map map, IntVec3 cell,
            Queue<BfsNode> frontier, HashSet<long> visited,
            List<SplashCandidate> candidates, int maxCandidates)
        {
            long key = EncodeVisited(map, cell);
            if (visited.Contains(key)) return;
            visited.Add(key);

            // Check passability
            if (!cell.Walkable(map))
            {
                TerrainDef t = map.terrainGrid.TerrainAt(cell);
                if (t != TerrainDefOf.WaterShallow && t != TerrainDefOf.WaterDeep)
                    return;
            }

            Building edifice = cell.GetEdifice(map);
            if (edifice != null && edifice.def.fillPercent > 0.1f)
                return;

            FlowingWater water = map.thingGrid.ThingAt<FlowingWater>(cell);

            if (water != null)
            {
                if (water.Volume >= FlowingWater.MaxVolume)
                {
                    // Full tile = conduit — expand BFS through it
                    frontier.Enqueue(new BfsNode(cell, map));
                }
                else if (candidates.Count < maxCandidates)
                {
                    candidates.Add(new SplashCandidate
                    {
                        cell = cell, map = map,
                        water = water, capacity = FlowingWater.MaxVolume - water.Volume
                    });
                }
            }
            else if (candidates.Count < maxCandidates)
            {
                // Empty passable tile
                candidates.Add(new SplashCandidate
                {
                    cell = cell, map = map,
                    water = null, capacity = FlowingWater.MaxVolume
                });
            }
        }

        /// <summary>
        /// Check if a cell is passable for splash distribution (water terrain counts as passable).
        /// </summary>
        private static bool IsCellPassableForSplash(Map map, IntVec3 cell)
        {
            if (!cell.Walkable(map))
            {
                TerrainDef t = map.terrainGrid.TerrainAt(cell);
                if (t != TerrainDefOf.WaterShallow && t != TerrainDefOf.WaterDeep)
                    return false;
            }
            Building edifice = cell.GetEdifice(map);
            if (edifice != null && edifice.def.fillPercent > 0.1f)
                return false;
            return true;
        }

        /// <summary>
        /// Encode (map, cell) pair into a unique long for the visited set.
        /// </summary>
        private static long EncodeVisited(Map map, IntVec3 cell)
        {
            long mapPart = (long)map.uniqueID * 1_000_000L;
            long cellPart = (long)map.cellIndices.CellToIndex(cell);
            return mapPart + cellPart;
        }

        /// <summary>
        /// Equalization: BFS from startCell to find a connected water region on the same map,
        /// then redistribute volumes to the maximum-entropy distribution (floor(V/N) and ceil(V/N)).
        /// Fixes staircase gradients that local pairwise diffusion cannot resolve.
        /// Returns true if any volumes were changed.
        /// </summary>
        public static bool TryEqualizeRegion(
            Map map, IntVec3 startCell, HashSet<int> globalVisited,
            WaterSpringModSettings settings, bool debug)
        {
            if (map == null || settings == null) return false;
            if (!startCell.InBounds(map)) return false;

            int startIndex = map.cellIndices.CellToIndex(startCell);
            if (globalVisited.Contains(startIndex)) return false;

            FlowingWater startWater = map.thingGrid.ThingAt<FlowingWater>(startCell);
            if (startWater == null || !startWater.Spawned) return false;

            var frontier = _eqFrontier;
            var region = _eqRegion;
            frontier.Clear();
            region.Clear();

            int maxRegion = settings.equalizationMaxRegionSize;

            // Seed BFS
            globalVisited.Add(startIndex);
            region.Add(startWater);
            frontier.Enqueue(startCell);

            int minVol = startWater.Volume;
            int maxVol = startWater.Volume;
            int totalVol = startWater.Volume;

            // BFS loop
            while (frontier.Count > 0 && region.Count < maxRegion)
            {
                IntVec3 current = frontier.Dequeue();

                for (int d = 0; d < 4; d++)
                {
                    IntVec3 neighbor = current + GenAdj.CardinalDirections[d];
                    if (!neighbor.InBounds(map)) continue;

                    int nIndex = map.cellIndices.CellToIndex(neighbor);
                    if (globalVisited.Contains(nIndex)) continue;
                    globalVisited.Add(nIndex);

                    FlowingWater nWater = map.thingGrid.ThingAt<FlowingWater>(neighbor);
                    if (nWater == null || !nWater.Spawned) continue;

                    region.Add(nWater);
                    frontier.Enqueue(neighbor);

                    int v = nWater.Volume;
                    totalVol += v;
                    if (v < minVol) minVol = v;
                    if (v > maxVol) maxVol = v;
                }
            }

            // Early-out: single tile or already at global equilibrium
            int count = region.Count;
            if (count <= 1 || (maxVol - minVol) <= 1)
            {
                frontier.Clear();
                region.Clear();
                return false;
            }

            // Compute maximum-entropy distribution
            int baseVol = totalVol / count;
            int remainder = totalVol % count;
            bool anyChanged = false;

            // Shuffle region to distribute remainder randomly (avoid BFS center-bias ring)
            for (int i = count - 1; i > 0; i--)
            {
                int j = Rand.RangeInclusive(0, i);
                if (j != i)
                {
                    FlowingWater tmp = region[i];
                    region[i] = region[j];
                    region[j] = tmp;
                }
            }

            for (int i = 0; i < count; i++)
            {
                int target = baseVol + (i < remainder ? 1 : 0);
                FlowingWater w = region[i];
                if (w.Volume != target)
                {
                    w.Volume = target; // setter handles clamp, terrain sync, activation, neighbor wake, destroy at 0
                    anyChanged = true;
                }
            }

            if (debug && anyChanged)
            {
                WaterSpringLogger.LogDebug($"[Equalize] Region of {count} tiles from {startCell}: total={totalVol}, base={baseVol}, remainder={remainder}, range was [{minVol},{maxVol}]");
            }

            frontier.Clear();
            region.Clear();
            return anyChanged;
        }
    }
}
