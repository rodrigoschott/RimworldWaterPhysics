using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WaterSpringMod.WaterSpring
{
    /// <summary>
    /// Simplified manager that provides developer utilities for water management
    /// </summary>
    public class WaterDiffusionManager
    {
        /// <summary>
        /// Developer command: Removes all water tiles from the specified map
        /// </summary>
        public void RemoveAllWaterFromMap(Map map)
        {
            if (map == null) return;
            
            WaterSpringLogger.LogWarning($"WaterDiffusionManager: DEVELOPER COMMAND - Removing ALL water from map {map.Index}");
            
            // Find all water tiles
            List<FlowingWater> allWaters = new List<FlowingWater>();
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing is FlowingWater water && water.Spawned && !water.Destroyed)
                {
                    allWaters.Add(water);
                }
            }
            
            int waterCount = allWaters.Count;
            if (waterCount == 0)
            {
                WaterSpringLogger.LogWarning($"WaterDiffusionManager: No water found on map");
                return;
            }
            
            // Destroy all water tiles
            foreach (FlowingWater water in allWaters)
            {
                water.Destroy();
            }
            
            WaterSpringLogger.LogWarning($"WaterDiffusionManager: Successfully removed {waterCount} water tiles from map");
        }
        
        // The DiffuseWater method is intentionally removed as it's redundant with FlowingWater's own diffusion
    }
}