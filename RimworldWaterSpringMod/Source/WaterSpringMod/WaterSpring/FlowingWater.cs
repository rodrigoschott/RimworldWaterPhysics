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
    // Terrain sync state
    private TerrainDef originalTerrain; // cached original terrain to restore when volume goes to 0
    private int lastAppliedBand = -1;   // -1 unknown, 0 none/original, 1 shallow, 2 deep
        
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
                    // Sync terrain to volume bands if enabled and spawned
                    if (Spawned && Map != null)
                    {
                        TrySyncTerrainToVolume();
                    }
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
                        // Before destroying, restore original terrain if needed
                        TryRestoreOriginalTerrain();
                        // If this tile is a hole or adjacent to one, wake upper maps because the lower outlet changed
                        try { VerticalPortalBridge.PropagateVerticalActivationForCellAndCardinals(Map, Position); } catch { }
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
    // Evaporation scheduling
    private int nextEvapCheckTick = -1;

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
            Scribe_Values.Look(ref nextEvapCheckTick, "nextEvapCheckTick", -1);
        }
        
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            
            // Force graphic update when spawned
            UpdateGraphic();

            // Capture original terrain when first spawning
            if (map != null)
            {
                if (originalTerrain == null)
                {
                    originalTerrain = map.terrainGrid.TerrainAt(Position);
                }
                // Apply terrain based on current volume if setting is enabled
                TrySyncTerrainToVolume();
            }
            
            // Set initial diffusion check time (use settings; bias slightly earlier for spawn)
            var settings = LoadedModManager.GetMod<WaterSpringModMain>().settings;
            int minI0 = Mathf.Max(1, settings.localCheckIntervalMin / 2);
            int maxI0 = Mathf.Max(minI0, settings.localCheckIntervalMax / 2 + 1);
            ticksUntilNextCheck = Rand.Range(minI0, maxI0);

            // Initialize evaporation schedule with a random phase to avoid spikes
            if (settings != null && settings.evaporationEnabled)
            {
                int now = Find.TickManager.TicksGame;
                int phase = Rand.Range(0, Mathf.Max(1, settings.evaporationIntervalTicks));
                nextEvapCheckTick = now + settings.evaporationIntervalTicks + phase;
            }
            
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
                // Also ensure terrain is in sync after volume change
                if (Spawned && Map != null)
                {
                    TrySyncTerrainToVolume();
                }
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

            // Evaporation check (very cheap per tick)
            var s = LoadedModManager.GetMod<WaterSpringModMain>()?.settings;
            if (s != null && s.evaporationEnabled && Map != null && Spawned)
            {
                if (nextEvapCheckTick < 0)
                {
                    // Late init if needed
                    int now = Find.TickManager.TicksGame;
                    int phase = Rand.Range(0, Mathf.Max(1, s.evaporationIntervalTicks));
                    nextEvapCheckTick = now + s.evaporationIntervalTicks + phase;
                }
                int tickNow = Find.TickManager.TicksGame;
                if (tickNow >= nextEvapCheckTick)
                {
                    // Schedule next
                    nextEvapCheckTick = tickNow + Mathf.Max(1, s.evaporationIntervalTicks);
                    // Only stable tiles evaporate
                    if (IsStable())
                    {
                        bool roofed = Map.roofGrid.Roofed(Position);
                        if (!(s.evaporationOnlyUnroofed && roofed))
                        {
                            if (Volume <= Mathf.Clamp(s.evaporationMaxVolumeThreshold, 0, MaxVolume))
                            {
                                int baseChance = Mathf.Clamp(s.evaporationChancePercent, 0, 100);
                                int chance = (!roofed) ? baseChance : Mathf.Clamp(s.evaporationChancePercentRoofed, 0, 100);
                                if (chance > 0 && Rand.RangeInclusive(1, 100) <= chance)
                                {
                                    // Evaporate one unit
                                    if (Volume > 0)
                                    {
                                        Volume = Volume - 1;
                                        // Reactivate tile to resume processing only if still present
                                        if (!Destroyed && Volume > 0)
                                        {
                                            // Wake this tile
                                            Reactivate();
                                            // Nudge neighbors and a small radius to resume diffusion promptly
                                            var dm = Current.Game?.GetComponent<GameComponent_WaterDiffusion>();
                                            dm?.ActivateNeighbors(Map, Position);
                                            dm?.ReactivateInRadius(Map, Position);
                                            // Propagate upward in case this affects vertical flow through holes
                                            try { VerticalPortalBridge.PropagateVerticalActivationForCellAndCardinals(Map, Position); } catch { }
                                        }
                                        // Optional: visual flash in debug
                                        if (s.debugModeEnabled)
                                        {
                                            Map.debugDrawer.FlashCell(Position, 0.5f, "evap");
                                        }
                                    }
                                }
                            }
                        }
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
                // ⚠️ INCREASED from 8 to 12 to accommodate stairs+elevators+void portals
                IntVec3[] validCells = new IntVec3[12];
                FlowingWater[] existingWaters = new FlowingWater[12]; // parallel storage for targets (same or lower map)
                Map[] targetMaps = new Map[12]; // track which map each target belongs to
                IntVec3[] targetCells = new IntVec3[12]; // track target cell (same or lower map)
                int validCount = 0;
                
                if (debug)
                {
                    WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Scanning adjacent cells for water at {Position}");
                }
                
                // If current tile is a hole, add a straight-down candidate first
                if (VerticalPortalBridge.IsHoleAt(Map, pos))
                {
                    if (VerticalPortalBridge.TryGetLowerMap(Map, out var selfLower))
                    {
                        if (pos.InBounds(selfLower))
                        {
                            FlowingWater selfLowerWater = selfLower.thingGrid.ThingAt<FlowingWater>(pos);
                            validCells[validCount] = pos;
                            targetCells[validCount] = pos;
                            targetMaps[validCount] = selfLower;
                            existingWaters[validCount] = selfLowerWater;
                            if (debug)
                            {
                                WaterSpringLogger.LogDebug($"[Portal] self-hole at {pos}; lower map #{selfLower.uniqueID}; target is {(selfLowerWater!=null?"existing":"empty")} water");
                            }
                            validCount++;
                        }
                        else if (debug)
                        {
                            WaterSpringLogger.LogDebug($"[Portal] self-hole: lower map does not contain cell {pos}");
                        }
                    }
                    else if (debug)
                    {
                        WaterSpringLogger.LogDebug("[Portal] self-hole: lower map not resolved");
                    }
                }

                // First pass: Find all valid cells and their contents
                foreach (IntVec3 neighbor in GenAdj.CardinalDirections)
                {
                    IntVec3 adjacentCell = pos + neighbor;
                    if (!adjacentCell.InBounds(Map)) continue;

                    // NEW: Check for stairs/portals (MultiFloors integration)
                    if (MultiFloorsIntegration.IsAvailable && settings.stairWaterFlowEnabled && validCount < validCells.Length)
                    {
                        if (MultiFloorsIntegration.TryGetStairDestination(Map, adjacentCell, Volume, settings, 
                            out Map stairDestMap, out IntVec3 stairDestCell, out bool isDownward))
                        {
                            FlowingWater destWater = stairDestMap.thingGrid.ThingAt<FlowingWater>(stairDestCell);
                            validCells[validCount] = adjacentCell;
                            targetCells[validCount] = stairDestCell;
                            targetMaps[validCount] = stairDestMap;
                            existingWaters[validCount] = destWater;
                            if (debug)
                            {
                                WaterSpringLogger.LogDebug($"[Stair] {(isDownward ? "downward" : "upward")} stair at {adjacentCell}; dest map #{stairDestMap.uniqueID} cell {stairDestCell}");
                            }
                            validCount++;
                            continue; // Don't also treat as normal neighbor
                        }
                    }

                    // NEW: Check for elevators (MultiFloors Phase 5)
                    if (MultiFloorsIntegration.IsAvailable && settings.elevatorWaterFlowEnabled && validCount < validCells.Length)
                    {
                        if (MultiFloorsIntegration.TryGetElevatorDestination(Map, adjacentCell, Volume, settings,
                            out Map elevDestMap, out IntVec3 elevDestCell))
                        {
                            FlowingWater elevWater = elevDestMap.thingGrid.ThingAt<FlowingWater>(elevDestCell);
                            validCells[validCount] = adjacentCell;
                            targetCells[validCount] = elevDestCell;
                            targetMaps[validCount] = elevDestMap;
                            existingWaters[validCount] = elevWater;
                            if (debug)
                            {
                                WaterSpringLogger.LogDebug($"[Elevator] shaft at {adjacentCell}; dest map #{elevDestMap.uniqueID} cell {elevDestCell}");
                            }
                            validCount++;
                            continue; // Don't also treat as normal neighbor
                        }
                    }

                    // Check for WS_Hole building first (even if not walkable)
                    if (VerticalPortalBridge.IsHoleAt(Map, adjacentCell))
                    {
                        if (VerticalPortalBridge.TryGetLowerMap(Map, out var lowerMap))
                        {
                            if (adjacentCell.InBounds(lowerMap))
                            {
                                bool passable = true;
                                if (!adjacentCell.Walkable(lowerMap))
                                {
                                    TerrainDef tLower = lowerMap.terrainGrid.TerrainAt(adjacentCell);
                                    if (!(tLower == TerrainDefOf.WaterShallow || tLower == TerrainDefOf.WaterDeep))
                                    {
                                        passable = false;
                                    }
                                }
                                if (passable)
                                {
                                    FlowingWater lowerWater = lowerMap.thingGrid.ThingAt<FlowingWater>(adjacentCell);
                                    validCells[validCount] = adjacentCell;
                                    targetCells[validCount] = adjacentCell;
                                    targetMaps[validCount] = lowerMap;
                                    existingWaters[validCount] = lowerWater;
                                    if (debug)
                                    {
                                        WaterSpringLogger.LogDebug($"[Portal] neighbor-hole at {adjacentCell}; lower map #{lowerMap.uniqueID}; target is {(lowerWater!=null?"existing":"empty")} water");
                                    }
                                    validCount++;
                                }
                                else if (debug)
                                {
                                    WaterSpringLogger.LogDebug($"[Portal] neighbor-hole: lower cell {adjacentCell} not passable and not water; skipping");
                                }
                            }
                            else if (debug)
                            {
                                WaterSpringLogger.LogDebug($"[Portal] neighbor-hole: lower map does not contain cell {adjacentCell}; skipping");
                            }
                        }
                        else if (debug)
                        {
                            WaterSpringLogger.LogDebug($"[Portal] neighbor-hole: lower map not resolved for {adjacentCell}");
                        }
                        // Do not add same-map candidate for hole tile (acts as portal)
                        continue;
                    }

                    // Non-void neighbor: require passability on this map
                    if (!IsCellPassableForWater(adjacentCell))
                    {
                        if (debug)
                        {
                            WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Cell {adjacentCell} is not passable");
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
                    
                    // Store valid cell info for same-map neighbor
                    validCells[validCount] = adjacentCell;
                    targetCells[validCount] = adjacentCell;
                    targetMaps[validCount] = Map;
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
                                    Map destMap = targetMaps[emptyIndex] ?? Map;
                                    IntVec3 destCell = targetCells[emptyIndex];
                                    GenSpawn.Spawn(newWater, destCell, destMap);
                                    bool moved = false;
                                    if (destMap == this.Map)
                                    {
                                        moved = TransferVolume(typedWater);
                                    }
                                    else
                                    {
                                        // Manual 1-unit transfer across maps; respect capacity and min-diff (dest is newly spawned => allowed)
                                        if (this.Volume > 0)
                                        {
                                            typedWater.AddVolume(1);
                                            this.Volume -= 1;
                                            if (debug)
                                            {
                                                WaterSpringLogger.LogDebug($"[Portal] cross-map expansion: {this.Position} (vol now {this.Volume}) -> {destCell} on map #{destMap.uniqueID} (new water vol 1)");
                                            }
                                            moved = true;
                                            if (LoadedModManager.GetMod<WaterSpringModMain>().settings.useActiveTileSystem)
                                            {
                                                var diffusionManager = Current.Game.GetComponent<GameComponent_WaterDiffusion>();
                                                diffusionManager?.RegisterActiveTile(this.Map, this.Position);
                                                diffusionManager?.RegisterActiveTile(destMap, destCell);
                                                this.ResetStabilityCounter();
                                                typedWater.ResetStabilityCounter();
                                            }
                                        }
                                    }
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
                                WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Transferring to existing water at {validCells[chosen]} (map {(targetMaps[chosen]==Map?"same":"lower")}) with volume {existingWaters[chosen].Volume} (minDiff {minDiff})");
                            }
                            bool transferred = false;
                            if (targetMaps[chosen] == this.Map)
                            {
                                transferred = TransferVolume(existingWaters[chosen]);
                            }
                            else
                            {
                                // Cross-map transfer: enforce min-diff and capacity
                                var dest = existingWaters[chosen];
                                if (dest.Volume < MaxVolume)
                                {
                                    int required = minDiff;
                                    if ((this.Volume - dest.Volume) >= required)
                                    {
                                        dest.AddVolume(1);
                                        this.Volume -= 1;
                                        if (debug)
                                        {
                                            WaterSpringLogger.LogDebug($"[Portal] cross-map transfer: {this.Position} -> {targetCells[chosen]} on map #{targetMaps[chosen].uniqueID}; src vol now {this.Volume}, dest vol now {dest.Volume}");
                                        }
                                        transferred = true;
                                        if (settings.useActiveTileSystem)
                                        {
                                            var diffusionManager = Current.Game.GetComponent<GameComponent_WaterDiffusion>();
                                            diffusionManager?.RegisterActiveTile(this.Map, this.Position);
                                            diffusionManager?.RegisterActiveTile(targetMaps[chosen], targetCells[chosen]);
                                            this.ResetStabilityCounter();
                                            dest.ResetStabilityCounter();
                                        }
                                    }
                                }
                            }
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

        // Hide the thing's visual when terrain is mirroring the water band
    // Cached water overlay materials (avoid GC allocation per frame)
        private static UnityEngine.Material _waterShallowMat;
        private static UnityEngine.Material _waterDeepMat;
        
        private static UnityEngine.Material WaterShallowMaterial
        {
            get
            {
                if (_waterShallowMat == null)
                    _waterShallowMat = SolidColorMaterials.SimpleSolidColorMaterial(
                        new UnityEngine.Color(0.25f, 0.45f, 0.82f, 0.38f));
                return _waterShallowMat;
            }
        }
        
        private static UnityEngine.Material WaterDeepMaterial
        {
            get
            {
                if (_waterDeepMat == null)
                    _waterDeepMat = SolidColorMaterials.SimpleSolidColorMaterial(
                        new UnityEngine.Color(0.15f, 0.30f, 0.75f, 0.55f));
                return _waterDeepMat;
            }
        }
        
    protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            var s = LoadedModManager.GetMod<WaterSpringModMain>()?.settings;
            bool isUpperLevel = MultiFloorsIntegration.IsAvailable && MultiFloorsIntegration.GetLevel(Map) > 0;
            
            if (isUpperLevel)
            {
                // On MF upper levels, foundations cover both terrain and low-altitude Things.
                // Draw a semi-transparent water overlay ABOVE foundations using a full-tile plane.
                // Still do terrain sync for gameplay (pathfinding, beauty), just not for visuals.
                UnityEngine.Material mat = (Volume >= 5) ? WaterDeepMaterial : WaterShallowMaterial;
                float altitude = Verse.Altitudes.AltitudeFor(Verse.AltitudeLayer.MetaOverlays);
                UnityEngine.Vector3 pos = Position.ToVector3ShiftedWithAltitude(altitude);
                UnityEngine.Graphics.DrawMesh(MeshPool.plane10, pos, UnityEngine.Quaternion.identity, mat, 0);
                return;
            }
            
            // Ground level: use terrain sync if enabled (natural water terrain look)
            if (s != null && s.syncTerrainToWaterVolume)
            {
                return;
            }
            
            base.DrawAt(drawLoc, flip);
        }

        // Compute band from current volume: 0 none, 1 shallow (1-4), 2 deep (5-7)
        private int ComputeBand()
        {
            if (Volume <= 0) return 0;
            if (Volume <= 4) return 1;
            return 2;
        }

        // Apply terrain to mirror volume band, if enabled
        private void TrySyncTerrainToVolume()
        {
            var s = LoadedModManager.GetMod<WaterSpringModMain>()?.settings;
            if (s == null || !s.syncTerrainToWaterVolume || Map == null || !Spawned) return;

            var grid = Map.terrainGrid;
            if (grid == null) return;

            // Capture original terrain once
            if (originalTerrain == null)
            {
                originalTerrain = grid.TerrainAt(Position);
            }

            int band = ComputeBand();
            if (band == lastAppliedBand) return; // no change needed

            if (band == 0)
            {
                // Restore original
                if (originalTerrain != null)
                {
                    grid.SetTerrain(Position, originalTerrain);
                }
            }
            else if (band == 1)
            {
                // Shallow water
                grid.SetTerrain(Position, TerrainDefOf.WaterShallow);
            }
            else // band == 2
            {
                grid.SetTerrain(Position, TerrainDefOf.WaterDeep);
            }

            lastAppliedBand = band;
        }

        private void TryRestoreOriginalTerrain()
        {
            if (Map == null) return;
            if (originalTerrain != null)
            {
                Map.terrainGrid.SetTerrain(Position, originalTerrain);
            }
            lastAppliedBand = 0;
        }

        // Determine if a cell is passable for water flow, treating water terrain as passable
        private bool IsCellPassableForWater(IntVec3 cell)
        {
            if (!cell.Walkable(Map))
            {
                // If not walkable due to water terrain, allow; otherwise, block
                TerrainDef t = Map.terrainGrid.TerrainAt(cell);
                if (t != null && (t == TerrainDefOf.WaterShallow || t == TerrainDefOf.WaterDeep))
                {
                    return true;
                }
                return false;
            }
            return true;
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