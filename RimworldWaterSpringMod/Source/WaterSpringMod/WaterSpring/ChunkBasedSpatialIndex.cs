using System.Collections.Generic;
using Verse;

namespace WaterSpringMod.WaterSpring
{
    /// <summary>
    /// A spatial index for water tiles organized by chunks
    /// This provides faster access to water tiles by their spatial location
    /// </summary>
    public class ChunkBasedSpatialIndex
    {
        // Main chunk-based storage
        private readonly Dictionary<ChunkCoordinate, HashSet<IntVec3>> waterTilesByChunk;
        
        // Cache of FlowingWater instances by position for faster lookups
        private readonly Dictionary<IntVec3, FlowingWater> waterCache;
        
        // Map reference for context
        private readonly Map map;
        
        // Chunk size configuration
        private readonly int chunkSize;
        
        /// <summary>
        /// Create a new spatial index for the specified map
        /// </summary>
        public ChunkBasedSpatialIndex(Map map, int chunkSize = ChunkCoordinate.DefaultChunkSize)
        {
            this.map = map;
            this.chunkSize = chunkSize;
            this.waterTilesByChunk = new Dictionary<ChunkCoordinate, HashSet<IntVec3>>();
            this.waterCache = new Dictionary<IntVec3, FlowingWater>();
            
            WaterSpringLogger.LogDebug($"Created new ChunkBasedSpatialIndex for map {map?.uniqueID} with chunk size {chunkSize}");
        }
        
        /// <summary>
        /// Register a water tile with the spatial index
        /// </summary>
        public void RegisterWaterTile(FlowingWater water)
        {
            if (water == null || map == null || !water.Spawned || water.Map != map) return;
            
            IntVec3 pos = water.Position;
            ChunkCoordinate chunk = ChunkCoordinate.FromPosition(pos, chunkSize);
            
            // Add to the chunk's water tiles
            if (!waterTilesByChunk.TryGetValue(chunk, out HashSet<IntVec3> tilesInChunk))
            {
                tilesInChunk = new HashSet<IntVec3>();
                waterTilesByChunk[chunk] = tilesInChunk;
            }
            
            tilesInChunk.Add(pos);
            
            // Update the cache
            waterCache[pos] = water;
        }
        
        /// <summary>
        /// Unregister a water tile from the spatial index
        /// </summary>
        public void UnregisterWaterTile(IntVec3 pos)
        {
            ChunkCoordinate chunk = ChunkCoordinate.FromPosition(pos, chunkSize);
            
            // Remove from the chunk's water tiles
            if (waterTilesByChunk.TryGetValue(chunk, out HashSet<IntVec3> tilesInChunk))
            {
                tilesInChunk.Remove(pos);
                
                // If the chunk is now empty, remove it
                if (tilesInChunk.Count == 0)
                {
                    waterTilesByChunk.Remove(chunk);
                }
            }
            
            // Remove from the cache
            waterCache.Remove(pos);
        }
        
        /// <summary>
        /// Get all water tiles in a specific chunk
        /// </summary>
        public HashSet<IntVec3> GetWaterTilesInChunk(ChunkCoordinate chunk)
        {
            if (!waterTilesByChunk.TryGetValue(chunk, out HashSet<IntVec3> tilesInChunk))
            {
                tilesInChunk = new HashSet<IntVec3>();
                waterTilesByChunk[chunk] = tilesInChunk;
            }
            
            return tilesInChunk;
        }
        
        /// <summary>
        /// Get a FlowingWater instance at a specific position (fast cached lookup)
        /// </summary>
        public FlowingWater GetWaterAt(IntVec3 pos)
        {
            // Try to get from cache first
            if (waterCache.TryGetValue(pos, out FlowingWater water))
            {
                // Validate that the cached value is still valid
                if (water.Spawned && !water.Destroyed)
                {
                    return water;
                }
                else
                {
                    // Cache is stale, remove it
                    UnregisterWaterTile(pos);
                }
            }
            
            // Cache miss or stale cache, look up from map
            if (map != null)
            {
                water = map.thingGrid.ThingAt<FlowingWater>(pos);
                if (water != null)
                {
                    // Update the cache
                    waterCache[pos] = water;
                }
            }
            
            return water;
        }
        
        /// <summary>
        /// Get all chunks that have water tiles
        /// </summary>
        public IEnumerable<ChunkCoordinate> GetOccupiedChunks()
        {
            return waterTilesByChunk.Keys;
        }
        
        /// <summary>
        /// Get a count of all water tiles in the index
        /// </summary>
        public int GetTotalWaterTileCount()
        {
            int count = 0;
            foreach (var tilesInChunk in waterTilesByChunk.Values)
            {
                count += tilesInChunk.Count;
            }
            return count;
        }
        
        /// <summary>
        /// Clear the spatial index
        /// </summary>
        public void Clear()
        {
            waterTilesByChunk.Clear();
            waterCache.Clear();
        }
        
        /// <summary>
        /// Update the entire spatial index from the map
        /// </summary>
        public void RebuildFromMap()
        {
            if (map == null) return;
            
            WaterSpringLogger.LogDebug($"Rebuilding ChunkBasedSpatialIndex for map {map.uniqueID}");
            
            Clear();
            
            // Find all water tiles
            List<Thing> allWaters = map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("FlowingWater"));
            foreach (Thing thing in allWaters)
            {
                if (thing is FlowingWater water && water.Spawned && !water.Destroyed)
                {
                    RegisterWaterTile(water);
                }
            }
            
            WaterSpringLogger.LogDebug($"ChunkBasedSpatialIndex rebuild complete. Indexed {GetTotalWaterTileCount()} water tiles");
        }
    }
}
