using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using UnityEngine;

namespace WaterSpringMod.WaterSpring
{
    // GameComponent for centralized water diffusion management
    public class GameComponent_WaterDiffusion : GameComponent
    {
    private const int MaxVolumeLabelsPerFrame = 256; // safeguard for on-GUI text cost

        // Spatial index for efficient lookups (contains dirty flags)
        private Dictionary<Map, ChunkBasedSpatialIndex> spatialIndices =
            new Dictionary<Map, ChunkBasedSpatialIndex>();

    // Performance & stats
    private int ticksAtFullCapacity = 0;
    private int dirtyChunkCount = 0;
    private int tilesProcessedLastTick = 0;
    // Reusable scratch buffers to reduce per-tick allocations
    private readonly List<ChunkCoordinate> scratchChunks = new List<ChunkCoordinate>(256);
    private readonly List<IntVec3> scratchChunkTiles = new List<IntVec3>(1024);
    // Dedicated BFS structures for pressure propagation (separate from terrain-change BFS)
    internal readonly Queue<IntVec3> pressureBfsFrontier = new Queue<IntVec3>(64);
    internal readonly HashSet<IntVec3> pressureBfsVisited = new HashSet<IntVec3>();
    // Equalization pass scratch buffers
    private readonly HashSet<int> eqVisited = new HashSet<int>(1024);
    private readonly List<IntVec3> eqPositionSnapshot = new List<IntVec3>(512);
    // Scratch list for drawing to avoid concurrent modification of sets during enumeration
    private readonly List<IntVec3> scratchDrawTiles = new List<IntVec3>(2048);

    // Global tick/frequency trackers
    private int tickCounter = 0;
    private float lastTPS = 0f;
    private float tpsAccumulator = 0f;
    private int tpsSampleCount = 0;
    private int ticksSinceLastUpdate = 0;
    // Logging throttles
    private float lastTPSLogged = -1f;
    private int lastTPSLogTick = -1000000;
    private int lastThrottleLogTick = -1000000;

        // Debug visualization
        public bool ShowActiveWaterDebug = false;

        // Settings access helper
        private WaterSpringModSettings Settings => LoadedModManager.GetMod<WaterSpringModMain>().settings;

        public GameComponent_WaterDiffusion(Game game) : base()
        {
            if (Settings.debugModeEnabled)
            {
                WaterSpringLogger.LogDebug("GameComponent_WaterDiffusion initialized");
            }
        }

        // Get spatial index for a specific map
        public ChunkBasedSpatialIndex GetSpatialIndex(Map map)
        {
            if (map == null)
            {
                WaterSpringLogger.LogError("Attempted to get spatial index for null map");
                return null;
            }

            if (!spatialIndices.ContainsKey(map))
            {
                int chunkSize = Settings.chunkSize;
                spatialIndices[map] = new ChunkBasedSpatialIndex(map, chunkSize);
                if (Settings.debugModeEnabled)
                {
                    WaterSpringLogger.LogDebug($"Created new spatial index for map {map.uniqueID} with chunk size {chunkSize}");
                }
            }

            return spatialIndices[map];
        }

        // Convenience: mark chunk dirty at a position on a given map
        public void MarkChunkDirtyAt(Map map, IntVec3 pos)
        {
            if (map == null) return;
            GetSpatialIndex(map)?.MarkChunkDirtyAt(pos);
        }

        // Get water at position using the spatial index (faster lookup)
        public FlowingWater GetWaterAt(Map map, IntVec3 position)
        {
            if (map == null) return null;
            return GetSpatialIndex(map)?.GetWaterAt(position)
                ?? map.thingGrid.ThingAt<FlowingWater>(position);
        }

        // Get count of tiles in dirty chunks for debug/stats
        public int GetActiveWaterTileCount(Map map)
        {
            var si = GetSpatialIndex(map);
            if (si == null) return 0;
            int count = 0;
            foreach (var chunk in si.GetDirtyChunks())
            {
                var tiles = si.GetWaterTilesInChunk(chunk);
                if (tiles != null) count += tiles.Count;
            }
            return count;
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();

            spatialIndices.Clear();

            // Initialize for each current map
            foreach (Map map in Find.Maps)
            {
                ChunkBasedSpatialIndex spatialIndex = GetSpatialIndex(map);
                spatialIndex.RebuildFromMap();
                spatialIndex.MarkAllDirty(); // mark everything dirty for initial pass
            }

            WaterSpringLogger.LogDebug("GameComponent_WaterDiffusion finalized initialization");
        }

        // Method to handle walls/buildings being added or removed
        public void NotifyTerrainChanged(Map map, IntVec3 position)
        {
            if (map == null) return;
            // Vertical wake for this location and its cardinals when terrain changes
            VerticalPortalBridge.PropagateVerticalActivationForCellAndCardinals(map, position);

            // Mark affected chunk and neighbors dirty, clear static on nearby water
            MarkChunkDirtyAt(map, position);
            foreach (IntVec3 dir in GenAdj.CardinalDirections)
            {
                IntVec3 adj = position + dir;
                if (!adj.InBounds(map)) continue;
                MarkChunkDirtyAt(map, adj);
                var w = map.thingGrid.ThingAt<FlowingWater>(adj);
                if (w != null) w.ClearStatic();
            }

            // Also check 8 neighbors (diagonals for completeness)
            foreach (IntVec3 offset in GenAdj.AdjacentCellsAndInside)
            {
                if (offset.x == 0 && offset.z == 0) continue;
                IntVec3 adjacentPos = position + offset;
                if (!adjacentPos.InBounds(map)) continue;
                FlowingWater water = map.thingGrid.ThingAt<FlowingWater>(adjacentPos);
                if (water != null)
                {
                    water.ClearStatic();
                    MarkChunkDirtyAt(map, adjacentPos);
                }
            }

            // MultiFloors: propagate to levels above/below
            if (MultiFloorsIntegration.IsAvailable && MultiFloorsIntegration.IsMultiLevel(map))
            {
                VerticalPortalBridge.PropagateVerticalActivationForCellAndCardinals(map, position);

                int currentLevel = MultiFloorsIntegration.GetLevel(map);
                if (currentLevel > 0)
                {
                    if (MultiFloorsIntegration.TryGetLowerMap(map, out Map lowerMap))
                    {
                        if (position.InBounds(lowerMap))
                        {
                            var waterBelow = lowerMap.thingGrid.ThingAt<FlowingWater>(position);
                            if (waterBelow != null) waterBelow.ClearStatic();
                            MarkChunkDirtyAt(lowerMap, position);
                        }
                    }
                }
            }
        }

        // Treat water terrain as passable for water flow
        private bool IsCellPassableForWater(Map map, IntVec3 cell)
        {
            if (cell.Walkable(map)) return true;
            var t = map.terrainGrid?.TerrainAt(cell);
            if (t != null && t.IsWater) return true;
            return false;
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            // Update tick counter for frequency-based processing
            tickCounter++;
            ticksSinceLastUpdate++;

            // Periodic equalization pass (entropy fix for staircase gradients)
            if (Settings.equalizationEnabled && tickCounter % Settings.equalizationIntervalTicks == 0)
            {
                PerformEqualizationPass();
            }

            // Delegate to frequency gate so global frequency/adaptive TPS are respected
            CheckProcessingFrequency();
        }

        private void CheckProcessingFrequency()
        {
            bool shouldProcessThisTick = true;

            if (Settings.useFrequencyBasedProcessing)
            {
                shouldProcessThisTick = (tickCounter % Settings.globalUpdateFrequency == 0);

                if (Settings.useAdaptiveTPS && Find.TickManager != null)
                {
                    float currentTPS = Find.TickManager.TickRateMultiplier * 60f;
                    tpsAccumulator += currentTPS;
                    tpsSampleCount++;

                    if (tpsSampleCount >= 60)
                    {
                        lastTPS = tpsAccumulator / tpsSampleCount;
                        tpsAccumulator = 0f;
                        tpsSampleCount = 0;
                        if (Settings.debugModeEnabled)
                        {
                            bool enoughTime = (tickCounter - lastTPSLogTick) >= 120;
                            float delta = Mathf.Abs(lastTPS - lastTPSLogged);
                            bool significant = delta >= 3.0f || (lastTPSLogged > 0f && (delta / lastTPSLogged) >= 0.05f);
                            if (enoughTime && significant)
                            {
                                WaterSpringLogger.LogDebug($"Current TPS: {lastTPS:F1}, Min Target: {Settings.minTPS:F1}");
                                lastTPSLogged = lastTPS;
                                lastTPSLogTick = tickCounter;
                            }
                        }
                    }

                    if (lastTPS < Settings.minTPS && lastTPS > 0)
                    {
                        float tpsDelta = Settings.minTPS - lastTPS;
                        int additionalDelay = Mathf.CeilToInt(tpsDelta * 0.5f);
                        shouldProcessThisTick = shouldProcessThisTick && (ticksSinceLastUpdate >= (Settings.globalUpdateFrequency + additionalDelay));
                        if (shouldProcessThisTick && Settings.debugModeEnabled)
                        {
                            if ((tickCounter - lastThrottleLogTick) >= 300)
                            {
                                WaterSpringLogger.LogDebug($"Throttling water processing due to low TPS. Additional delay: {additionalDelay} ticks");
                                lastThrottleLogTick = tickCounter;
                            }
                        }
                    }
                }
            }

            if (shouldProcessThisTick)
            {
                ticksSinceLastUpdate = 0;
                foreach (Map map in Find.Maps)
                {
                    ProcessDirtyChunks(map);
                }
            }
        }

        // New dirty-chunk processing loop (replaces both old tile-based and chunk-based methods)
        private void ProcessDirtyChunks(Map map)
        {
            tilesProcessedLastTick = 0;
            var spatialIndex = GetSpatialIndex(map);
            if (spatialIndex == null) return;

            var dirtyChunks = spatialIndex.GetDirtyChunks();
            dirtyChunkCount = dirtyChunks.Count;
            if (dirtyChunks.Count == 0) return;

            // Snapshot dirty chunks (they may be modified during processing)
            scratchChunks.Clear();
            scratchChunks.AddRange(dirtyChunks);

            int chunksProcessed = 0;
            int maxChunks = Settings.maxProcessedChunksPerTick;
            int maxTiles = Settings.maxProcessedTilesPerTick;

            if (scratchChunks.Count > maxChunks)
            {
                ticksAtFullCapacity++;
                if (ticksAtFullCapacity % 60 == 0)
                {
                    WaterSpringLogger.LogWarning($"Water chunk system at processing capacity for {ticksAtFullCapacity} ticks. Dirty chunks: {scratchChunks.Count}, Max: {maxChunks}");
                }
            }
            else
            {
                ticksAtFullCapacity = 0;
            }

            for (int i = 0; i < scratchChunks.Count && chunksProcessed < maxChunks; i++)
            {
                // Fisher-Yates shuffle for random sampling
                int ri = Rand.Range(i, scratchChunks.Count);
                if (ri != i) { var tmp = scratchChunks[i]; scratchChunks[i] = scratchChunks[ri]; scratchChunks[ri] = tmp; }

                ChunkCoordinate chunk = scratchChunks[i];
                var tiles = spatialIndex.GetWaterTilesInChunk(chunk);
                if (tiles == null || tiles.Count == 0)
                {
                    spatialIndex.ClearChunkDirty(chunk);
                    continue;
                }

                bool chunkHadNonStatic = false;
                scratchChunkTiles.Clear();
                scratchChunkTiles.AddRange(tiles);

                foreach (IntVec3 pos in scratchChunkTiles)
                {
                    if (tilesProcessedLastTick >= maxTiles) return;

                    FlowingWater water = GetWaterAt(map, pos);
                    if (water == null)
                    {
                        spatialIndex.UnregisterWaterTile(pos);
                        continue;
                    }

                    if (water.IsStatic) continue; // Skip static tiles within dirty chunk
                    chunkHadNonStatic = true;

                    if (water.ticksUntilNextCheck > 0)
                    {
                        water.ticksUntilNextCheck--;
                        tilesProcessedLastTick++;
                        continue;
                    }

                    bool changed = water.AttemptLocalDiffusion();
                    if (!changed)
                    {
                        // DF model: immediate static after one no-change tick
                        // Spring source tiles with neverStabilize should never go static
                        if (!(water.IsSpringSourceTile && Settings.springNeverStabilize))
                        {
                            water.SetStatic();
                        }
                    }
                    // If changed: Volume setter already marked chunks dirty + cleared static

                    // Set next check interval
                    int minI = Mathf.Max(1, Settings.localCheckIntervalMin);
                    int maxI = Mathf.Max(minI, Settings.localCheckIntervalMax);
                    water.ticksUntilNextCheck = Rand.Range(minI, maxI);
                    tilesProcessedLastTick++;
                }

                // If no non-static tiles found in this chunk, clear dirty flag
                if (!chunkHadNonStatic)
                {
                    spatialIndex.ClearChunkDirty(chunk);
                }

                chunksProcessed++;
            }
        }

        /// <summary>
        /// Periodic equalization: iterate all FlowingWater on each map, BFS connected regions,
        /// and redistribute to maximum-entropy distribution. Fixes staircase gradients.
        /// </summary>
        private void PerformEqualizationPass()
        {
            bool debug = Settings.debugModeEnabled;
            ThingDef waterDef = DefDatabase<ThingDef>.GetNamed("FlowingWater", false);
            if (waterDef == null) return;

            foreach (Map map in Find.Maps)
            {
                eqVisited.Clear();

                List<Thing> allWater = map.listerThings.ThingsOfDef(waterDef);
                if (allWater == null || allWater.Count == 0) continue;

                // Snapshot positions to avoid concurrent modification if Volume=0 triggers Destroy
                eqPositionSnapshot.Clear();
                for (int i = 0; i < allWater.Count; i++)
                {
                    eqPositionSnapshot.Add(allWater[i].Position);
                }

                for (int i = 0; i < eqPositionSnapshot.Count; i++)
                {
                    PressurePropagation.TryEqualizeRegion(map, eqPositionSnapshot[i], eqVisited, Settings, debug);
                }
            }
        }

        // Debug visualization of active water tiles
        public override void GameComponentOnGUI()
        {
            base.GameComponentOnGUI();
            #if WATERPHYSICS_DEV
            PerfHarness.OnGUI();
            #endif

            // Debug toggle shortcut (Alt+W)
            if (Prefs.DevMode && Current.Game != null && Event.current.type == EventType.KeyDown &&
                Event.current.alt && Event.current.keyCode == KeyCode.W)
            {
                ShowActiveWaterDebug = !ShowActiveWaterDebug;
                Messages.Message($"Active water debug visualization: {(ShowActiveWaterDebug ? "ON" : "OFF")}", MessageTypeDefOf.NeutralEvent);
                Event.current.Use();
            }

            if ((ShowActiveWaterDebug || Settings.showPerformanceStats) && Find.CurrentMap != null)
            {
                var si = GetSpatialIndex(Find.CurrentMap);
                int dirtyCount = si?.DirtyChunkCount ?? 0;
                int tileCount = GetActiveWaterTileCount(Find.CurrentMap);

                if (ShowActiveWaterDebug)
                {
                    if (Event.current.type == EventType.Repaint)
                    {
                        float y = Settings.showPerformanceStats ? 30f : 10f;
                        Widgets.Label(new Rect(10f, y, 420f, 22f), $"[WaterPhysics] Dirty chunks: {dirtyCount}, Tiles in dirty: {tileCount} (overlay ON)");

                        // Collect tiles in dirty chunks for visualization
                        scratchDrawTiles.Clear();
                        if (si != null)
                        {
                            foreach (var chunk in si.GetDirtyChunks())
                            {
                                var tiles = si.GetWaterTilesInChunk(chunk);
                                if (tiles != null) scratchDrawTiles.AddRange(tiles);
                            }
                        }

                        if (scratchDrawTiles.Count > 0)
                        {
                            GenDraw.DrawFieldEdges(scratchDrawTiles);
                            int maxFlash = Mathf.Min(scratchDrawTiles.Count, 512);
                            for (int i = 0; i < maxFlash; i++)
                            {
                                Find.CurrentMap.debugDrawer.FlashCell(scratchDrawTiles[i], 0.15f);
                            }

                            int drawn = 0;
                            var map = Find.CurrentMap;
                            var oldFont = Text.Font;
                            var oldAnchor = Text.Anchor;
                            Text.Font = GameFont.Tiny;
                            Text.Anchor = TextAnchor.MiddleCenter;
                            for (int i = 0; i < scratchDrawTiles.Count && drawn < MaxVolumeLabelsPerFrame; i++)
                            {
                                IntVec3 c = scratchDrawTiles[i];
                                FlowingWater w = GetWaterAt(map, c);
                                if (w == null) continue;
                                string txt = w.Volume.ToString();
                                Vector3 world = c.ToVector3Shifted();
                                GenMapUI.DrawThingLabel(world, txt, Color.yellow);
                                Vector2 uiPos = GenMapUI.LabelDrawPosFor(c);
                                Vector2 size = Text.CalcSize(txt);
                                Rect r = new Rect(uiPos.x - size.x * 0.5f, uiPos.y - size.y * 0.5f, size.x, size.y);
                                var oldColor = GUI.color;
                                GUI.color = Color.yellow;
                                Widgets.Label(r, txt);
                                GUI.color = oldColor;
                                drawn++;
                            }
                            Text.Font = oldFont;
                            Text.Anchor = oldAnchor;
                        }

                        // Draw dirty chunk boundaries
                        if (si != null)
                        {
                            foreach (ChunkCoordinate chunk in si.GetDirtyChunks())
                            {
                                IntVec3 min = chunk.MinCorner;
                                IntVec3 max = chunk.MaxCorner;
                                Vector3 v1 = new Vector3(min.x, AltitudeLayer.MetaOverlays.AltitudeFor(), min.z);
                                Vector3 v2 = new Vector3(max.x + 1, AltitudeLayer.MetaOverlays.AltitudeFor(), min.z);
                                Vector3 v3 = new Vector3(max.x + 1, AltitudeLayer.MetaOverlays.AltitudeFor(), max.z + 1);
                                Vector3 v4 = new Vector3(min.x, AltitudeLayer.MetaOverlays.AltitudeFor(), max.z + 1);
                                GenDraw.DrawLineBetween(v1, v2, SimpleColor.Green);
                                GenDraw.DrawLineBetween(v2, v3, SimpleColor.Green);
                                GenDraw.DrawLineBetween(v3, v4, SimpleColor.Green);
                                GenDraw.DrawLineBetween(v4, v1, SimpleColor.Green);
                            }
                        }

                        // Draw static water tiles with blue circles
                        if (Settings.showDetailedDebug)
                        {
                            List<Thing> allWaterTiles = Find.CurrentMap.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("FlowingWater"));
                            foreach (Thing t in allWaterTiles)
                            {
                                if (t is FlowingWater water && water.IsStatic)
                                {
                                    Vector3 drawPos = water.Position.ToVector3Shifted();
                                    drawPos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                                    GenDraw.DrawCircleOutline(drawPos, 0.4f, SimpleColor.Blue);
                                }
                            }
                        }
                    }
                }

                // Always show stats if performance monitoring is enabled
                if (Settings.showPerformanceStats)
                {
                    Widgets.Label(new Rect(10, 10, 400, 20),
                        $"Dirty chunks: {dirtyCount}, Tiles in dirty: {tileCount}, Processed: {tilesProcessedLastTick}");

                    if (ticksAtFullCapacity > 0)
                    {
                        Widgets.Label(new Rect(10, 30, 300, 20),
                            $"WARNING: At processing capacity for {ticksAtFullCapacity} ticks");
                    }
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ShowActiveWaterDebug, "showActiveWaterDebug", false);
            Scribe_Values.Look(ref ticksAtFullCapacity, "ticksAtFullCapacity", 0);
            Scribe_Values.Look(ref tickCounter, "tickCounter", 0);
            Scribe_Values.Look(ref lastTPS, "lastTPS", 0f);
        }
    }
}
