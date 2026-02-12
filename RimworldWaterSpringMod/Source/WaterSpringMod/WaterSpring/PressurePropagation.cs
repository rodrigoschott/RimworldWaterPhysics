using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WaterSpringMod.WaterSpring
{
    /// <summary>
    /// DF-inspired pressure propagation: BFS through connected 7/7 tiles
    /// to find the nearest non-full outlet and teleport water there.
    /// </summary>
    public static class PressurePropagation
    {
        /// <summary>
        /// Attempt to propagate water from a full (MaxVolume) source tile through connected
        /// full tiles to the nearest outlet. Returns true if water was placed.
        /// </summary>
        public static bool TryPropagate(FlowingWater source, Map map, WaterSpringModSettings settings, bool debug)
        {
            if (source == null || map == null) return false;
            if (source.Volume < FlowingWater.MaxVolume) return false;
            if (!settings.pressurePropagationEnabled) return false;

            var diffusionManager = Current.Game.GetComponent<GameComponent_WaterDiffusion>();
            if (diffusionManager == null) return false;

            // Use dedicated scratch buffers (not shared with terrain-change BFS)
            var frontier = diffusionManager.pressureBfsFrontier;
            var visited = diffusionManager.pressureBfsVisited;
            frontier.Clear();
            visited.Clear();

            IntVec3 sourcePos = source.Position;
            frontier.Enqueue(sourcePos);
            visited.Add(sourcePos);

            int maxDepth = settings.pressureMaxSearchDepth;
            int explored = 0;

            // BFS through connected 7/7 tiles (cardinal only, same map)
            while (frontier.Count > 0 && explored < maxDepth)
            {
                IntVec3 current = frontier.Dequeue();
                explored++;

                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                {
                    IntVec3 neighbor = current + offset;
                    if (!neighbor.InBounds(map) || visited.Contains(neighbor))
                        continue;

                    visited.Add(neighbor);

                    // Check passability (walls, solid buildings block pressure)
                    if (!neighbor.Walkable(map))
                    {
                        // Allow water terrain
                        TerrainDef t = map.terrainGrid.TerrainAt(neighbor);
                        if (t != TerrainDefOf.WaterShallow && t != TerrainDefOf.WaterDeep)
                            continue;
                    }

                    Building edifice = neighbor.GetEdifice(map);
                    if (edifice != null && edifice.def.fillPercent > 0.1f)
                        continue;

                    FlowingWater neighborWater = map.thingGrid.ThingAt<FlowingWater>(neighbor);

                    if (neighborWater != null)
                    {
                        if (neighborWater.Volume >= FlowingWater.MaxVolume)
                        {
                            // Full tile — continue BFS through it
                            frontier.Enqueue(neighbor);
                        }
                        else
                        {
                            // Partial tile — OUTLET FOUND
                            int capacity = FlowingWater.MaxVolume - neighborWater.Volume;
                            int transfer = Math.Min(1, capacity); // Pressure delivers 1 unit per event

                            if (debug)
                            {
                                WaterSpringLogger.LogDebug($"[Pressure] BFS found outlet at {neighbor} (vol {neighborWater.Volume}) after {explored} tiles. Delivering {transfer} unit(s).");
                            }

                            neighborWater.AddVolume(transfer);
                            source.Volume -= transfer;

                            // Wake the outlet tile
                            if (settings.useActiveTileSystem)
                            {
                                diffusionManager.RegisterActiveTile(map, neighbor);
                                neighborWater.ResetStabilityCounter();
                            }

                            frontier.Clear();
                            visited.Clear();
                            return true;
                        }
                    }
                    else
                    {
                        // Empty passable tile — OUTLET FOUND (spawn new water)
                        ThingDef waterDef = DefDatabase<ThingDef>.GetNamed("FlowingWater", false);
                        if (waterDef != null)
                        {
                            Thing newWater = ThingMaker.MakeThing(waterDef);
                            if (newWater is FlowingWater typed)
                            {
                                typed.Volume = 0;
                                GenSpawn.Spawn(newWater, neighbor, map);
                                typed.AddVolume(1);
                                source.Volume -= 1;

                                if (debug)
                                {
                                    WaterSpringLogger.LogDebug($"[Pressure] BFS found empty outlet at {neighbor} after {explored} tiles. Spawned new water.");
                                }

                                if (settings.useActiveTileSystem)
                                {
                                    diffusionManager.RegisterActiveTile(map, neighbor);
                                    typed.ResetStabilityCounter();
                                }

                                frontier.Clear();
                                visited.Clear();
                                return true;
                            }
                        }
                    }
                }
            }

            if (debug && explored >= maxDepth)
            {
                WaterSpringLogger.LogDebug($"[Pressure] BFS hit depth limit ({maxDepth}) from {sourcePos}. Pressure trapped.");
            }

            // No outlet found — pressure is trapped. Tile stays full and stabilizes.
            frontier.Clear();
            visited.Clear();
            return false;
        }
    }
}
