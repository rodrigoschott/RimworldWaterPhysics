using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using UnityEngine;

namespace WaterSpringMod.WaterSpring
{
    /// <summary>
    /// Manages water transfers using a multi-phase approach:
    /// 1. Collection phase: Gather all potential water movements
    /// 2. Calculation phase: Determine final volumes after all transfers
    /// 3. Application phase: Apply all changes at once
    /// </summary>
    public class WaterTransferManager
    {
        // Store pending transfers
        private Dictionary<IntVec3, List<WaterTransfer>> pendingTransfers = new Dictionary<IntVec3, List<WaterTransfer>>();
        
        // Track final volumes after all transfers
        private Dictionary<IntVec3, int> finalVolumes = new Dictionary<IntVec3, int>();
        
        // Track which tiles have been processed this phase
        private HashSet<IntVec3> processedTiles = new HashSet<IntVec3>();
        
        // Track which tiles were affected by transfers (for stability tracking)
        private HashSet<IntVec3> affectedTiles = new HashSet<IntVec3>();
        
        // Statistics for monitoring
        public int TotalTransfersCollected { get; private set; }
        public int TransfersApplied { get; private set; }
        
        // Maximum transfers to process per tick (safety limit)
        private int maxTransfersPerTick;
        
        // Reference to the GameComponent for activating tiles
        private GameComponent_WaterDiffusion diffusionManager;
        
        // Self-contained stability tracking for multi-phase approach
        private Dictionary<IntVec3, int> stabilityCounters = new Dictionary<IntVec3, int>();
        private HashSet<IntVec3> stableTiles = new HashSet<IntVec3>();
    private const int MaxStabilityCounter = 10000; // Upper hard ceiling; actual cap is configurable
        
        /// <summary>
        /// Initialize a new WaterTransferManager
        /// </summary>
        /// <param name="maxTransfers">Maximum number of transfers to process per tick</param>
        /// <param name="manager">Reference to the GameComponent_WaterDiffusion</param>
        public WaterTransferManager(int maxTransfers, GameComponent_WaterDiffusion manager)
        {
            maxTransfersPerTick = maxTransfers;
            diffusionManager = manager;
            WaterSpringLogger.LogDebug("WaterTransferManager initialized");
        }
        
        /// <summary>
        /// Clear all pending transfers and reset state
        /// </summary>
        public void Reset()
        {
            pendingTransfers.Clear();
            finalVolumes.Clear();
            processedTiles.Clear();
            affectedTiles.Clear();
            TotalTransfersCollected = 0;
            TransfersApplied = 0;
            
            // Don't clear stability tracking - that persists across ticks
            // We only clear it when switching diffusion methods
        }
        
        /// <summary>
        /// Clear all state including stability tracking
        /// Used when switching diffusion methods
        /// </summary>
        public void ClearAll()
        {
            Reset();
            stabilityCounters.Clear();
            stableTiles.Clear();
        }
        
        /// <summary>
        /// Get the stability counter for a tile
        /// </summary>
        private int GetStabilityCounter(IntVec3 pos)
        {
            if (stabilityCounters.TryGetValue(pos, out int stability))
            {
                return stability;
            }
            return 0;
        }
        
        /// <summary>
        /// Set the stability counter for a tile
        /// </summary>
        private void SetStabilityCounter(IntVec3 pos, int value)
        {
            int cap = Mathf.Clamp(LoadedModManager.GetMod<WaterSpringModMain>().settings.stabilityCap, 1, MaxStabilityCounter);
            stabilityCounters[pos] = Mathf.Clamp(value, 0, cap);
            
            // If cap reached, add to stable tiles set
            if (stabilityCounters[pos] >= cap)
            {
                stableTiles.Add(pos);
            }
            else
            {
                stableTiles.Remove(pos);
            }
        }
        
        /// <summary>
        /// Increment stability for a tile
        /// </summary>
        private void IncrementStability(IntVec3 pos, int amount = 1)
        {
            int currentStability = GetStabilityCounter(pos);
            SetStabilityCounter(pos, currentStability + amount);
        }
        
        /// <summary>
        /// Reset stability for a tile
        /// </summary>
        private void ResetStability(IntVec3 pos)
        {
            SetStabilityCounter(pos, 0);
            stableTiles.Remove(pos);
        }
        
        /// <summary>
        /// Check if a tile is considered stable
        /// </summary>
        private bool IsStable(IntVec3 pos)
        {
            int cap = Mathf.Clamp(LoadedModManager.GetMod<WaterSpringModMain>().settings.stabilityCap, 1, MaxStabilityCounter);
            return GetStabilityCounter(pos) >= cap;
        }
        
        /// <summary>
        /// Check if a tile is at max stability
        /// </summary>
        private bool IsFullyStable(IntVec3 pos)
        {
            return stableTiles.Contains(pos) || GetStabilityCounter(pos) >= MaxStabilityCounter;
        }
        
        /// <summary>
        /// Collection phase: Gather all potential water movements from active tiles
        /// </summary>
        /// <param name="map">The map to process</param>
        /// <param name="activeTiles">Collection of active water tile positions</param>
        /// <returns>Number of transfers collected</returns>
        public int CollectTransfers(Map map, IEnumerable<IntVec3> activeTiles)
        {
            if (map == null) return 0;
            
            Reset();
            int transfersCollected = 0;
            
            WaterSpringLogger.LogDebug("WaterTransferManager: Starting collection phase");
            
            // Process each active tile
            foreach (IntVec3 pos in activeTiles)
            {
                // Skip if we've already processed this tile
                if (processedTiles.Contains(pos)) continue;
                
                // Get the water at this position
                FlowingWater water = map.thingGrid.ThingAt<FlowingWater>(pos);
                if (water == null) continue;
                
                // Mark as processed even if volume <= 1
                // This ensures we track stability for all water tiles, not just those that can transfer
                processedTiles.Add(pos);
                
                // Skip actual transfer collection if volume is too low
                if (water.Volume <= 1)
                {
                    // Still track the volume for stability calculations later
                    if (!finalVolumes.ContainsKey(pos))
                    {
                        finalVolumes[pos] = water.Volume;
                    }
                    continue;
                }
                
                // Record the current volume for this position
                if (!finalVolumes.ContainsKey(pos))
                {
                    finalVolumes[pos] = water.Volume;
                }
                
                // Check adjacent cells (cardinal directions only)
                List<WaterTransferTarget> potentialTargets = new List<WaterTransferTarget>();
                
                // First pass: Find all valid targets
                foreach (IntVec3 neighbor in GenAdj.CardinalDirections)
                {
                    IntVec3 adjacentCell = pos + neighbor;
                    
                    // Skip if not valid or not walkable
                    if (!adjacentCell.InBounds(map) || !adjacentCell.Walkable(map))
                    {
                        continue;
                    }
                    
                    // Check for solid buildings
                    bool hasBuilding = false;
                    foreach (Thing thing in adjacentCell.GetThingList(map))
                    {
                        if (thing.def.fillPercent > 0.1f && thing.def.category == ThingCategory.Building)
                        {
                            hasBuilding = true;
                            break;
                        }
                    }
                    if (hasBuilding) continue;
                    
                    // Look for existing water in this cell
                    FlowingWater existingWater = null;
                    foreach (Thing thing in adjacentCell.GetThingList(map))
                    {
                        if (thing is FlowingWater w)
                        {
                            existingWater = w;
                            break;
                        }
                    }
                    
                    // Create transfer target info
                    WaterTransferTarget target = new WaterTransferTarget
                    {
                        Position = adjacentCell,
                        ExistingWater = existingWater,
                        Volume = existingWater?.Volume ?? 0
                    };
                    
                    potentialTargets.Add(target);
                    
                    // Initialize final volume for this target if it has water
                    if (existingWater != null && !finalVolumes.ContainsKey(adjacentCell))
                    {
                        finalVolumes[adjacentCell] = existingWater.Volume;
                    }
                }
                
                // Process potential transfers
                if (potentialTargets.Count > 0)
                {
                    // Before attempting any transfers, check if this water is already at equilibrium with neighbors
                    bool isAtEquilibrium = true;
                    
                    // First check for empty neighbors - if there are any, we're not at equilibrium
                    var emptyCells = potentialTargets.FindAll(t => t.ExistingWater == null);
                    if (emptyCells.Count > 0 && water.Volume >= 2)
                    {
                        isAtEquilibrium = false;
                    }
                    
                    // Then check volume differences with existing water neighbors
                    if (isAtEquilibrium)
                    {
                        // Get the minimum volume difference from settings
                        int minVolumeDifference = LoadedModManager.GetMod<WaterSpringModMain>().settings.minVolumeDifferenceForTransfer;
                        
                        var waterNeighbors = potentialTargets.FindAll(t => t.ExistingWater != null);
                        foreach (var target in waterNeighbors)
                        {
                            // Consider equilibrium reached if the volume difference is less than the configured minimum
                            int volumeDifference = water.Volume - target.Volume;
                            if (volumeDifference >= minVolumeDifference)
                            {
                                isAtEquilibrium = false;
                                break;
                            }
                        }
                    }
                    
                    // If water is at equilibrium with all neighbors, don't transfer
                    // This helps the system reach stability more quickly
                    if (isAtEquilibrium)
                    {
                        WaterSpringLogger.LogDebug($"Water at {pos} is at equilibrium with neighbors, no transfer needed");
                        continue;
                    }
                    
                    // Check if we need to create a new water tile
                    if (water.Volume >= 2)
                    {
                        // Find empty cells
                        if (emptyCells.Count > 0)
                        {
                            // Randomly select one empty cell
                            WaterTransferTarget emptyTarget = emptyCells[Rand.Range(0, emptyCells.Count)];
                            
                            // Create a transfer to this cell
                            AddTransfer(map, pos, emptyTarget.Position, 1);
                            transfersCollected++;
                            
                            // Mark both tiles as affected
                            affectedTiles.Add(pos);
                            affectedTiles.Add(emptyTarget.Position);
                            
                            continue; // Skip to next active tile after creating a new water tile
                        }
                    }
                    
                    // If we can't create a new water tile, find existing water with lowest volume
                    var waterTargets = potentialTargets.FindAll(t => t.ExistingWater != null && t.Volume < FlowingWater.MaxVolume);
                    
                    if (waterTargets.Count > 0)
                    {
                        // Get the minimum volume difference from settings
                        int minVolumeDifference = LoadedModManager.GetMod<WaterSpringModMain>().settings.minVolumeDifferenceForTransfer;
                        
                        // Only transfer to targets that have sufficient volume difference
                        var validTargets = waterTargets.FindAll(t => t.Volume + minVolumeDifference <= water.Volume);
                        
                        // If there are no targets with sufficient volume difference, don't transfer
                        if (validTargets.Count == 0)
                        {
                            WaterSpringLogger.LogDebug($"Water at {pos} has no neighbors with sufficient volume difference, no transfer needed");
                            continue;
                        }
                        
                        // Find lowest volume among valid targets
                        int lowestVolume = int.MaxValue;
                        foreach (var target in validTargets)
                        {
                            if (target.Volume < lowestVolume)
                            {
                                lowestVolume = target.Volume;
                            }
                        }
                        
                        // Get all targets with this volume
                        var lowestVolumeTargets = validTargets.FindAll(t => t.Volume == lowestVolume);
                        
                        // Randomly select one target
                        WaterTransferTarget selectedTarget = lowestVolumeTargets[Rand.Range(0, lowestVolumeTargets.Count)];
                        
                        // Create a transfer to this cell
                        AddTransfer(map, pos, selectedTarget.Position, 1);
                        transfersCollected++;
                        
                        // Mark both tiles as affected
                        affectedTiles.Add(pos);
                        affectedTiles.Add(selectedTarget.Position);
                    }
                }
            }
            
            TotalTransfersCollected = transfersCollected;
            WaterSpringLogger.LogDebug($"WaterTransferManager: Collected {transfersCollected} transfers");
            
            return transfersCollected;
        }
        
        /// <summary>
        /// Add a water transfer to the pending transfers collection
        /// </summary>
        private void AddTransfer(Map map, IntVec3 source, IntVec3 destination, int amount)
        {
            // Create the transfer
            WaterTransfer transfer = new WaterTransfer
            {
                Source = source,
                Destination = destination,
                Amount = amount
            };
            
            // Add to pending transfers
            if (!pendingTransfers.ContainsKey(source))
            {
                pendingTransfers[source] = new List<WaterTransfer>();
            }
            pendingTransfers[source].Add(transfer);
            
            // Update final volumes
            finalVolumes[source] -= amount;
            
            // If destination doesn't have a final volume yet, check for existing water
            if (!finalVolumes.ContainsKey(destination))
            {
                FlowingWater existingWater = map.thingGrid.ThingAt<FlowingWater>(destination);
                finalVolumes[destination] = existingWater?.Volume ?? 0;
            }
            
            // Add the transfer amount to the destination's final volume
            finalVolumes[destination] += amount;
            
            WaterSpringLogger.LogDebug($"Added transfer: {amount} water from {source} to {destination}");
        }
        
        /// <summary>
        /// Calculation phase: Calculate final volumes after all transfers
        /// No changes are made to the actual water tiles yet
        /// </summary>
        public void CalculateFinalVolumes()
        {
            WaterSpringLogger.LogDebug("WaterTransferManager: Starting calculation phase");
            
            // The calculation is implicitly done during the collection phase
            // as we track the final volumes in the finalVolumes dictionary
            
            // Calculate entropy metrics to monitor system stability
            int totalVolumeDelta = 0;
            int totalWaterTiles = 0;
            int maxVolumeDelta = 0;
            int equalVolumeCount = 0;
            
            // Get maps for looking up current water volumes
            Map[] maps = Find.Maps.ToArray();
            
            foreach (var entry in finalVolumes)
            {
                FlowingWater water = null;
                foreach (Map map in maps)
                {
                    water = map.thingGrid.ThingAt<FlowingWater>(entry.Key);
                    if (water != null) break;
                }
                
                int currentVolume = water?.Volume ?? 0;
                int newVolume = entry.Value;
                int delta = Math.Abs(newVolume - currentVolume);
                
                totalVolumeDelta += delta;
                totalWaterTiles++;
                
                if (delta > maxVolumeDelta)
                {
                    maxVolumeDelta = delta;
                }
                
                if (delta == 0)
                {
                    equalVolumeCount++;
                }
            }
            
            // Calculate entropy metrics
            double percentUnchanged = totalWaterTiles > 0 ? ((double)equalVolumeCount / totalWaterTiles) * 100.0 : 100.0;
            double averageDelta = totalWaterTiles > 0 ? (double)totalVolumeDelta / totalWaterTiles : 0.0;
            
            // Log stability metrics
            WaterSpringLogger.LogDebug($"WaterTransferManager: Calculation complete. Entropy metrics:");
            WaterSpringLogger.LogDebug($"  - Total water tiles: {totalWaterTiles}");
            WaterSpringLogger.LogDebug($"  - Unchanged tiles: {equalVolumeCount} ({percentUnchanged:F1}%)");
            WaterSpringLogger.LogDebug($"  - Total volume delta: {totalVolumeDelta}");
            WaterSpringLogger.LogDebug($"  - Average delta: {averageDelta:F2} units/tile");
            WaterSpringLogger.LogDebug($"  - Maximum delta: {maxVolumeDelta} units");
            
            // The system is approaching equilibrium when:
            // 1. A high percentage of tiles are unchanged
            // 2. The average delta is very low
            // 3. The maximum delta is small
            
            if (percentUnchanged > 95.0 && maxVolumeDelta <= 1)
            {
                WaterSpringLogger.LogDebug("WaterTransferManager: System is approaching equilibrium.");
            }
        }
        
        /// <summary>
        /// Application phase: Apply all transfers at once
        /// </summary>
        /// <param name="map">The map to apply transfers to</param>
        /// <returns>Number of transfers applied</returns>
        public int ApplyTransfers(Map map)
        {
            if (map == null) return 0;
            
            WaterSpringLogger.LogDebug("WaterTransferManager: Starting application phase");
            
            int transfersApplied = 0;
            HashSet<IntVec3> createdWaterPositions = new HashSet<IntVec3>();
            HashSet<IntVec3> unchangedWaterPositions = new HashSet<IntVec3>();
            
            // Track all water in the processed area for stability tracking
            HashSet<IntVec3> allProcessedWaterPositions = new HashSet<IntVec3>();
            foreach (IntVec3 pos in processedTiles)
            {
                FlowingWater water = map.thingGrid.ThingAt<FlowingWater>(pos);
                if (water != null)
                {
                    allProcessedWaterPositions.Add(pos);
                }
            }
            
            // Apply final volumes to all affected tiles
            foreach (var entry in finalVolumes)
            {
                IntVec3 pos = entry.Key;
                int newVolume = entry.Value;
                
                // Skip if there's no change or volume is invalid
                if (newVolume < 0) continue;
                
                // Get or create water at this position
                FlowingWater water = map.thingGrid.ThingAt<FlowingWater>(pos);
                
                // If no water exists but we need to create it
                if (water == null && newVolume > 0)
                {
                    ThingDef waterDef = DefDatabase<ThingDef>.GetNamed("FlowingWater", false);
                    if (waterDef != null)
                    {
                        // Create new water
                        Thing newWater = ThingMaker.MakeThing(waterDef);
                        if (newWater != null && newWater is FlowingWater typedWater)
                        {
                            // Set initial volume to 0 to prevent auto-registration with normal method
                            // This is important for isolation between methods
                            typedWater.Volume = 0;
                            GenSpawn.Spawn(newWater, pos, map);
                            water = typedWater;
                            
                            // Remember that we created this water
                            createdWaterPositions.Add(pos);
                            
                            // Add to affected tiles since it's a new water
                            affectedTiles.Add(pos);
                            
                            // Register with multi-phase system
                            diffusionManager.RegisterActiveTile(map, pos);
                            
                            WaterSpringLogger.LogDebug($"Created new water at {pos} with volume {newVolume}");
                            transfersApplied++;
                        }
                    }
                }
                
                // If water exists, update its volume
                if (water != null)
                {
                    // Check if volume changed
                    if (water.Volume != newVolume)
                    {
                        // Set the new volume
                        int oldVolume = water.Volume;
                        water.Volume = newVolume;
                        
                        // Log the change
                        WaterSpringLogger.LogDebug($"Updated water at {pos} from volume {oldVolume} to {newVolume}");
                        transfersApplied++;
                        
                        // Ensure this position is marked as affected
                        if (!affectedTiles.Contains(pos))
                        {
                            affectedTiles.Add(pos);
                        }
                        
                        // If volume is now 0, the water will be automatically destroyed by the Volume setter
                    }
                    else
                    {
                        // Volume didn't change - track for stability updates
                        unchangedWaterPositions.Add(pos);
                    }
                }
            }
            
            // Update stability counters - USE OUR OWN INTERNAL TRACKING, not the water tiles' stability
            
            // First, RESET stability for affected/changed tiles
            foreach (IntVec3 pos in affectedTiles)
            {
                // Get the water at this position
                FlowingWater water = map.thingGrid.ThingAt<FlowingWater>(pos);
                if (water != null)
                {
                    // Reset our internal stability counter for this position
                    ResetStability(pos);
                    WaterSpringLogger.LogDebug($"WaterTransferManager: Reset stability for water at {pos}");
                    
                    // Register with the active tile system for continued processing
                    if (diffusionManager != null)
                    {
                        diffusionManager.RegisterActiveTile(map, pos);
                        
                        // Also activate neighbors since they might be affected next tick
                        diffusionManager.ActivateNeighbors(map, pos);
                    }
                }
            }
            
            // Second, check if the system as a whole is showing signs of equilibrium
            // If more than 95% of processed tiles had no volume change, boost stability gain
            double unchangedPercent = allProcessedWaterPositions.Count > 0 
                ? (double)unchangedWaterPositions.Count / allProcessedWaterPositions.Count * 100.0
                : 0.0;
                
            bool systemNearingEquilibrium = unchangedPercent >= 95.0;
            
            // Extra boost to stability when the system is nearing equilibrium
            int stabilityIncrement = systemNearingEquilibrium ? 3 : 1;
            
            if (systemNearingEquilibrium)
            {
                WaterSpringLogger.LogDebug($"WaterTransferManager: System nearing equilibrium ({unchangedPercent:F1}% unchanged). Boosting stability increment to {stabilityIncrement}");
            }
            
            // Third, INCREMENT stability for unchanged tiles
            foreach (IntVec3 pos in unchangedWaterPositions)
            {
                CheckAndMarkStableInternal(map, pos, stabilityIncrement);
            }
            
            // Find and increment stability for any processed water not accounted for
            foreach (IntVec3 pos in allProcessedWaterPositions)
            {
                if (!affectedTiles.Contains(pos) && !unchangedWaterPositions.Contains(pos))
                {
                    // This water position was processed but not changed or tracked
                    CheckAndMarkStableInternal(map, pos, stabilityIncrement);
                }
            }
            
            TransfersApplied = transfersApplied;
            WaterSpringLogger.LogDebug($"WaterTransferManager: Applied {transfersApplied} transfers, {affectedTiles.Count} tiles affected, {unchangedWaterPositions.Count} unchanged");
            
            return transfersApplied;
        }
        
        /// <summary>
        /// Helper method to check stability and mark as stable if threshold reached
        /// Original method that directly modifies FlowingWater - used for compatibility
        /// </summary>
        /// <param name="map">The map containing the water</param>
        /// <param name="pos">Position of the water tile</param>
        /// <param name="increment">Amount to increment stability by (default: 1)</param>
        private void CheckAndMarkStable(Map map, IntVec3 pos, int increment = 1)
        {
            FlowingWater water = map.thingGrid.ThingAt<FlowingWater>(pos);
            if (water != null)
            {
                // If already at max stability, don't increment further
                if (water.StabilityCounter >= FlowingWater.MaxStability)
                {
                    return;
                }
                
                // Check if this water is truly at equilibrium with its neighbors
                bool isAtTrueEquilibrium = IsTileAtTrueEquilibrium(map, pos);
                
                // If not at true equilibrium, reset stability rather than incrementing it
                if (!isAtTrueEquilibrium)
                {
                    WaterSpringLogger.LogDebug($"WaterTransferManager: Water at {pos} is NOT at true equilibrium. Resetting stability.");
                    water.ResetStabilityCounter();
                    return;
                }
                
                // If we're here, the water is at true equilibrium with neighbors
                
                // Increment stability counter multiple times if needed
                for (int i = 0; i < increment && water.StabilityCounter < FlowingWater.MaxStability; i++)
                {
                    water.IncrementStabilityCounter();
                }
                
                WaterSpringLogger.LogDebug($"WaterTransferManager: Incremented stability for water at {pos} to {water.StabilityCounter} (by {increment})");
                
                // Water.IncrementStabilityCounter will now automatically check and mark as stable 
                // when reaching max stability (no need to call MarkAsStable here)
                
                // If water meets the configurable stability threshold but not max stability yet
                if (water.IsStable() && water.StabilityCounter < FlowingWater.MaxStability && !water.IsExplicitlyDeregistered)
                {
                    // Mark as stable
                    water.MarkAsStable();
                    WaterSpringLogger.LogDebug($"WaterTransferManager: Water at {pos} marked as stable with stability {water.StabilityCounter}");
                }
            }
        }
        
        /// <summary>
        /// Internal version of CheckAndMarkStable that uses our own stability tracking
        /// This ensures isolation from the normal diffusion method
        /// </summary>
        private void CheckAndMarkStableInternal(Map map, IntVec3 pos, int increment = 1)
        {
            FlowingWater water = map.thingGrid.ThingAt<FlowingWater>(pos);
            if (water != null)
            {
                // If already at max stability, don't increment further
                if (IsFullyStable(pos))
                {
                    return;
                }
                
                // Check if this water is truly at equilibrium with its neighbors
                bool isAtTrueEquilibrium = IsTileAtTrueEquilibrium(map, pos);
                
                // If not at true equilibrium, reset stability rather than incrementing it
                if (!isAtTrueEquilibrium)
                {
                    WaterSpringLogger.LogDebug($"WaterTransferManager: Water at {pos} is NOT at true equilibrium. Resetting stability.");
                    ResetStability(pos);
                    return;
                }
                
                // If we're here, the water is at true equilibrium with neighbors
                
                // Increment stability counter
                IncrementStability(pos, increment);
                
                int currentStability = GetStabilityCounter(pos);
                WaterSpringLogger.LogDebug($"WaterTransferManager: Incremented stability for water at {pos} to {currentStability} (by {increment})");
                
                // If water reached the configurable stability cap
                if (IsStable(pos))
                {
                    // If we've reached max stability, unregister from active tiles
                    if (IsFullyStable(pos) && diffusionManager != null)
                    {
                        diffusionManager.UnregisterActiveTile(map, pos);
                        WaterSpringLogger.LogDebug($"WaterTransferManager: Water at {pos} reached max stability and was unregistered");
                    }
                }
            }
        }
        
        /// <summary>
        /// Determines if a water tile is at true equilibrium with its neighbors
        /// True equilibrium means:
        /// - All adjacent water tiles have the same volume, OR
        /// - All adjacent water tiles are at max volume, OR
        /// - This tile is at min volume (1) and all adjacent water tiles have >= volume
        /// </summary>
        private bool IsTileAtTrueEquilibrium(Map map, IntVec3 pos)
        {
            FlowingWater water = map.thingGrid.ThingAt<FlowingWater>(pos);
            if (water == null) return false;
            
            int currentVolume = water.Volume;
            
            // For a water tile to be stable, all adjacent walkable cells must either:
            // 1. Have water with the same volume
            // 2. Have water with max volume (7/7)
            // 3. Be non-walkable (wall/mountain)
            // Any other scenario means water should keep flowing
            
            // Get all adjacent walkable cells
            List<IntVec3> adjacentCells = new List<IntVec3>();
            List<FlowingWater> adjacentWaters = new List<FlowingWater>();
            List<int> adjacentVolumes = new List<int>();
            
            foreach (IntVec3 neighbor in GenAdj.CardinalDirections)
            {
                IntVec3 adjacentCell = pos + neighbor;
                
                // Skip if not in bounds
                if (!adjacentCell.InBounds(map)) continue;
                
                // Skip non-walkable cells (walls, mountains, etc.)
                if (!adjacentCell.Walkable(map)) continue;
                
                // Check for solid buildings
                bool hasBuilding = false;
                foreach (Thing thing in adjacentCell.GetThingList(map))
                {
                    if (thing.def.fillPercent > 0.1f && thing.def.category == ThingCategory.Building)
                    {
                        hasBuilding = true;
                        break;
                    }
                }
                if (hasBuilding) continue;
                
                // This is a walkable cell - check if it has water
                FlowingWater adjacentWater = map.thingGrid.ThingAt<FlowingWater>(adjacentCell);
                
                adjacentCells.Add(adjacentCell);
                adjacentWaters.Add(adjacentWater); // May be null if no water
                adjacentVolumes.Add(adjacentWater?.Volume ?? 0);
            }
            
            // If there are no adjacent walkable cells, this water is isolated (surrounded by walls)
            // and should be considered stable regardless of volume
            if (adjacentCells.Count == 0) return true;
            
            // If there are any adjacent walkable cells without water, this water is not at equilibrium
            // unless it's at minimum volume (1)
            if (adjacentWaters.Contains(null))
            {
                // Water can only be stable next to empty walkable cells if it's at minimum volume (1)
                return currentVolume <= 1;
            }
            
            // Check if any adjacent water has significantly different volume
            bool hasLowerVolumeNeighbor = false;
            bool hasHigherVolumeNeighbor = false;
            int minVolumeDifference = LoadedModManager.GetMod<WaterSpringModMain>().settings.minVolumeDifferenceForTransfer;
            
            foreach (int neighborVolume in adjacentVolumes)
            {
                if (neighborVolume < currentVolume - minVolumeDifference)
                {
                    hasLowerVolumeNeighbor = true;
                }
                else if (neighborVolume > currentVolume + minVolumeDifference)
                {
                    hasHigherVolumeNeighbor = true;
                }
            }
            
            // If we have both higher and lower volume neighbors, we're not at equilibrium
            if (hasLowerVolumeNeighbor && hasHigherVolumeNeighbor) return false;
            
            // If we have lower volume neighbors and we're not at minimum volume, we're not at equilibrium
            if (hasLowerVolumeNeighbor && currentVolume > 1) return false;
            
            // If we have higher volume neighbors and we're not at maximum volume, we're not at equilibrium
            if (hasHigherVolumeNeighbor && currentVolume < FlowingWater.MaxVolume) return false;
            
            // Special case: all tiles have the same volume (true equilibrium)
            bool allSameVolume = true;
            int firstVolume = adjacentVolumes[0];
            
            for (int i = 1; i < adjacentVolumes.Count; i++)
            {
                if (Math.Abs(adjacentVolumes[i] - firstVolume) > minVolumeDifference)
                {
                    allSameVolume = false;
                    break;
                }
            }
            
            if (allSameVolume && Math.Abs(currentVolume - firstVolume) <= minVolumeDifference) return true;
            
            // If we've reached here, there's probably still potential for water to flow
            return false;
        }
        
        /// <summary>
        /// Struct to represent a potential water transfer target
        /// </summary>
        private struct WaterTransferTarget
        {
            public IntVec3 Position;
            public FlowingWater ExistingWater;
            public int Volume;
        }
        
        /// <summary>
        /// Struct to represent a water transfer
        /// </summary>
        public struct WaterTransfer
        {
            public IntVec3 Source;
            public IntVec3 Destination;
            public int Amount;
            
            public override string ToString()
            {
                return $"{Amount} water from {Source} to {Destination}";
            }
        }
    }
}
