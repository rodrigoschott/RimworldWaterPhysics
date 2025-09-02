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
                    // Update graphics
                    UpdateGraphic();
                    
                    // Destroy this water if volume is zero
                    if (_volume <= 0 && Spawned && !Destroyed)
                    {
                        this.Destroy();
                    }
                }
            }
        }
        
        private int lastDrawnVolume = -1;
        private int ticksUntilNextCheck = 0; // Timer for local diffusion checks
        
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _volume, "volume", 1);
            Scribe_Values.Look(ref ticksUntilNextCheck, "ticksUntilNextCheck", 0);
        }
        
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            // Force graphic update when spawned
            UpdateGraphic();
            
            // Set initial diffusion check time
            ticksUntilNextCheck = Rand.Range(10, 30); // Stagger checks to prevent all waters updating at once
        }
        
        protected override void Tick()
        {
            base.Tick();
            
            // Update graphic if volume has changed
            if (lastDrawnVolume != Volume)
            {
                UpdateGraphic();
            }
            
            // Handle local water diffusion within the water entity itself
            ticksUntilNextCheck--;
            if (ticksUntilNextCheck <= 0)
            {
                AttemptLocalDiffusion();
                
                // Reset timer - this is much more frequent than the global diffusion
                ticksUntilNextCheck = Rand.Range(30, 60);
            }
        }
        
        // This method lets the individual water check its surroundings and potentially spread
        private void AttemptLocalDiffusion()
        {
            if (Volume <= 1 || Map == null || !Spawned) return;
            
            WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Starting diffusion check for water at {Position} with volume {Volume}");
            
            // Only attempt to spread if we have enough volume
            if (Volume > 1)
            {
                // Check adjacent cells (cardinal directions only)
                IntVec3 pos = Position;
                IntVec3[] validCells = new IntVec3[4]; // Store valid cells
                FlowingWater[] existingWaters = new FlowingWater[4]; // Store existing water objects
                int[] volumes = new int[4]; // Store volumes (or -1 for empty cells)
                int validCount = 0;
                
                WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Scanning adjacent cells for water at {Position}");
                
                // First pass: Find all valid cells and their contents
                foreach (IntVec3 neighbor in GenAdj.CardinalDirections)
                {
                    IntVec3 adjacentCell = pos + neighbor;
                    
                    // Skip if not valid or not walkable
                    if (!adjacentCell.InBounds(Map) || !adjacentCell.Walkable(Map))
                    {
                        WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Cell {adjacentCell} is not valid (not in bounds or not walkable)");
                        continue;
                    }
                    
                    // Check for solid buildings
                    bool hasBuilding = false;
                    foreach (Thing thing in adjacentCell.GetThingList(Map))
                    {
                        if (thing.def.fillPercent > 0.1f && thing.def.category == ThingCategory.Building)
                        {
                            hasBuilding = true;
                            break;
                        }
                    }
                    if (hasBuilding)
                    {
                        WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Cell {adjacentCell} has a solid building, skipping");
                        continue;
                    }
                    
                    // Look for existing water in this cell
                    FlowingWater existingWater = null;
                    foreach (Thing thing in adjacentCell.GetThingList(Map))
                    {
                        if (thing is FlowingWater water)
                        {
                            existingWater = water;
                            break;
                        }
                    }
                    
                    // Store valid cell info
                    validCells[validCount] = adjacentCell;
                    existingWaters[validCount] = existingWater;
                    volumes[validCount] = existingWater != null ? existingWater.Volume : -1;
                    
                    WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Found valid cell {adjacentCell} with " + 
                                             (existingWater != null ? $"existing water volume {existingWater.Volume}" : "no water"));
                    
                    validCount++;
                }
                
                // If we found valid cells, choose the best one to transfer water to
                if (validCount > 0)
                {
                    WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Found {validCount} valid adjacent cells");
                    
                    // First, check if there are any empty cells where we can create new water
                    // This is a priority to expand the water area
                    if (Volume > 2)
                    {
                        // Find all empty cells and randomly select one
                        var emptyCellIndices = new System.Collections.Generic.List<int>();
                        
                        for (int i = 0; i < validCount; i++)
                        {
                            if (existingWaters[i] == null)
                            {
                                emptyCellIndices.Add(i);
                            }
                        }
                        
                        // Randomly select one of the empty cells if any were found
                        int emptyIndex = -1;
                        if (emptyCellIndices.Count > 0)
                        {
                            emptyIndex = emptyCellIndices[Rand.Range(0, emptyCellIndices.Count)];
                            WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Randomly selected empty cell {validCells[emptyIndex]} from {emptyCellIndices.Count} empty cells");
                        }
                        
                        // If we found an empty cell, create new water
                        if (emptyIndex >= 0)
                        {
                            WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Creating new water at empty cell {validCells[emptyIndex]}");
                            ThingDef waterDef = DefDatabase<ThingDef>.GetNamed("FlowingWater", false);
                            if (waterDef != null)
                            {
                                Thing newWater = ThingMaker.MakeThing(waterDef);
                                if (newWater != null && newWater is FlowingWater typedWater)
                                {
                                    typedWater.Volume = 0;
                                    GenSpawn.Spawn(newWater, validCells[emptyIndex], Map);
                                    TransferVolume(typedWater);
                                    return;
                                }
                            }
                        }
                    }
                    
                    // Second priority: Transfer to existing water with lowest volume
                    var lowestVolumeIndices = new System.Collections.Generic.List<int>();
                    int lowestVolume = int.MaxValue;
                    
                    // First pass: Find the lowest volume
                    for (int i = 0; i < validCount; i++)
                    {
                        if (existingWaters[i] != null && existingWaters[i].Volume < MaxVolume)
                        {
                            if (existingWaters[i].Volume < lowestVolume)
                            {
                                lowestVolume = existingWaters[i].Volume;
                                lowestVolumeIndices.Clear();
                                lowestVolumeIndices.Add(i);
                            }
                            else if (existingWaters[i].Volume == lowestVolume)
                            {
                                lowestVolumeIndices.Add(i);
                            }
                        }
                    }
                    
                    // If we found water to transfer to, randomly select one of the lowest volume cells
                    if (lowestVolumeIndices.Count > 0)
                    {
                        int lowestVolumeIndex = lowestVolumeIndices[Rand.Range(0, lowestVolumeIndices.Count)];
                        WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Randomly selected water cell {validCells[lowestVolumeIndex]} from {lowestVolumeIndices.Count} cells with volume {lowestVolume}");
                    
                        WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: Transferring to existing water at {validCells[lowestVolumeIndex]} with volume {existingWaters[lowestVolumeIndex].Volume}");
                        TransferVolume(existingWaters[lowestVolumeIndex]);
                        return;
                    }
                    
                    // If no empty cells and all adjacent water cells are at max volume,
                    // we simply wait and do nothing
                    WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: No available cells to transfer to - all adjacent water is at max volume");
                }
                else
                {
                    WaterSpringLogger.LogDebug($"FlowingWater.AttemptLocalDiffusion: No valid adjacent cells found for water at {Position}");
                }
            }
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
                // This ensures diffusion continues even when volume differences are small
                int transferAmount = Math.Min(1, Math.Min(this.Volume, MaxVolume - neighbor.Volume));
                if (transferAmount <= 0) return false;
                
                WaterSpringLogger.LogDebug($"FlowingWater.TransferVolume: Transferring {transferAmount} volume from {Position} (vol:{Volume}) to {neighbor.Position} (vol:{neighbor.Volume})");
                
                neighbor.AddVolume(transferAmount);
                Volume -= transferAmount; // Use the property setter
                
                WaterSpringLogger.LogDebug($"FlowingWater.TransferVolume: After transfer - Source: {Volume}, Target: {neighbor.Volume}");
                
                return true;
            }
            return false;
        }
        
        public override string GetInspectString()
        {
            return base.GetInspectString() + $"\nWater volume: {Volume}/{MaxVolume}";
        }
    }
}