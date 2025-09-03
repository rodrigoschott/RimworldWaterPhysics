using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace WaterSpringMod.WaterSpring
{
    public class FlowingWater : ThingWithComps
    {
        private int _volume = 1; // Internal volume storage
        public const int MaxVolume = 7; // Maximum volume for the tile
    public const int MaxStability = 10000; // Upper hard ceiling; actual cap is configurable via settings
        
        // Stability tracking for the active tile system
        private int stabilityCounter = 0;
        private bool isExplicitlyDeregistered = false; // Flag to prevent re-registration when volume changes but tile is stable
        
        // Track volume changes for debugging purposes
        private int previousVolume = 1;
        private int volumeChangeCounter = 0;
        private int ticksSinceLastChange = 0;
        
        // Properly encapsulated Volume property with validation
        public int Volume
        {
            get => _volume;
            set
            {
                int oldVolume = _volume;
                _volume = Math.Max(0, Math.Min(value, MaxVolume)); // Ensure volume stays between 0 and MaxVolume
                
                // Check if volume changed
                if (oldVolume != _volume)
                {
                    // Update debug tracking
                    previousVolume = oldVolume;
                    volumeChangeCounter++;
                    ticksSinceLastChange = 0;
                    
                    // Update graphics
                    UpdateGraphic();
                    if (Spawned && Map != null && !isExplicitlyDeregistered && !IsStable())
                    {
                        GameComponent_WaterDiffusion diffusionManager = Current.Game.GetComponent<GameComponent_WaterDiffusion>();
                        if (diffusionManager != null)
                        {
                            diffusionManager.RegisterActiveTile(Map, Position);
                // Reset stability counter when volume changes (do not clear explicit deregister here)
                ResetStabilityCounter();
                // Note: Do NOT trigger ReactivateInRadius here to avoid re-entrant immediate transfers.
                        }
                    }
                    
                    // Destroy this water if volume is zero
                    if (_volume <= 0 && Spawned && !Destroyed)
                    {
                        this.Destroy();
                    }
                }
            }
        }
        
        // Stability counter properties for the active tile system
        public int StabilityCounter => stabilityCounter;
        public bool IsExplicitlyDeregistered => isExplicitlyDeregistered;
        
        public void IncrementStabilityCounter()
        {
            var settings = LoadedModManager.GetMod<WaterSpringModMain>().settings;
            bool debug = settings.debugModeEnabled;
            if (stabilityCounter < MaxStability)
            {
                stabilityCounter++;
                if (debug)
                {
                    WaterSpringLogger.LogDebug($"FlowingWater.IncrementStabilityCounter: Water at {Position} stability now {stabilityCounter}");
                }
                
                // Determine configured cap and clamp
                int cap = Mathf.Clamp(settings.stabilityCap, 1, MaxStability);
                stabilityCounter = Math.Min(stabilityCounter, cap);

                // Auto-mark as stable when reaching configured cap
                var s = settings;
                bool neverStableSpring = isSpringSourceTile && s.springNeverStabilize;
                if (!neverStableSpring && stabilityCounter >= cap)
                {
                    MarkAsStable();
                    if (debug)
                    {
                        WaterSpringLogger.LogDebug($"FlowingWater.IncrementStabilityCounter: Water at {Position} reached stability cap ({cap}) and is now fully deactivated");
                    }
                }
            }
        }
        
        public void ResetStabilityCounter()
        {
            stabilityCounter = 0;
            // Do NOT clear explicit deregistration here; Reactivate() is responsible for that.
            var settings = LoadedModManager.GetMod<WaterSpringModMain>().settings;
            if (settings.debugModeEnabled)
            {
                WaterSpringLogger.LogDebug($"FlowingWater.ResetStabilityCounter: Water at {Position} stability reset to 0");
            }
        }
        
        // Helper method to check if the water is considered stable
        public bool IsStable()
        {
            // If we've reached the configured cap, always return true
            var s = LoadedModManager.GetMod<WaterSpringModMain>().settings;
            if (isSpringSourceTile && s.springNeverStabilize)
            {
                return false;
            }
            int cap = Mathf.Clamp(s.stabilityCap, 1, MaxStability);
            if (stabilityCounter >= cap) return true;
            
            // Otherwise, not stable yet
            return false;
        }
        
        // Get the processing frequency based on stability level
        public int GetProcessingInterval()
        {
            WaterSpringModSettings settings = LoadedModManager.GetMod<WaterSpringModMain>().settings;
            
            // If water is fully stable (at configured cap), return a very large value to effectively disable processing
            int capCheck = Mathf.Clamp(settings.stabilityCap, 1, MaxStability);
            bool neverStableSpring = isSpringSourceTile && settings.springNeverStabilize;
            if (!neverStableSpring && (stabilityCounter >= capCheck || isExplicitlyDeregistered))
            {
                return int.MaxValue; // Effectively never process
            }
            
            // Strategy 2 removed: always use base path (scheduler spaces work with local intervals)
            return 1;
        }
        
        // New method to explicitly deregister this water from active processing
        public void MarkAsStable()
        {
            if (!isExplicitlyDeregistered)
            {
                isExplicitlyDeregistered = true;
                var settings = LoadedModManager.GetMod<WaterSpringModMain>().settings;
                if (settings.debugModeEnabled)
                {
                    WaterSpringLogger.LogDebug($"FlowingWater.MarkAsStable: Water at {Position} marked as stable and will not auto-register");
                }
                
                // Unregister from the active tile system
                GameComponent_WaterDiffusion diffusionManager = Current.Game.GetComponent<GameComponent_WaterDiffusion>();
                if (diffusionManager != null && Map != null && Spawned)
                {
                    diffusionManager.UnregisterActiveTile(Map, Position);
                }
            }
        }
        
        // Method to reactivate a stable water tile when external conditions change
        public void Reactivate()
        {
            if (!isExplicitlyDeregistered) return;
            isExplicitlyDeregistered = false;
            ResetStabilityCounter();
            // Ensure immediate processing next tick
            ticksUntilNextCheck = 0;
            var settings = LoadedModManager.GetMod<WaterSpringModMain>().settings;
            if (settings.debugModeEnabled)
            {
                WaterSpringLogger.LogDebug($"FlowingWater.Reactivate: Water at {Position} reactivated");
            }
            
            // Register with the active tile system
            GameComponent_WaterDiffusion diffusionManager = Current.Game.GetComponent<GameComponent_WaterDiffusion>();
            if (diffusionManager != null && Map != null && Spawned)
            {
                diffusionManager.RegisterActiveTile(Map, Position);
            }
        }
        
        private int lastDrawnVolume = -1;
        public int ticksUntilNextCheck = 0; // Timer for local diffusion checks - public for access by GameComponent_WaterDiffusion
        private int tickCounter = 0; // For tracking ticks for tiered processing

    // Anti-backflow tracking
    private IntVec3 lastInboundFrom = IntVec3.Invalid;
    private int backflowCooldownRemaining = 0;
    private bool isSpringSourceTile = false; // set by spring on spawn
        
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _volume, "volume", 1);
            Scribe_Values.Look(ref ticksUntilNextCheck, "ticksUntilNextCheck", 0);
            Scribe_Values.Look(ref stabilityCounter, "stabilityCounter", 0);
            Scribe_Values.Look(ref isExplicitlyDeregistered, "isExplicitlyDeregistered", false);
            Scribe_Values.Look(ref tickCounter, "tickCounter", 0);
            Scribe_Values.Look(ref previousVolume, "previousVolume", 1);
            Scribe_Values.Look(ref volumeChangeCounter, "volumeChangeCounter", 0);
            Scribe_Values.Look(ref ticksSinceLastChange, "ticksSinceLastChange", 0);
            Scribe_Values.Look(ref lastInboundFrom, "lastInboundFrom", IntVec3.Invalid);
            Scribe_Values.Look(ref backflowCooldownRemaining, "backflowCooldownRemaining", 0);
            Scribe_Values.Look(ref isSpringSourceTile, "isSpringSourceTile", false);
        }
        
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            
            // Force graphic update when spawned
            UpdateGraphic();
            
            // Set initial diffusion check time (use settings; bias slightly earlier for spawn)
            var settings = LoadedModManager.GetMod<WaterSpringModMain>().settings;
            int minI0 = Mathf.Max(1, settings.localCheckIntervalMin / 2);
            int maxI0 = Mathf.Max(minI0, settings.localCheckIntervalMax / 2 + 1);
            ticksUntilNextCheck = Rand.Range(minI0, maxI0);
            
            // Register with the active tile system
            GameComponent_WaterDiffusion diffusionManager = Current.Game.GetComponent<GameComponent_WaterDiffusion>();
            if (diffusionManager != null)
            {
                diffusionManager.RegisterActiveTile(map, Position);
                ResetStabilityCounter();
            }
        }
        
        // Method to detect if a wall or other barrier has been added or removed nearby
        public bool HasEnvironmentChanged(Map map)
        {
            if (map == null || !Spawned) return false;
            
            // Check adjacent cells for changed walkability or fillPercent
            foreach (IntVec3 neighbor in GenAdj.CardinalDirections)
            {
                IntVec3 adjacentCell = Position + neighbor;
                
                // If the cell is outside map bounds, skip it
                if (!adjacentCell.InBounds(map)) continue;
                
                // Check if the cell's walkability or structures have changed
                // We can't easily detect changes directly, so we'll just check current state
                // and rely on external systems to call this method when relevant changes occur
                if (!adjacentCell.Walkable(map))
                {
                    // If there's a non-walkable cell adjacent, that's a potential boundary
                    // Future enhancement: Track which cells are boundaries and detect changes
                    continue;
                }
            }
            
            return false; // No changes detected
        }
        
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            // Unregister from active tile system when despawned
            GameComponent_WaterDiffusion diffusionManager = Current.Game.GetComponent<GameComponent_WaterDiffusion>();
            if (diffusionManager != null)
            {
                diffusionManager.UnregisterActiveTile(Map, Position);
            }
            
            base.DeSpawn(mode);
        }
        
        protected override void Tick()
        {
            base.Tick();
            
            // Increment counter for time since last volume change
            ticksSinceLastChange++;
            
            // Update graphic if volume has changed
            if (lastDrawnVolume != Volume)
            {
                UpdateGraphic();
            }
            
            // Increment the tick counter for tiered processing
            tickCounter++;
            
            bool useActiveTiles = LoadedModManager.GetMod<WaterSpringModMain>().settings.useActiveTileSystem;
            var settings = LoadedModManager.GetMod<WaterSpringModMain>().settings;
            
            // Handle local water diffusion within the water entity itself
            // Skip diffusion logic if using active tile system, as it will be handled by GameComponent_WaterDiffusion
            if (!useActiveTiles)
            {
                // Original non-active tile system behavior
                ticksUntilNextCheck--;
                if (ticksUntilNextCheck <= 0)
                {
                    bool diffusionOccurred = AttemptLocalDiffusion();
                    
                    // Reset timer using settings
                    int minI = Mathf.Max(1, settings.localCheckIntervalMin);
                    int maxI = Mathf.Max(minI, settings.localCheckIntervalMax);
                    ticksUntilNextCheck = Rand.Range(minI, maxI);
                }
            }
            else
            {
                // For active tile system, we only count down the timer
                // The actual processing happens in GameComponent_WaterDiffusion
                if (ticksUntilNextCheck > 0)
                {
                    ticksUntilNextCheck--;
                }
                
                // When timer reaches zero, only register if not stable and not explicitly deregistered
                if (ticksUntilNextCheck <= 0)
                {
                    if (!IsStable() && !isExplicitlyDeregistered)
                    {
                        GameComponent_WaterDiffusion diffusionManager = Current.Game.GetComponent<GameComponent_WaterDiffusion>();
                        diffusionManager?.RegisterActiveTile(Map, Position);
                    }
                }
            }
        }
        
        // This method lets the individual water check its surroundings and potentially spread
        // Returns true if diffusion occurred, false otherwise
    public bool AttemptLocalDiffusion()
        {
            if (Volume <= 1 || Map == null || !Spawned) return false;
            var settings = LoadedModManager.GetMod<WaterSpringModMain>().settings;
            bool debug = settings.debugModeEnabled;
            if (debug)
            {
                WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Starting diffusion check for water at {Position} with volume {Volume}");
            }
            
            // Only attempt to spread if we have enough volume
            if (Volume > 1)
            {
                // Check adjacent cells (cardinal directions only)
                IntVec3 pos = Position;
                IntVec3[] validCells = new IntVec3[4]; // Store valid cells
                FlowingWater[] existingWaters = new FlowingWater[4]; // Store existing water objects
                int validCount = 0;
                
                if (debug)
                {
                    WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Scanning adjacent cells for water at {Position}");
                }
                
                // First pass: Find all valid cells and their contents
                foreach (IntVec3 neighbor in GenAdj.CardinalDirections)
                {
                    IntVec3 adjacentCell = pos + neighbor;
                    
                    // Skip if not valid or not walkable
                    if (!adjacentCell.InBounds(Map) || !adjacentCell.Walkable(Map))
                    {
                        if (debug)
                        {
                            WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Cell {adjacentCell} is not valid (not in bounds or not walkable)");
                        }
                        continue;
                    }
                    
                    // Check for solid buildings
                    Building ed = adjacentCell.GetEdifice(Map);
                    if (ed != null && ed.def != null && ed.def.fillPercent > 0.1f)
                    {
                        if (debug)
                        {
                            WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Cell {adjacentCell} has a solid building, skipping");
                        }
                        continue;
                    }
                    
                    // Look for existing water in this cell
                    FlowingWater existingWater = Map.thingGrid.ThingAt<FlowingWater>(adjacentCell);
                    
                    // Store valid cell info
                    validCells[validCount] = adjacentCell;
                    existingWaters[validCount] = existingWater;
                    
                    if (debug)
                    {
                        WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Found valid cell {adjacentCell} with " + 
                                                 (existingWater != null ? $"existing water volume {existingWater.Volume}" : "no water"));
                    }
                    
                    validCount++;
                }
                
                // If we found valid cells, choose the best one to transfer water to
                if (validCount > 0)
                {
                    if (debug)
                    {
                        WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Found {validCount} valid adjacent cells");
                    }
                    
                    // Anti-backflow: decay cooldown over time
                    if (backflowCooldownRemaining > 0) backflowCooldownRemaining--;

                    // First, check if there are any empty cells where we can create new water
                    // This is a priority to expand the water area. Note: If a min diff is required for transfers,
                    // expansion provides a path forward even when neighbors are equal.
                    // Allow expansion when there's at least 2 units to split
                    if (Volume >= 2)
                    {
                        // Reservoir-sample an empty cell (uniform without allocations)
                        int emptyIndex = -1;
                        int emptySeen = 0;
                        for (int i = 0; i < validCount; i++)
                        {
                            if (existingWaters[i] == null)
                            {
                                emptySeen++;
                                // pick with 1/emptySeen probability
                                if (Rand.Range(0, emptySeen) == 0)
                                {
                                    emptyIndex = i;
                                }
                            }
                        }
                        if (debug && emptySeen > 0 && emptyIndex >= 0)
                        {
                            WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Selected empty cell {validCells[emptyIndex]} from {emptySeen} empty cells");
                        }
                        
                        // If we found an empty cell, create new water
                        if (emptyIndex >= 0)
                        {
                            if (debug)
                            {
                                WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Creating new water at empty cell {validCells[emptyIndex]}");
                            }
                            ThingDef waterDef = DefDatabase<ThingDef>.GetNamed("FlowingWater", false);
                            if (waterDef != null)
                            {
                                Thing newWater = ThingMaker.MakeThing(waterDef);
                                if (newWater != null && newWater is FlowingWater typedWater)
                                {
                                    typedWater.Volume = 0;
                                    GenSpawn.Spawn(newWater, validCells[emptyIndex], Map);
                                    bool moved = TransferVolume(typedWater);
                                    if (moved)
                                    {
                                        // Mark inbound direction for the new tile to reduce immediate backflow
                                        typedWater.lastInboundFrom = this.Position;
                                        typedWater.backflowCooldownRemaining = Mathf.Max(typedWater.backflowCooldownRemaining, LoadedModManager.GetMod<WaterSpringModMain>().settings.backflowCooldownTicks);
                                    }
                                    return true; // Diffusion occurred
                                }
                            }
                        }
                    }
                    
                    // Second priority: Transfer to existing water with lowest volume (respect min volume difference)
                    int lowestVolume = int.MaxValue;
                    
                    // First pass: Find the lowest volume
                    for (int i = 0; i < validCount; i++)
                    {
                        if (existingWaters[i] != null && existingWaters[i].Volume < MaxVolume)
                        {
                            if (existingWaters[i].Volume < lowestVolume)
                            {
                                lowestVolume = existingWaters[i].Volume;
                            }
                        }
                    }
                    
                    // If we found water to transfer to, randomly select one of the lowest volume cells
                    if (lowestVolume != int.MaxValue)
                    {
                        // Enforce minimum volume difference to avoid equal-volume oscillation
                        int minDiff = settings.minVolumeDifferenceForTransfer;
                        int chosen = -1;
                        int eligSeen = 0;
                        for (int i = 0; i < validCount; i++)
                        {
                            var nw = existingWaters[i];
                            if (nw == null) continue;
                            if (nw.Volume != lowestVolume) continue;
                            int neighborVol = nw.Volume;
                            int required = minDiff;
                            if (settings.antiBackflowEnabled && backflowCooldownRemaining > 0 && validCells[i] == lastInboundFrom)
                            {
                                required += Mathf.Max(0, settings.backflowMinDiffBonus);
                            }
                            if ((this.Volume - neighborVol) >= required)
                            {
                                eligSeen++;
                                if (Rand.Range(0, eligSeen) == 0)
                                {
                                    chosen = i;
                                }
                            }
                        }
                        if (chosen >= 0)
                        {
                            if (debug)
                            {
                                WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Transferring to existing water at {validCells[chosen]} with volume {existingWaters[chosen].Volume} (minDiff {minDiff})");
                            }
                            bool transferred = TransferVolume(existingWaters[chosen]);
                            if (transferred && settings.antiBackflowEnabled)
                            {
                                // Update inbound/outbound markers to discourage immediate backflow
                                existingWaters[chosen].lastInboundFrom = this.Position;
                                existingWaters[chosen].backflowCooldownRemaining = Mathf.Max(existingWaters[chosen].backflowCooldownRemaining, settings.backflowCooldownTicks);
                            }
                            return transferred; // Return whether diffusion actually occurred
                        }
                        else if (debug)
                        {
                            WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: No eligible neighbor meets min volume difference {minDiff}; no transfer");
                        }
                    }
                    
                    // If no empty cells and all adjacent water cells are at max volume,
                    // we simply wait and do nothing
                    if (debug)
                    {
                        WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: No available cells to transfer to - all adjacent water is at max volume");
                    }
                }
                else
                {
                    if (debug)
                    {
                        WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: No valid adjacent cells found for water at {Position}");
                    }
                }
            }
            
            return false; // No diffusion occurred
        }
        
        private void UpdateGraphic()
        {
            lastDrawnVolume = Volume;
            
            // Update draw properties based on volume
            if (Map != null)
            {
                Map.mapDrawer.MapMeshDirty(Position, MapMeshFlagDefOf.Things);
            }
        }

        public void AddVolume(int amount)
        {
            if (amount == 0) return;
            Volume = Volume + amount; // Use the property setter for validation
        }

        public bool TransferVolume(FlowingWater neighbor)
        {
            if (neighbor == null || neighbor.Destroyed || !neighbor.Spawned || Destroyed || !Spawned)
                return false;
                
            if (neighbor.Volume < MaxVolume && this.Volume > 0)
            {
                // Always transfer if neighbor isn't at max volume and this water has volume to give
                // Enforce minimum difference for transfers to existing water to prevent ping-pong
                var s = LoadedModManager.GetMod<WaterSpringModMain>().settings;
                int minDiff = Mathf.Max(0, s.minVolumeDifferenceForTransfer);
                bool debug = s.debugModeEnabled;
                bool neighborIsNewlySpawned = neighbor.Volume == 0; // allow expansion
                if (!neighborIsNewlySpawned)
                {
                    if ((this.Volume - neighbor.Volume) < minDiff)
                    {
                        return false;
                    }
                }
                int transferAmount = Math.Min(1, Math.Min(this.Volume, MaxVolume - neighbor.Volume));
                if (transferAmount <= 0) return false;
                
                if (debug)
                {
                    WaterSpringLogger.LogDebug($"FlowingWater.TransferVolume: Transferring {transferAmount} volume from {Position} (vol:{Volume}) to {neighbor.Position} (vol:{neighbor.Volume})");
                }
                
                neighbor.AddVolume(transferAmount);
                Volume -= transferAmount; // Use the property setter
                
                if (debug)
                {
                    WaterSpringLogger.LogDebug($"FlowingWater.TransferVolume: After transfer - Source: {Volume}, Target: {neighbor.Volume}");
                }
                
                // Make sure to register both tiles as active in the system
                if (LoadedModManager.GetMod<WaterSpringModMain>().settings.useActiveTileSystem)
                {
                    GameComponent_WaterDiffusion diffusionManager = Current.Game.GetComponent<GameComponent_WaterDiffusion>();
                    if (diffusionManager != null)
                    {
                        // Register this tile and the neighbor for active processing
                        diffusionManager.RegisterActiveTile(Map, Position);
                        diffusionManager.RegisterActiveTile(neighbor.Map, neighbor.Position);
                        
                        // Reset stability counters since they've changed
                        this.ResetStabilityCounter();
                        neighbor.ResetStabilityCounter();
                    }
                }
                
                return true;
            }
            return false;
        }
        
        public override string GetInspectString()
        {
            string baseString = base.GetInspectString();
            // Build without leading newlines to satisfy RimWorld's inspector (no empty lines allowed)
            System.Text.StringBuilder sb = new System.Text.StringBuilder(128);
            if (!string.IsNullOrEmpty(baseString))
            {
                sb.Append(baseString.Trim());
            }

            // Water volume (always shown)
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("Water volume: ").Append(Volume).Append('/').Append(MaxVolume);

            // Stability info when active tile system is enabled
            var mod = LoadedModManager.GetMod<WaterSpringModMain>();
            var settings = mod?.settings;
            if (settings != null && settings.useActiveTileSystem)
            {
                int cap = Mathf.Clamp(settings.stabilityCap, 1, MaxStability);
                sb.Append('\n').Append("Stability: ").Append(stabilityCounter).Append('/').Append(cap);
                if (isSpringSourceTile && settings.springNeverStabilize)
                {
                    sb.Append(" (spring source)");
                }
                if (stabilityCounter >= MaxStability)
                {
                    sb.Append(" (Fully Stable)");
                }
                else if (isExplicitlyDeregistered)
                {
                    sb.Append(" (Stable)");
                }

                // Volume change history for debugging
                sb.Append('\n').Append("Previous vol: ").Append(previousVolume)
                  .Append(", Changes: ").Append(volumeChangeCounter);
                if (volumeChangeCounter > 0)
                {
                    sb.Append(", Time since last: ").Append(ticksSinceLastChange).Append(" ticks");
                }
            }

            return sb.ToString();
        }

        // Mark this tile as a spring source tile (affects stability behavior/priority)
        public void MarkAsSpringSource(bool neverStabilize)
        {
            isSpringSourceTile = true;
            if (neverStabilize)
            {
                ResetStabilityCounter();
                isExplicitlyDeregistered = false;
            }
        }
    }
}