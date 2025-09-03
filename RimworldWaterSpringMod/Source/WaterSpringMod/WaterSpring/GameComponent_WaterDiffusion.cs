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
        
    // Global tick/frequency trackers
    private int tickCounter = 0;
    private float lastTPS = 0f;
    private float tpsAccumulator = 0f;
    private int tpsSampleCount = 0;
    private int ticksSinceLastUpdate = 0;
        
        // Debug visualization
        public bool ShowActiveWaterDebug = false;

        // Settings access helper
        private WaterSpringModSettings Settings => LoadedModManager.GetMod<WaterSpringModMain>().settings;
        
        public GameComponent_WaterDiffusion(Game game) : base()
        {
            WaterSpringLogger.LogDebug("GameComponent_WaterDiffusion initialized");
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
                WaterSpringLogger.LogDebug($"Created new active water tile registry for map {map.uniqueID}");
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
                WaterSpringLogger.LogDebug($"Created new active chunks registry for map {map.uniqueID}");
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
                WaterSpringLogger.LogDebug($"Created new water tiles by chunk registry for map {map.uniqueID}");
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
                WaterSpringLogger.LogDebug($"Created new spatial index for map {map.uniqueID} with chunk size {chunkSize}");
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
            WaterSpringLogger.LogDebug($"Registered active chunk {chunkCoord} on map {map.uniqueID}");
        }
        
        // Unregister a chunk from active list
        private void UnregisterActiveChunk(Map map, ChunkCoordinate chunkCoord)
        {
            if (map == null) return;
            
            if (activeChunks.ContainsKey(map))
            {
                activeChunks[map].Remove(chunkCoord);
                WaterSpringLogger.LogDebug($"Unregistered chunk {chunkCoord} from map {map.uniqueID}");
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
            if (wasAdded)
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
                if (wasRemoved)
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
                        // Only reactivate if neighbor was explicitly deregistered AND has room and real potential to change
                        // Avoid waking long-term stable water without cause
                        if (neighborWater.IsExplicitlyDeregistered && !neighborWater.IsStable())
                        {
                            neighborWater.Reactivate();
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
                    
                    WaterSpringLogger.LogDebug($"Water at {adjacentPos} reactivated due to terrain change at {position}");
                }
            }
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
                        
                        // Log TPS if in debug mode
                        if (Settings.debugModeEnabled)
                        {
                            WaterSpringLogger.LogDebug($"Current TPS: {lastTPS:F1}, Min Target: {Settings.minTPS:F1}");
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
                        
                        // Log throttling if in debug mode
                        if (shouldProcessThisTick && Settings.debugModeEnabled)
                        {
                            WaterSpringLogger.LogDebug($"Throttling water processing due to low TPS. Additional delay: {additionalDelay} ticks");
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
            
            // Create a copy of the active chunks to avoid concurrent modification
            List<ChunkCoordinate> chunksToProcess = new List<ChunkCoordinate>(mapActiveChunks);
            HashSet<ChunkCoordinate> chunksToRemove = new HashSet<ChunkCoordinate>();
            
            // If we have too many active chunks, warn about it
            int chunkProcessLimit = Settings.maxProcessedChunksPerTick;
            if (chunksToProcess.Count > chunkProcessLimit)
            {
                ticksAtFullCapacity++;
                
                // Only log every 60 ticks to avoid spam
                if (ticksAtFullCapacity % 60 == 0)
                {
                    WaterSpringLogger.LogWarning($"Water chunk system at processing capacity for {ticksAtFullCapacity} ticks. " +
                                               $"Active chunks: {chunksToProcess.Count}, Max: {chunkProcessLimit}");
                }
            }
            else
            {
                ticksAtFullCapacity = 0;
            }
            
            // Limit how many chunks we process per tick to prevent lag
            int processLimit = Mathf.Min(chunksToProcess.Count, chunkProcessLimit);
            
            // Process random chunks up to the limit for better distribution
            for (int i = 0; i < processLimit; i++)
            {
                // Get a random index from the remaining chunks
                int randomIndex = Rand.Range(i, chunksToProcess.Count);
                if (randomIndex != i)
                {
                    ChunkCoordinate temp = chunksToProcess[i];
                    chunksToProcess[i] = chunksToProcess[randomIndex];
                    chunksToProcess[randomIndex] = temp;
                }
                
                ChunkCoordinate chunk = chunksToProcess[i];
                
                // Get all water tiles in this chunk
                HashSet<IntVec3> waterTilesInChunk = GetWaterTilesInChunk(map, chunk);
                
                // Skip empty chunks
                if (waterTilesInChunk.Count == 0)
                {
                    chunksToRemove.Add(chunk);
                    continue;
                }
                
                // Implement checkerboard pattern if enabled
                if (Settings.useCheckerboardPattern)
                {
                    // Determine if this chunk should be processed this tick based on checkerboard pattern
                    // This creates a staggered update pattern where alternate chunks update on different ticks
                    bool isEvenTick = (tickCounter % 2 == 0);
                    bool isEvenChunk = ((chunk.X + chunk.Z) % 2 == 0);
                    
                    // Skip if this isn't the right tick for this chunk
                    if (isEvenTick != isEvenChunk)
                    {
                        continue;
                    }
                }
                
                // Process the tiles in this chunk using normal processing
                List<IntVec3> chunkTilesToProcess = new List<IntVec3>(waterTilesInChunk);
                HashSet<IntVec3> tilesToRemove = new HashSet<IntVec3>();
                
                ProcessWaterTilesInChunk(map, chunk, waterTilesInChunk, tilesToRemove);
                
                // Remove inactive tiles from the chunk
                foreach (IntVec3 pos in tilesToRemove)
                {
                    UnregisterActiveTile(map, pos);
                }
                
                // If the chunk is now empty, mark it for removal
                if (GetWaterTilesInChunk(map, chunk).Count == 0)
                {
                    chunksToRemove.Add(chunk);
                }
            }
            
            // Remove empty chunks from active list
            foreach (ChunkCoordinate chunk in chunksToRemove)
            {
                UnregisterActiveChunk(map, chunk);
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
            
            // Create a copy of the active tiles to avoid concurrent modification
            List<IntVec3> tilesToProcess = new List<IntVec3>(activeTiles);
            HashSet<IntVec3> tilesToRemove = new HashSet<IntVec3>();
            
            // If we have too many active tiles, warn about it
            if (activeTiles.Count > Settings.maxProcessedTilesPerTick)
            {
                ticksAtFullCapacity++;
                
                // Only log every 60 ticks to avoid spam
                if (ticksAtFullCapacity % 60 == 0)
                {
                    WaterSpringLogger.LogWarning($"Water system at processing capacity for {ticksAtFullCapacity} ticks. " +
                                               $"Active tiles: {activeTiles.Count}, Max: {Settings.maxProcessedTilesPerTick}");
                }
            }
            else
            {
                ticksAtFullCapacity = 0;
            }
            
            // Limit how many tiles we process per tick to prevent lag
            int processLimit = Mathf.Min(tilesToProcess.Count, Settings.maxProcessedTilesPerTick);
            int processed = 0;
            
            // Process tiles up to the limit - prioritize random tiles to avoid always processing the same ones
            // This gives better distribution of processing across the water system
            for (int i = 0; i < processLimit; i++)
            {
                // Get a random index and swap with current position for efficient removal
                int randomIndex = Rand.Range(i, tilesToProcess.Count);
                if (randomIndex != i)
                {
                    IntVec3 temp = tilesToProcess[i];
                    tilesToProcess[i] = tilesToProcess[randomIndex];
                    tilesToProcess[randomIndex] = temp;
                }
                
                // Get the position to process
                IntVec3 pos = tilesToProcess[i];
                
                // Use the spatial index for faster lookup when chunk processing is enabled
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

                    // Process the water tile immediately when its local timer elapses
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

                    // Reset the water's timer using settings plus tier interval as an additive delay
                    var s = Settings;
                    int minI = Mathf.Max(1, s.localCheckIntervalMin);
                    int maxI = Mathf.Max(minI, s.localCheckIntervalMax);
                    int baseDelay = Rand.Range(minI, maxI);
                    int tierDelay = Math.Max(0, interval - 1);
                    // Clamp to prevent int overflow and avoid negative timers
                    long nextDelay = (long)baseDelay + (long)tierDelay;
                    water.ticksUntilNextCheck = (int)Mathf.Clamp(nextDelay, 1, int.MaxValue - 1);
                }
                
                processed++;
                tilesProcessedLastTick++;
            }
            
            // Remove stable tiles from active list
            foreach (IntVec3 pos in tilesToRemove)
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
                    // Draw active tiles with red circles
                    foreach (IntVec3 pos in activeTiles)
                    {
                        Vector3 drawPos = pos.ToVector3Shifted();
                        drawPos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                        
                        // Draw red circle for active tiles
                        GenDraw.DrawCircleOutline(drawPos, 0.5f, SimpleColor.Red);
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
                    
                    // Draw stable (explicitly deregistered) water tiles with blue circles
                    // (This is more expensive as we need to check all water tiles)
                    if (Settings.showDetailedDebug)
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
