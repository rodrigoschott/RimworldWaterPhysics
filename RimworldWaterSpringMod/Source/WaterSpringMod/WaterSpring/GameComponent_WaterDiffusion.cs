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
        // Central registry of active water tiles by map
        private Dictionary<Map, HashSet<IntVec3>> activeWaterTilesByMap = new Dictionary<Map, HashSet<IntVec3>>();
        
        // Chunk-based organization of water tiles
        private Dictionary<Map, Dictionary<ChunkCoordinate, HashSet<IntVec3>>> waterTilesByChunk = 
            new Dictionary<Map, Dictionary<ChunkCoordinate, HashSet<IntVec3>>>();
        private Dictionary<Map, HashSet<ChunkCoordinate>> activeChunks = 
            new Dictionary<Map, HashSet<ChunkCoordinate>>();
            
        // Spatial index for efficient lookups
        private Dictionary<Map, ChunkBasedSpatialIndex> spatialIndices = 
            new Dictionary<Map, ChunkBasedSpatialIndex>();
            
    // Performance & stats
    private int ticksAtFullCapacity = 0;
    private int activeChunkCount = 0;
    private int tilesProcessedLastTick = 0;
    private int activeWaterTileCount = 0;
    // Reentrancy guard for ReactivateInRadius
    private bool reactivatingNow = false;
    // Reusable scratch buffers to reduce per-tick allocations
    private readonly List<IntVec3> scratchTiles = new List<IntVec3>(1024);
    private readonly HashSet<IntVec3> scratchTilesToRemove = new HashSet<IntVec3>();
    private readonly List<ChunkCoordinate> scratchChunks = new List<ChunkCoordinate>(256);
    private readonly HashSet<ChunkCoordinate> scratchChunksToRemove = new HashSet<ChunkCoordinate>();
    private readonly List<IntVec3> scratchChunkTiles = new List<IntVec3>(1024);
    private readonly HashSet<IntVec3> scratchChunkRemovals = new HashSet<IntVec3>();
    // Reusable BFS structures for terrain-change waves
    private readonly Queue<IntVec3> bfsFrontier = new Queue<IntVec3>(256);
    private readonly HashSet<IntVec3> bfsVisited = new HashSet<IntVec3>();
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

        // Get active water tiles for a specific map
        public HashSet<IntVec3> GetActiveWaterTiles(Map map)
        {
            if (map == null)
            {
                WaterSpringLogger.LogError("Attempted to get active water tiles for null map");
                return new HashSet<IntVec3>();
            }
            
            if (!activeWaterTilesByMap.ContainsKey(map))
            {
                activeWaterTilesByMap[map] = new HashSet<IntVec3>();
                if (Settings.debugModeEnabled)
                {
                    WaterSpringLogger.LogDebug($"Created new active water tile registry for map {map.uniqueID}");
                }
            }
            
            return activeWaterTilesByMap[map];
        }
        
        // Get all active chunks for a specific map
        public HashSet<ChunkCoordinate> GetActiveChunks(Map map)
        {
            if (map == null)
            {
                WaterSpringLogger.LogError("Attempted to get active chunks for null map");
                return new HashSet<ChunkCoordinate>();
            }
            
            if (!activeChunks.ContainsKey(map))
            {
                activeChunks[map] = new HashSet<ChunkCoordinate>();
                if (Settings.debugModeEnabled)
                {
                    WaterSpringLogger.LogDebug($"Created new active chunks registry for map {map.uniqueID}");
                }
            }
            
            return activeChunks[map];
        }
        
        // Get water tiles by chunk for a specific map
        private Dictionary<ChunkCoordinate, HashSet<IntVec3>> GetWaterTilesByChunk(Map map)
        {
            if (map == null)
            {
                WaterSpringLogger.LogError("Attempted to get water tiles by chunk for null map");
                return new Dictionary<ChunkCoordinate, HashSet<IntVec3>>();
            }
            
            if (!waterTilesByChunk.ContainsKey(map))
            {
                waterTilesByChunk[map] = new Dictionary<ChunkCoordinate, HashSet<IntVec3>>();
                if (Settings.debugModeEnabled)
                {
                    WaterSpringLogger.LogDebug($"Created new water tiles by chunk registry for map {map.uniqueID}");
                }
            }
            
            return waterTilesByChunk[map];
        }
        
        // Get water tiles for a specific chunk
        public HashSet<IntVec3> GetWaterTilesInChunk(Map map, ChunkCoordinate chunkCoord)
        {
            var chunksForMap = GetWaterTilesByChunk(map);
            
            if (!chunksForMap.ContainsKey(chunkCoord))
            {
                chunksForMap[chunkCoord] = new HashSet<IntVec3>();
            }
            
            return chunksForMap[chunkCoord];
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
        
    // Multi-phase transfer manager removed
        
        // Get water at position using the spatial index (faster lookup)
        public FlowingWater GetWaterAt(Map map, IntVec3 position)
        {
            if (map == null || !Settings.useChunkBasedProcessing) 
            {
                // Fall back to standard lookup if chunk processing is disabled
                return map?.thingGrid.ThingAt<FlowingWater>(position);
            }
            
            return GetSpatialIndex(map)?.GetWaterAt(position);
        }
        
        // Register a chunk as active
        private void RegisterActiveChunk(Map map, ChunkCoordinate chunkCoord)
        {
            if (map == null) return;
            
            GetActiveChunks(map).Add(chunkCoord);
            if (Settings.debugModeEnabled)
            {
                WaterSpringLogger.LogDebug($"Registered active chunk {chunkCoord} on map {map.uniqueID}");
            }
        }
        
        // Unregister a chunk from active list
        private void UnregisterActiveChunk(Map map, ChunkCoordinate chunkCoord)
        {
            if (map == null) return;
            
            if (activeChunks.ContainsKey(map))
            {
                activeChunks[map].Remove(chunkCoord);
                if (Settings.debugModeEnabled)
                {
                    WaterSpringLogger.LogDebug($"Unregistered chunk {chunkCoord} from map {map.uniqueID}");
                }
            }
        }
        
        // Register a tile as active
        public void RegisterActiveTile(Map map, IntVec3 position)
        {
            if (map == null || !Settings.useActiveTileSystem) return;
            // Skip registering if tile exists and is stable or explicitly deregistered
            FlowingWater existing = map.thingGrid.ThingAt<FlowingWater>(position);
            if (existing != null && (existing.IsStable() || existing.IsExplicitlyDeregistered))
            {
                return;
            }
            
            bool wasAdded = GetActiveWaterTiles(map).Add(position);
            if (wasAdded && Settings.debugModeEnabled)
            {
                WaterSpringLogger.LogDebug($"Registered active water tile at {position} on map {map.uniqueID}");
            }
            
            // Also register with chunk system if enabled
            if (Settings.useChunkBasedProcessing)
            {
                ChunkCoordinate chunk = ChunkCoordinate.FromPosition(position, Settings.chunkSize);
                RegisterActiveChunk(map, chunk);
                
                // Add to the chunk's water tiles
                GetWaterTilesInChunk(map, chunk).Add(position);
                
                // Add to the spatial index for faster lookups
                FlowingWater water = map.thingGrid.ThingAt<FlowingWater>(position);
                if (water != null)
                {
                    GetSpatialIndex(map)?.RegisterWaterTile(water);
                }
            }
        }
        
        // Unregister a tile from active list
        public void UnregisterActiveTile(Map map, IntVec3 position)
        {
            if (map == null || !Settings.useActiveTileSystem) return;
            
            if (activeWaterTilesByMap.ContainsKey(map))
            {
                bool wasRemoved = activeWaterTilesByMap[map].Remove(position);
                if (wasRemoved && Settings.debugModeEnabled)
                {
                    WaterSpringLogger.LogDebug($"Unregistered water tile at {position} from map {map.uniqueID}");
                }
            }
            
            // Also unregister from chunk system if enabled
            if (Settings.useChunkBasedProcessing)
            {
                ChunkCoordinate chunk = ChunkCoordinate.FromPosition(position, Settings.chunkSize);
                
                // Remove from the chunk's water tiles
                var waterTilesInChunk = GetWaterTilesInChunk(map, chunk);
                waterTilesInChunk.Remove(position);
                
                // If the chunk is now empty, unregister it as active
                if (waterTilesInChunk.Count == 0)
                {
                    UnregisterActiveChunk(map, chunk);
                }
                
                // Remove from the spatial index
                GetSpatialIndex(map)?.UnregisterWaterTile(position);
            }
        }
        
        // Activate neighboring tiles when water spreads
        public void ActivateNeighbors(Map map, IntVec3 position)
        {
            if (map == null || !Settings.useActiveTileSystem) return;
            
            // Check all 4 cardinal directions
            foreach (IntVec3 offset in GenAdj.CardinalDirections)
            {
                IntVec3 neighborPos = position + offset;
                
                // Only activate if in bounds and walkable
                if (neighborPos.InBounds(map) && neighborPos.Walkable(map))
                {
                    // Check if there's a water tile at this position
                    FlowingWater neighborWater = map.thingGrid.ThingAt<FlowingWater>(neighborPos);
                    if (neighborWater != null)
                    {
                        // Only reactivate if deregistered and either:
                        // - neighbor has Volume > 1 and at least one valid target (empty cell or lower-volume neighbor), or
                        // - the changed cell is now a valid target for neighbor (lower volume than neighbor or empty)
                        if (neighborWater.IsExplicitlyDeregistered)
                        {
                            bool shouldReactivate = false;
                            // Check if the changed cell is a valid recipient from neighbor
                            FlowingWater changed = map.thingGrid.ThingAt<FlowingWater>(position);
                            int minDiff = Settings.minVolumeDifferenceForTransfer;
                            if (changed == null)
                            {
                                // Empty means neighbor could expand
                                shouldReactivate = neighborWater.Volume >= 2;
                            }
                            else
                            {
                                // Neighbor could transfer to changed if has sufficient difference and changed not maxed
                                if (changed.Volume < FlowingWater.MaxVolume && (neighborWater.Volume - changed.Volume) >= minDiff)
                                {
                                    shouldReactivate = true;
                                }
                            }
                            
                            // Also scan neighbor's other adjacent cells quickly for any potential movement
                            if (!shouldReactivate && neighborWater.Volume > 1)
                            {
                                foreach (IntVec3 off2 in GenAdj.CardinalDirections)
                                {
                                    IntVec3 adj2 = neighborPos + off2;
                                    if (!adj2.InBounds(map) || !adj2.Walkable(map)) continue;
                                    FlowingWater w2 = map.thingGrid.ThingAt<FlowingWater>(adj2);
                                    if (w2 == null)
                                    {
                                        if (neighborWater.Volume >= 2) { shouldReactivate = true; break; }
                                    }
                                    else if (w2.Volume < FlowingWater.MaxVolume && (neighborWater.Volume - w2.Volume) >= minDiff)
                                    {
                                        shouldReactivate = true; break;
                                    }
                                }
                            }
                            
                            if (shouldReactivate)
                            {
                                neighborWater.Reactivate();
                            }
                        }
                    }
                    
                    // Register the tile as active for potential water flow
                    // (This needs to happen for both existing water and empty tiles)
                    RegisterActiveTile(map, neighborPos);
                }
            }
            
            // If chunk processing is enabled, also activate neighboring chunks
            if (Settings.useChunkBasedProcessing)
            {
                ChunkCoordinate currentChunk = ChunkCoordinate.FromPosition(position);
                
                // Check if we're near a chunk boundary and activate adjacent chunks if needed
                IntVec3 minCorner = currentChunk.MinCorner;
                IntVec3 maxCorner = currentChunk.MaxCorner;
                
                bool isNearBoundary = 
                    position.x - minCorner.x <= 1 || 
                    maxCorner.x - position.x <= 1 ||
                    position.z - minCorner.z <= 1 || 
                    maxCorner.z - position.z <= 1;
                
                if (isNearBoundary)
                {
                    // Activate all adjacent chunks
                    foreach (ChunkCoordinate neighborChunk in currentChunk.GetAdjacentChunks())
                    {
                        RegisterActiveChunk(map, neighborChunk);
                    }
                }
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            
            // Clear any active tiles that might have been saved
            activeWaterTilesByMap.Clear();
            waterTilesByChunk.Clear();
            activeChunks.Clear();
            spatialIndices.Clear();
            // Multi-phase transfer managers removed
            
            // Initialize for each current map
            foreach (Map map in Find.Maps)
            {
                GetActiveWaterTiles(map);
                if (Settings.useChunkBasedProcessing)
                {
                    GetActiveChunks(map);
                    GetWaterTilesByChunk(map);
                    
                    // Initialize and build the spatial index
                    ChunkBasedSpatialIndex spatialIndex = GetSpatialIndex(map);
                    spatialIndex.RebuildFromMap();
                }
                
                    // Multi-phase removed
            }
            
            WaterSpringLogger.LogDebug("GameComponent_WaterDiffusion finalized initialization");
        }

        // Method to handle walls/buildings being added or removed
        // Call this when terrain changes that might affect water flow
        public void NotifyTerrainChanged(Map map, IntVec3 position)
        {
            if (map == null) return;
            
            // Check all 8 adjacent cells
            foreach (IntVec3 offset in GenAdj.AdjacentCellsAndInside)
            {
                // Skip center cell
                if (offset.x == 0 && offset.z == 0) continue;
                
                IntVec3 adjacentPos = position + offset;
                
                // Skip if not in bounds
                if (!adjacentPos.InBounds(map)) continue;
                
                // Check for water tile
                FlowingWater water = map.thingGrid.ThingAt<FlowingWater>(adjacentPos);
                if (water != null)
                {
                    // Reset stability and reactivate
                    water.ResetStabilityCounter();
                    water.Reactivate();
                    
                    // Register as active
                    RegisterActiveTile(map, adjacentPos);
                    
                    // Also activate its neighbors
                    ActivateNeighbors(map, adjacentPos);
                    
                    if (Settings.debugModeEnabled)
                    {
                        WaterSpringLogger.LogDebug($"Water at {adjacentPos} reactivated due to terrain change at {position}");
                    }
                }
            }

            // Propagate a bounded reactivation wave backwards from the changed position
            // to ensure the path toward the source wakes up. Reuse scratch BFS buffers.
            int maxWaveSteps = 32; // safety bound
            bfsFrontier.Clear();
            bfsVisited.Clear();
            bfsFrontier.Enqueue(position);
            bfsVisited.Add(position);
            int steps = 0;
            while (bfsFrontier.Count > 0 && steps < maxWaveSteps)
            {
                int breadth = bfsFrontier.Count;
                for (int i = 0; i < breadth; i++)
                {
                    var p = bfsFrontier.Dequeue();
                    foreach (var d in GenAdj.CardinalDirections)
                    {
                        var np = p + d;
                        if (!np.InBounds(map) || bfsVisited.Contains(np) || !np.Walkable(map)) continue;
                        bfsVisited.Add(np);
                        var w = map.thingGrid.ThingAt<FlowingWater>(np);
                        if (w == null) continue;
                        // Only wake tiles that were explicitly deregistered and have any potential flow
                        if (w.IsExplicitlyDeregistered)
                        {
                            // If any adjacent cell is empty or has lower volume by minDiff, reactivate
                            bool canFlow = false;
                            int md = Settings.minVolumeDifferenceForTransfer;
                            foreach (var d2 in GenAdj.CardinalDirections)
                            {
                                var ap = np + d2;
                                if (!ap.InBounds(map) || !ap.Walkable(map)) continue;
                                var aw = map.thingGrid.ThingAt<FlowingWater>(ap);
                                if (aw == null) { if (w.Volume >= 2) { canFlow = true; break; } }
                                else if (aw.Volume < FlowingWater.MaxVolume && (w.Volume - aw.Volume) >= md) { canFlow = true; break; }
                            }
                            if (canFlow)
                            {
                                w.Reactivate();
                                RegisterActiveTile(map, np);
                            }
                        }
                        bfsFrontier.Enqueue(np);
                    }
                }
                steps++;
            }

            // Also trigger a radius-based reactivation around the change
            ReactivateInRadius(map, position);
        }

        // Reactivate tiles in a radius and optionally perform one immediate transfer
        public void ReactivateInRadius(Map map, IntVec3 center)
        {
            if (map == null || !Settings.useActiveTileSystem) return;
            // Reentrancy guard to avoid nested waves in the same tick
            if (reactivatingNow) return;
            reactivatingNow = true;
            int radius = Mathf.Max(1, Settings.reactivationRadius);
            int maxTiles = Mathf.Max(1, Settings.reactivationMaxTiles);
            bool doImmediate = Settings.reactivationImmediateTransfers;

            int processed = 0;
            foreach (var pos in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!pos.InBounds(map) || !pos.Walkable(map)) continue;
                var w = map.thingGrid.ThingAt<FlowingWater>(pos);
                if (w == null) continue;

                // Wake tiles that were stable
                if (w.IsExplicitlyDeregistered)
                {
                    w.Reactivate();
                }

                // Ensure registered for processing
                RegisterActiveTile(map, pos);

                // Optionally attempt one immediate transfer to kickstart flow
                if (doImmediate && processed < maxTiles && w.Volume > 1)
                {
                    // Try to move toward any valid neighbor
                    foreach (var d in GenAdj.CardinalDirections)
                    {
                        var np = pos + d;
                        if (!np.InBounds(map) || !np.Walkable(map)) continue;
                        var nw = map.thingGrid.ThingAt<FlowingWater>(np);
                        if (nw == null)
                        {
                            if (w.Volume >= 2)
                            {
                                // Create and move 1 unit
                                ThingDef waterDef = DefDatabase<ThingDef>.GetNamed("FlowingWater", false);
                                if (waterDef != null)
                                {
                                    Thing newWater = ThingMaker.MakeThing(waterDef);
                                    if (newWater is FlowingWater tw)
                                    {
                                        tw.Volume = 0;
                                        GenSpawn.Spawn(newWater, np, map);
                                        if (w.TransferVolume(tw)) { processed++; break; }
                                    }
                                }
                            }
                        }
                        else if (nw.Volume < FlowingWater.MaxVolume && (w.Volume - nw.Volume) >= Settings.minVolumeDifferenceForTransfer)
                        {
                            if (w.TransferVolume(nw)) { processed++; break; }
                        }
                    }
                }

                if (processed >= maxTiles) break;
            }
            reactivatingNow = false;
        }

    // Diffusion method switching removed; Normal method is always used
        
    // Cleanup for diffusion method switching removed

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            
            // Skip processing if active tile system is disabled
            if (!Settings.useActiveTileSystem) return;
            
            // Update tick counter for frequency-based processing
            tickCounter++;
            ticksSinceLastUpdate++;
            
            // Delegate to frequency gate so global frequency/adaptive TPS are respected
            CheckProcessingFrequency();
        }
        
        // Process using the normal method (individual tiles)
        private void CheckProcessingFrequency()
        {
            // Check if we should process water this tick
            bool shouldProcessThisTick = true;
            
            // If frequency-based processing is enabled, only process on specific ticks
            if (Settings.useFrequencyBasedProcessing)
            {
                // Only process every N ticks based on global frequency setting
                shouldProcessThisTick = (tickCounter % Settings.globalUpdateFrequency == 0);
                
                // If adaptive TPS is enabled, monitor performance and adjust processing
                if (Settings.useAdaptiveTPS && Find.TickManager != null)
                {
                    // Get current TPS estimate
                    float currentTPS = Find.TickManager.TickRateMultiplier * 60f;
                    
                    // Sample TPS for smoothing
                    tpsAccumulator += currentTPS;
                    tpsSampleCount++;
                    
                    // Update average TPS every 60 ticks
                    if (tpsSampleCount >= 60)
                    {
                        lastTPS = tpsAccumulator / tpsSampleCount;
                        tpsAccumulator = 0f;
                        tpsSampleCount = 0;
                        // Throttle TPS logging: only if debug, at most every 120 ticks, and if value changed meaningfully
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
                    
                    // If TPS is below target, throttle processing more aggressively
                    if (lastTPS < Settings.minTPS && lastTPS > 0)
                    {
                        // Dynamically adjust processing frequency based on how far below target we are
                        float tpsDelta = Settings.minTPS - lastTPS;
                        int additionalDelay = Mathf.CeilToInt(tpsDelta * 0.5f); // 0.5 extra ticks of delay per 1 TPS below target
                        
                        // Only process if we've waited long enough
                        shouldProcessThisTick = shouldProcessThisTick && (ticksSinceLastUpdate >= (Settings.globalUpdateFrequency + additionalDelay));
                        
                        // Log throttling if in debug mode (throttled)
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
            
            // Only process if conditions are met
            if (shouldProcessThisTick)
            {
                // Reset counter when processing occurs
                ticksSinceLastUpdate = 0;
                
                // Branch based on diffusion method
                // Only Normal method remains
                ProcessNormalMethod();
            }
        }
        
        // Process water using the normal method
        private void ProcessNormalMethod()
        {
            foreach (Map map in Find.Maps)
            {
                if (Settings.useChunkBasedProcessing)
                {
                    ProcessActiveChunksNormal(map);
                }
                else
                {
                    ProcessActiveWaterTilesNormal(map);
                }
            }
        }
        
    // Multi-phase removed
        
        // Process active chunks using normal (individual) method
        private void ProcessActiveChunksNormal(Map map)
        {
            tilesProcessedLastTick = 0;
            HashSet<ChunkCoordinate> mapActiveChunks = GetActiveChunks(map);
            activeChunkCount = mapActiveChunks.Count;

            // Copy active chunks into scratch list
            scratchChunks.Clear();
            scratchChunks.AddRange(mapActiveChunks);
            scratchChunksToRemove.Clear();

            int chunkProcessLimit = Settings.maxProcessedChunksPerTick;
            if (scratchChunks.Count > chunkProcessLimit)
            {
                ticksAtFullCapacity++;
                if (ticksAtFullCapacity % 60 == 0)
                {
                    WaterSpringLogger.LogWarning($"Water chunk system at processing capacity for {ticksAtFullCapacity} ticks. Active chunks: {scratchChunks.Count}, Max: {chunkProcessLimit}");
                }
            }
            else
            {
                ticksAtFullCapacity = 0;
            }

            int processLimit = Mathf.Min(scratchChunks.Count, chunkProcessLimit);

            for (int i = 0; i < processLimit; i++)
            {
                int randomIndex = Rand.Range(i, scratchChunks.Count);
                if (randomIndex != i)
                {
                    ChunkCoordinate tmp = scratchChunks[i];
                    scratchChunks[i] = scratchChunks[randomIndex];
                    scratchChunks[randomIndex] = tmp;
                }

                ChunkCoordinate chunk = scratchChunks[i];
                HashSet<IntVec3> waterTilesInChunk = GetWaterTilesInChunk(map, chunk);
                if (waterTilesInChunk.Count == 0)
                {
                    scratchChunksToRemove.Add(chunk);
                    continue;
                }

                if (Settings.useCheckerboardPattern)
                {
                    bool isEvenTick = (tickCounter % 2 == 0);
                    bool isEvenChunk = ((chunk.X + chunk.Z) % 2 == 0);
                    if (isEvenTick != isEvenChunk)
                    {
                        continue;
                    }
                }

                scratchChunkRemovals.Clear();
                ProcessWaterTilesInChunk(map, chunk, waterTilesInChunk, scratchChunkRemovals);

                foreach (IntVec3 pos in scratchChunkRemovals)
                {
                    UnregisterActiveTile(map, pos);
                }

                if (GetWaterTilesInChunk(map, chunk).Count == 0)
                {
                    scratchChunksToRemove.Add(chunk);
                }
            }

            foreach (ChunkCoordinate cc in scratchChunksToRemove)
            {
                UnregisterActiveChunk(map, cc);
            }
        }
        // Process active chunks using multi-phase method
    // Multi-phase removed
        
        // Process active water tiles using normal (individual) method
        private void ProcessActiveWaterTilesNormal(Map map)
        {
            tilesProcessedLastTick = 0;
            HashSet<IntVec3> activeTiles = GetActiveWaterTiles(map);
            activeWaterTileCount = activeTiles.Count;

            scratchTiles.Clear();
            scratchTiles.AddRange(activeTiles);
            scratchTilesToRemove.Clear();

            if (activeTiles.Count > Settings.maxProcessedTilesPerTick)
            {
                ticksAtFullCapacity++;
                if (ticksAtFullCapacity % 60 == 0)
                {
                    WaterSpringLogger.LogWarning($"Water system at processing capacity for {ticksAtFullCapacity} ticks. Active tiles: {activeTiles.Count}, Max: {Settings.maxProcessedTilesPerTick}");
                }
            }
            else
            {
                ticksAtFullCapacity = 0;
            }

            int processLimit = Mathf.Min(scratchTiles.Count, Settings.maxProcessedTilesPerTick);

            for (int i = 0; i < processLimit; i++)
            {
                int randomIndex = Rand.Range(i, scratchTiles.Count);
                if (randomIndex != i)
                {
                    IntVec3 tmp = scratchTiles[i];
                    scratchTiles[i] = scratchTiles[randomIndex];
                    scratchTiles[randomIndex] = tmp;
                }

                IntVec3 pos = scratchTiles[i];
                FlowingWater water = GetWaterAt(map, pos);
                if (water == null)
                {
                    scratchTilesToRemove.Add(pos);
                    continue;
                }

                if (water.ticksUntilNextCheck <= 0 && !water.IsStable())
                {
                    int interval = Mathf.Max(1, water.GetProcessingInterval());
                    bool changed = water.AttemptLocalDiffusion();

                    if (!changed)
                    {
                        water.IncrementStabilityCounter();
                        if (water.IsStable())
                        {
                            scratchTilesToRemove.Add(pos);
                            water.MarkAsStable();
                        }
                    }
                    else
                    {
                        water.ResetStabilityCounter();
                        ActivateNeighbors(map, pos);
                    }

                    var s = Settings;
                    int minI = Mathf.Max(1, s.localCheckIntervalMin);
                    int maxI = Mathf.Max(minI, s.localCheckIntervalMax);
                    int baseDelay = Rand.Range(minI, maxI);
                    int tierDelay = Math.Max(0, interval - 1);
                    long nextDelay = (long)baseDelay + (long)tierDelay;
                    water.ticksUntilNextCheck = (int)Mathf.Clamp(nextDelay, 1, int.MaxValue - 1);
                }

                tilesProcessedLastTick++;
            }

            foreach (IntVec3 pos in scratchTilesToRemove)
            {
                UnregisterActiveTile(map, pos);
            }
        }
        // Process active water tiles using multi-phase method
    // Multi-phase removed
        
        // Debug visualization of active water tiles
        public override void GameComponentOnGUI()
        {
            base.GameComponentOnGUI();
            #if WATERPHYSICS_DEV
            // Dev-only harness hook (safe no-op when symbol is absent)
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
                HashSet<IntVec3> activeTiles = GetActiveWaterTiles(Find.CurrentMap);
                // Draw visualization if enabled
                if (ShowActiveWaterDebug)
                {
                    if (Event.current.type == EventType.Repaint)
                    {
                        // Status label
                        float y = Settings.showPerformanceStats ? 30f : 10f;
                        Widgets.Label(new Rect(10f, y, 420f, 22f), $"[WaterPhysics] Active tiles: {activeTiles.Count} (overlay ON)");
                        // Copy to scratch list to avoid enumerating a mutating set
                        scratchDrawTiles.Clear();
                        scratchDrawTiles.AddRange(activeTiles);
                        // Draw field edges for all active cells for a clear, persistent overlay
                        if (scratchDrawTiles.Count > 0)
                        {
                            GenDraw.DrawFieldEdges(scratchDrawTiles);
                            // Fallback: also flash cells to guarantee visibility in case edges are not apparent
                            int maxFlash = Mathf.Min(scratchDrawTiles.Count, 512);
                            for (int i = 0; i < maxFlash; i++)
                            {
                                Find.CurrentMap.debugDrawer.FlashCell(scratchDrawTiles[i], 0.15f);
                            }

                            // Draw volume numbers (capped) for visible clarity; uses tiny font to minimize cost
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
                                // Try stock label drawer (may be gated by zoom/settings)
                                GenMapUI.DrawThingLabel(world, txt, Color.yellow);
                                // Manual screen-space fallback so text is always visible
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
                    
                    // Draw chunk boundaries if chunk processing is enabled
                        if (Settings.useChunkBasedProcessing)
                    {
                        HashSet<ChunkCoordinate> activeChunksForMap = GetActiveChunks(Find.CurrentMap);
                        foreach (ChunkCoordinate chunk in activeChunksForMap)
                        {
                            IntVec3 min = chunk.MinCorner;
                            IntVec3 max = chunk.MaxCorner;
                            
                            // Draw chunk boundary
                            Vector3 v1 = new Vector3(min.x, AltitudeLayer.MetaOverlays.AltitudeFor(), min.z);
                            Vector3 v2 = new Vector3(max.x + 1, AltitudeLayer.MetaOverlays.AltitudeFor(), min.z);
                            Vector3 v3 = new Vector3(max.x + 1, AltitudeLayer.MetaOverlays.AltitudeFor(), max.z + 1);
                            Vector3 v4 = new Vector3(min.x, AltitudeLayer.MetaOverlays.AltitudeFor(), max.z + 1);
                            // Draw each edge of the chunk
                            GenDraw.DrawLineBetween(v1, v2, SimpleColor.Green);
                            GenDraw.DrawLineBetween(v2, v3, SimpleColor.Green);
                            GenDraw.DrawLineBetween(v3, v4, SimpleColor.Green);
                            GenDraw.DrawLineBetween(v4, v1, SimpleColor.Green);
                        }
                    }
                    }
                    
                    // Draw stable (explicitly deregistered) water tiles with blue circles
                    // (This is more expensive as we need to check all water tiles)
                if (Settings.showDetailedDebug && Event.current.type == EventType.Repaint)
                    {
                        List<Thing> allWaterTiles = Find.CurrentMap.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("FlowingWater"));
                        foreach (Thing t in allWaterTiles)
                        {
                            if (t is FlowingWater water && water.IsExplicitlyDeregistered)
                            {
                                Vector3 drawPos = water.Position.ToVector3Shifted();
                                drawPos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                                
                                // Draw blue circle for stable water tiles
                                GenDraw.DrawCircleOutline(drawPos, 0.4f, SimpleColor.Blue);
                            }
                        }
                    }
                }
                
                // Always show stats if performance monitoring is enabled
                if (Settings.showPerformanceStats)
                {
                    // Add debug info text
                    if (Settings.useChunkBasedProcessing)
                    {
                        Widgets.Label(new Rect(10, 10, 300, 20), 
                            $"Active chunks: {GetActiveChunks(Find.CurrentMap).Count}, Processed tiles: {tilesProcessedLastTick}");
                    }
                    else
                    {
                        Widgets.Label(new Rect(10, 10, 300, 20), 
                            $"Active water tiles: {activeTiles.Count}, Processed: {tilesProcessedLastTick}");
                    }
                    
                    // Multi-phase stats removed
                    
                    // Show additional info if at capacity
                    if (ticksAtFullCapacity > 0)
                    {
                        Widgets.Label(new Rect(10, 50, 300, 20),
                            $"WARNING: At processing capacity for {ticksAtFullCapacity} ticks");
                    }
                }
            }
        }
        
        // Process water tiles within a chunk (old individual approach)
        private void ProcessWaterTilesInChunk(Map map, ChunkCoordinate chunk, HashSet<IntVec3> waterTilesInChunk, HashSet<IntVec3> tilesToRemove)
        {
            // Process up to a limited number of tiles per chunk
            List<IntVec3> chunkTilesToProcess = new List<IntVec3>(waterTilesInChunk);
            int tileProcessLimit = Mathf.Min(chunkTilesToProcess.Count, Settings.maxProcessedTilesPerChunk);
            
            // Process tiles in this chunk
            for (int j = 0; j < tileProcessLimit; j++)
            {
                // Get a random index from the remaining tiles for better distribution
                int randomTileIndex = Rand.Range(j, chunkTilesToProcess.Count);
                if (randomTileIndex != j)
                {
                    IntVec3 temp = chunkTilesToProcess[j];
                    chunkTilesToProcess[j] = chunkTilesToProcess[randomTileIndex];
                    chunkTilesToProcess[randomTileIndex] = temp;
                }
                
                IntVec3 pos = chunkTilesToProcess[j];
                
                // Use the spatial index for faster lookup
                FlowingWater water = GetWaterAt(map, pos);
                
                // Skip if water no longer exists at this position
                if (water == null)
                {
                    tilesToRemove.Add(pos);
                    continue;
                }
                
                // Only process water that's ready for diffusion and not stable
                if (water.ticksUntilNextCheck <= 0 && !water.IsStable())
                {
                    // Get the processing interval based on stability
                    int interval = Mathf.Max(1, water.GetProcessingInterval());

                    // Process immediately when timer elapses
                    bool changed = water.AttemptLocalDiffusion();

                    // If it didn't change, mark for potential removal from active list
                    if (!changed)
                    {
                        water.IncrementStabilityCounter();

                        if (water.IsStable())
                        {
                            tilesToRemove.Add(pos);
                            water.MarkAsStable(); // Mark as explicitly stable
                        }
                    }
                    else
                    {
                        // Reset stability counter on change
                        water.ResetStabilityCounter();

                        // Activate neighbors when water changes
                        ActivateNeighbors(map, pos);
                    }

                    // Reset the water's timer using settings plus tier interval as additive delay
                    var s2 = Settings;
                    int minI2 = Mathf.Max(1, s2.localCheckIntervalMin);
                    int maxI2 = Mathf.Max(minI2, s2.localCheckIntervalMax);
                    int baseDelay2 = Rand.Range(minI2, maxI2);
                    int tierDelay2 = Math.Max(0, interval - 1);
                    long nextDelay2 = (long)baseDelay2 + (long)tierDelay2;
                    water.ticksUntilNextCheck = (int)Mathf.Clamp(nextDelay2, 1, int.MaxValue - 1);
                }
                
                tilesProcessedLastTick++;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            
            // We don't need to save the active tile registry
            // It will be rebuilt as water spawns and diffuses
            
            Scribe_Values.Look(ref ShowActiveWaterDebug, "showActiveWaterDebug", false);
            Scribe_Values.Look(ref ticksAtFullCapacity, "ticksAtFullCapacity", 0);
            Scribe_Values.Look(ref tickCounter, "tickCounter", 0);
            Scribe_Values.Look(ref lastTPS, "lastTPS", 0f);
        }
    }
}
