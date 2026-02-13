using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace WaterSpringMod.WaterSpring
{
    [StaticConstructorOnStartup]
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
                    if (Spawned && Map != null)
                    {
                        GameComponent_WaterDiffusion diffusionManager = Current.Game.GetComponent<GameComponent_WaterDiffusion>();
                        if (diffusionManager != null)
                        {
                            // Wake this tile
                            diffusionManager.RegisterActiveTile(Map, Position);
                            ResetStabilityCounter();
                            // Clear explicit deregister so this tile gets processed again
                            isExplicitlyDeregistered = false;

                            // DF rule: "STABLE -> FLOWING when adjacent tile changes depth"
                            // Wake all cardinal neighbors so they can re-evaluate transfers
                            foreach (IntVec3 dir in GenAdj.CardinalDirections)
                            {
                                IntVec3 adj = Position + dir;
                                if (!adj.InBounds(Map)) continue;
                                FlowingWater adjWater = Map.thingGrid.ThingAt<FlowingWater>(adj);
                                if (adjWater != null && adjWater.Spawned && !adjWater.Destroyed)
                                {
                                    adjWater.isExplicitlyDeregistered = false;
                                    adjWater.ResetStabilityCounter();
                                    diffusionManager.RegisterActiveTile(Map, adj);
                                }
                            }
                        }
                    }
                    
                    // Destroy this water if volume is zero — UNLESS it sits on a hole (drain receptor)
                    if (_volume <= 0 && Spawned && !Destroyed)
                    {
                        bool isOnHole = Map != null && VerticalPortalBridge.IsHoleAt(Map, Position);
                        if (!isOnHole)
                        {
                            // Before destroying, restore original terrain if needed
                            TryRestoreOriginalTerrain();
                            // If this tile is adjacent to a hole, wake upper maps because the lower outlet changed
                            try { VerticalPortalBridge.PropagateVerticalActivationForCellAndCardinals(Map, Position); } catch { }
                            this.Destroy();
                        }
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

    private int pressureCooldownRemaining = 0;

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
            Scribe_Values.Look(ref isSpringSourceTile, "isSpringSourceTile", false);
            Scribe_Values.Look(ref nextEvapCheckTick, "nextEvapCheckTick", -1);
            Scribe_Values.Look(ref pressureCooldownRemaining, "pressureCooldownRemaining", 0);
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
            if (Map == null || !Spawned) return false;
            // Hole tiles drain even at volume 1 (aggressive gravity); others need volume >= 2
            bool isOnHole = VerticalPortalBridge.IsHoleAt(Map, Position);
            if (Volume <= 0 && !isOnHole) return false;
            if (Volume <= 1 && !isOnHole) return false;
            var settings = LoadedModManager.GetMod<WaterSpringModMain>().settings;
            bool debug = settings.debugModeEnabled;
            if (debug)
            {
                WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Starting diffusion check for water at {Position} with volume {Volume}");
            }
            
            IntVec3 pos = Position;

            // GRAVITY PRIORITY (no cooldown, no volume gate): holes drain every tick at ANY volume > 0
            // Splash distribution: BFS from tile below, distribute across all reachable tiles with capacity
            if (isOnHole && Volume > 0)
            {
                if (VerticalPortalBridge.TryGetLowerMap(Map, out var selfLower) && pos.InBounds(selfLower))
                {
                    bool splashed = PressurePropagation.TrySplashDistribute(
                        this, selfLower, pos, this.Volume, settings, debug);
                    if (splashed) return true;
                }
            }

            // Horizontal diffusion + pressure require Volume > 1
            if (Volume > 1)
            {
                // Check adjacent cells (cardinal directions only)
                // ⚠️ INCREASED from 8 to 12 to accommodate stairs+elevators+void portals
                IntVec3[] validCells = new IntVec3[12];
                FlowingWater[] existingWaters = new FlowingWater[12];
                Map[] targetMaps = new Map[12];
                IntVec3[] targetCells = new IntVec3[12];
                int validCount = 0;

                if (debug)
                {
                    WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Scanning adjacent cells for water at {Position}");
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

                    // Check for WS_Hole building on neighbor (gravity shortcut)
                    // Splash distribution: BFS from tile below the hole, distribute across all reachable tiles
                    if (VerticalPortalBridge.IsHoleAt(Map, adjacentCell))
                    {
                        if (VerticalPortalBridge.TryGetLowerMap(Map, out var lowerMap) && adjacentCell.InBounds(lowerMap))
                        {
                            bool splashed = PressurePropagation.TrySplashDistribute(
                                this, lowerMap, adjacentCell, this.Volume, settings, debug);
                            if (splashed) return true;
                        }
                        continue; // Hole tile: always skip as same-map horizontal candidate
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
                
                // If we found valid cells, transfer to ALL eligible targets
                if (validCount > 0)
                {
                    if (debug)
                    {
                        WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Found {validCount} valid adjacent cells");
                    }

                    bool anyTransferred = false;

                    // PASS 1: Expansion to empty cells (spawn new water)
                    // Expand to ONE random empty cell per tick (spawning is expensive)
                    if (Volume >= 2)
                    {
                        int emptyIndex = -1;
                        int emptySeen = 0;
                        for (int i = 0; i < validCount; i++)
                        {
                            if (existingWaters[i] == null)
                            {
                                emptySeen++;
                                if (Rand.Range(0, emptySeen) == 0) emptyIndex = i;
                            }
                        }

                        if (emptyIndex >= 0)
                        {
                            if (debug)
                            {
                                WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Expanding to empty cell {validCells[emptyIndex]}");
                            }
                            ThingDef waterDef = DefDatabase<ThingDef>.GetNamed("FlowingWater", false);
                            if (waterDef != null)
                            {
                                Thing newWater = ThingMaker.MakeThing(waterDef);
                                if (newWater is FlowingWater typedWater)
                                {
                                    typedWater.Volume = 0;
                                    Map destMap = targetMaps[emptyIndex] ?? Map;
                                    IntVec3 destCell = targetCells[emptyIndex];
                                    GenSpawn.Spawn(newWater, destCell, destMap);

                                    // DF rule: transfer 1 unit to empty tile
                                    int expAmount = 1;
                                    if (this.Volume > 1)
                                    {
                                        typedWater.AddVolume(expAmount);
                                        this.Volume -= expAmount;
                                        anyTransferred = true;

                                        if (settings.useActiveTileSystem)
                                        {
                                            var dm = Current.Game.GetComponent<GameComponent_WaterDiffusion>();
                                            dm?.RegisterActiveTile(this.Map, this.Position);
                                            dm?.RegisterActiveTile(destMap, destCell);
                                            this.ResetStabilityCounter();
                                            typedWater.ResetStabilityCounter();
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // PASS 2: Transfer to ALL existing water neighbors with lower volume
                    // Process each eligible neighbor independently with equilibrium transfer
                    if (this.Volume >= 2) // Still have water to give after expansion
                    {
                        for (int i = 0; i < validCount; i++)
                        {
                            if (this.Volume <= 1) break; // Stop if we're down to 1

                            var nw = existingWaters[i];
                            if (nw == null || nw.Volume >= MaxVolume) continue;

                            int diff = this.Volume - nw.Volume;
                            if (diff < 2) continue; // Equilibrium: diff 0-1 means no transfer

                            // DF rule: transfer exactly 1 unit per eligible neighbor pair
                            int transferAmount = 1;

                            if (debug)
                            {
                                WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Multi-transfer {transferAmount} to {validCells[i]} (vol {nw.Volume}) [diff={diff}]");
                            }

                            nw.AddVolume(transferAmount);
                            this.Volume -= transferAmount;
                            anyTransferred = true;

                            // Register both tiles as active
                            if (settings.useActiveTileSystem)
                            {
                                var dm = Current.Game.GetComponent<GameComponent_WaterDiffusion>();
                                dm?.RegisterActiveTile(this.Map, this.Position);
                                dm?.RegisterActiveTile(targetMaps[i], targetCells[i]);
                                this.ResetStabilityCounter();
                                nw.ResetStabilityCounter();
                            }
                        }
                    }

                    if (anyTransferred)
                    {
                        return true;
                    }

                    if (debug)
                    {
                        WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: No eligible transfers (all neighbors at equilibrium or max)");
                    }
                }
                else
                {
                    if (debug)
                    {
                        WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: No valid adjacent cells found for water at {Position}");
                    }
                }

                // PRESSURE FALLBACK: If tile is at max volume and no normal transfers worked,
                // try pressure BFS to find outlets through connected 7/7 tiles (including cross-map via holes).
                // This fires for any stuck full tile: spring sources, tiles fed by gravity, etc.
                if (settings.pressurePropagationEnabled && this.Volume >= MaxVolume)
                {
                    if (pressureCooldownRemaining > 0)
                    {
                        pressureCooldownRemaining--;
                    }
                    else
                    {
                        if (debug)
                        {
                            WaterSpringLogger.LogDebug($"[Pressure] Fallback: tile at {pos} is full with no transfers. Trying BFS.");
                        }

                        bool pressured = PressurePropagation.TryPropagate(this, Map, settings, debug);
                        if (pressured)
                        {
                            pressureCooldownRemaining = settings.pressureCooldownTicks;
                            return true;
                        }
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

        // Volume overlay: draw volume number on every water tile (debug mode)
        // DrawGUIOverlay is the correct hook for 2D text labels on the map.
        public override void DrawGUIOverlay()
        {
            base.DrawGUIOverlay();
            var s = LoadedModManager.GetMod<WaterSpringModMain>()?.settings;
            if (s == null || !s.debugModeEnabled) return;
            if (!Spawned || Map == null || Find.CurrentMap != Map) return;

            // Only show when zoomed in enough to read labels (same threshold RimWorld uses for item labels)
            if (Find.CameraDriver.CurrentZoom > CameraZoomRange.Close) return;

            GenMapUI.DrawThingLabel(this, Volume.ToString(), new Color(1f, 1f, 1f, 0.9f));
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

            if (neighbor.Volume >= MaxVolume || this.Volume <= 0)
                return false;

            int diff = this.Volume - neighbor.Volume;
            if (diff <= 1) return false; // diff=1 means at equilibrium; diff<=0 means neighbor is equal or higher

            // DF rule: transfer exactly 1 unit per pair per tick
            int transferAmount = 1;

            var settings = LoadedModManager.GetMod<WaterSpringModMain>().settings;
            bool debug = settings.debugModeEnabled;

            if (debug)
            {
                WaterSpringLogger.LogDebug($"FlowingWater.TransferVolume: Transferring {transferAmount} volume from {Position} (vol:{Volume}) to {neighbor.Position} (vol:{neighbor.Volume}) [diff={diff}]");
            }

            neighbor.AddVolume(transferAmount);
            Volume -= transferAmount;

            if (debug)
            {
                WaterSpringLogger.LogDebug($"FlowingWater.TransferVolume: After transfer - Source: {Volume}, Target: {neighbor.Volume}");
            }

            // Register both tiles as active
            if (settings.useActiveTileSystem)
            {
                GameComponent_WaterDiffusion diffusionManager = Current.Game.GetComponent<GameComponent_WaterDiffusion>();
                if (diffusionManager != null)
                {
                    diffusionManager.RegisterActiveTile(Map, Position);
                    diffusionManager.RegisterActiveTile(neighbor.Map, neighbor.Position);
                    this.ResetStabilityCounter();
                    neighbor.ResetStabilityCounter();
                }
            }

            return true;
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