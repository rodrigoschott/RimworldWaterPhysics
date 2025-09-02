using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WaterSpringMod.WaterSpring
{
    public class Building_WaterSpring : Building
    {
        private int ticksUntilNextWaterSpawn = 60; // Start with a short timer

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            
            WaterSpringLogger.LogDebug($"Building_WaterSpring.SpawnSetup: Spring at {Position} being setup. Respawning: {respawningAfterLoad}");
            WaterSpringLogger.LogDebug($"Building_WaterSpring.SpawnSetup: ThingID: {this.ThingID}, Def: {this.def.defName}, TickerType: {this.def.tickerType}");
            
            if (this.def.tickerType == TickerType.Never)
            {
                WaterSpringLogger.LogWarning($"Building_WaterSpring.SpawnSetup: WARNING - TickerType is set to Never! This will prevent Tick from being called.");
            }
            
            // Register for ticking if needed
            if (Find.TickManager != null)
            {
                WaterSpringLogger.LogDebug($"Building_WaterSpring.SpawnSetup: TickManager exists, making sure we're registered");
            }
            
            if (Spawned)
            {
                // Force a short initial water spawn time
                ticksUntilNextWaterSpawn = 60;
                WaterSpringLogger.LogDebug($"Building_WaterSpring.SpawnSetup: Spring is spawned, set initial spawn timer to {ticksUntilNextWaterSpawn} ticks");
            }
            
            // Try to spawn water immediately for testing
            WaterSpringLogger.LogDebug($"Building_WaterSpring.SpawnSetup: Attempting initial water spawn for testing...");
            SpawnFlowingWater();
        }
        
        // Let's make absolutely sure Tick is getting called
        private int tickCounter = 0;
        private bool firstTickLogged = false;
        
        protected override void Tick()
        {
            base.Tick();
            
            // Add an immediate log for the first tick to verify the method is being called
            if (!firstTickLogged)
            {
                WaterSpringLogger.LogDebug($"Building_WaterSpring.Tick: FIRST TICK DETECTED for spring at {Position}");
                firstTickLogged = true;
            }
            
            // Count total ticks for diagnostic purposes
            tickCounter++;
            
            if (tickCounter % 250 == 0)
            {
                WaterSpringLogger.LogDebug($"Building_WaterSpring.Tick: Spring at {Position} has been ticked {tickCounter} times");
            }
            
            if (!Spawned)
            {
                WaterSpringLogger.LogWarning($"Building_WaterSpring.Tick: Spring at {Position} is not spawned!");
                return;
            }
            
            ticksUntilNextWaterSpawn--;
            
            // Log more frequently to catch the issue
            if (ticksUntilNextWaterSpawn % 10 == 0 || ticksUntilNextWaterSpawn < 5)
            {
                WaterSpringLogger.LogDebug($"Building_WaterSpring.Tick: Spring at {Position} has {ticksUntilNextWaterSpawn} ticks until next spawn");
            }
            
            if (ticksUntilNextWaterSpawn <= 0)
            {
                WaterSpringLogger.LogDebug($"Building_WaterSpring.Tick: Spring at {Position} triggering water spawn!");
                SpawnFlowingWater();
                
                // Get settings from the mod
                WaterSpringModSettings settings = LoadedModManager.GetMod<WaterSpringModMain>()?.GetSettings<WaterSpringModSettings>();
                int newInterval = settings != null ? settings.waterSpringSpawnInterval : 200;
                ticksUntilNextWaterSpawn = newInterval;
                WaterSpringLogger.LogDebug($"Building_WaterSpring.Tick: Next spawn scheduled in {newInterval} ticks");
            }
        }

        private void SpawnFlowingWater()
        {
            WaterSpringLogger.LogDebug($"SpawnFlowingWater: Starting water spawn process at {Position}");
            
            if (Map == null)
            {
                WaterSpringLogger.LogWarning($"SpawnFlowingWater: Map is null, cannot spawn water!");
                return;
            }
            
            IntVec3 position = this.Position;
            WaterSpringLogger.LogDebug($"SpawnFlowingWater: Target position: {position}");
            
            // Check if position is valid
            if (!position.IsValid)
            {
                WaterSpringLogger.LogWarning($"SpawnFlowingWater: Position {position} is not valid!");
                return;
            }
            
            if (!position.InBounds(Map))
            {
                WaterSpringLogger.LogWarning($"SpawnFlowingWater: Position {position} is not in bounds of map!");
                return;
            }
            
            WaterSpringLogger.LogDebug($"SpawnFlowingWater: Position validation passed");
            
            // Check for existing water
            bool waterExists = false;
            FlowingWater existingWater = null;
            
            WaterSpringLogger.LogDebug($"SpawnFlowingWater: Checking for existing water at {position}");
            List<Thing> thingsAtPosition = new List<Thing>();
            foreach (Thing t in position.GetThingList(Map))
            {
                thingsAtPosition.Add(t);
            }
            WaterSpringLogger.LogDebug($"SpawnFlowingWater: Found {thingsAtPosition.Count} things at position");
            
            foreach (Thing thing in thingsAtPosition)
            {
                WaterSpringLogger.LogDebug($"SpawnFlowingWater: Found thing: {thing.def.defName} (ThingID: {thing.ThingID}) at position");
                if (thing.def.defName == "FlowingWater")
                {
                    waterExists = true;
                    existingWater = thing as FlowingWater;
                    WaterSpringLogger.LogDebug($"SpawnFlowingWater: Found existing water with volume: {existingWater?.Volume}");
                    break;
                }
            }
            
            if (waterExists && existingWater != null)
            {
                // Add volume to existing water
                int oldVolume = existingWater.Volume;
                WaterSpringLogger.LogDebug($"SpawnFlowingWater: Adding volume to existing water. Current volume: {oldVolume}");
                existingWater.AddVolume(1);
                WaterSpringLogger.LogDebug($"SpawnFlowingWater: Added volume to existing water. Old volume: {oldVolume}, New volume: {existingWater.Volume}");
            }
            else
            {
                // Create new water
                WaterSpringLogger.LogDebug($"SpawnFlowingWater: No existing water found, creating new water");
                
                // Let's verify the ThingDef exists in the database
                ThingDef waterDef = DefDatabase<ThingDef>.GetNamed("FlowingWater", false);
                
                if (waterDef == null)
                {
                    // Try to find the def if it exists with a different name
                    WaterSpringLogger.LogWarning($"SpawnFlowingWater: Could not find 'FlowingWater' ThingDef! Checking all defs...");
                    foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
                    {
                        if (def.defName.Contains("Water") || def.defName.Contains("water"))
                        {
                            WaterSpringLogger.LogWarning($"SpawnFlowingWater: Found potential water def: {def.defName}");
                        }
                    }
                    return;
                }
                
                WaterSpringLogger.LogDebug($"SpawnFlowingWater: Found FlowingWater ThingDef. Label: {waterDef.label}, Category: {waterDef.category}");
                Thing flowingWater = ThingMaker.MakeThing(waterDef);
                
                if (flowingWater != null)
                {
                    WaterSpringLogger.LogDebug($"SpawnFlowingWater: Successfully created FlowingWater thing of type {flowingWater.GetType().FullName}");
                    
                    if (flowingWater is FlowingWater typedWater)
                    {
                        WaterSpringLogger.LogDebug($"SpawnFlowingWater: Setting initial volume to 1");
                        typedWater.Volume = 1;
                        
                        try
                        {
                            // Spawn at position - ONLY at the spring's position
                            WaterSpringLogger.LogDebug($"SpawnFlowingWater: Attempting to spawn water at {position}");
                            
                            // Check if something might be blocking the spawn
                            foreach (Thing t in position.GetThingList(Map))
                            {
                                if (t.def.passability == Traversability.Impassable)
                                {
                                    WaterSpringLogger.LogWarning($"SpawnFlowingWater: Impassable thing at position: {t.def.defName}");
                                }
                            }
                            
                            GenSpawn.Spawn(flowingWater, position, Map);
                            WaterSpringLogger.LogDebug($"SpawnFlowingWater: Successfully spawned new water at {position} with volume {typedWater.Volume}");
                        }
                        catch (Exception ex)
                        {
                            WaterSpringLogger.LogWarning($"SpawnFlowingWater: Exception while spawning water: {ex.Message}\n{ex.StackTrace}");
                        }
                    }
                    else
                    {
                        WaterSpringLogger.LogWarning($"SpawnFlowingWater: Created thing is not a FlowingWater instance! Actual type: {flowingWater.GetType().FullName}");
                    }
                }
                else
                {
                    WaterSpringLogger.LogWarning($"SpawnFlowingWater: Failed to make FlowingWater thing!");
                }
            }
            WaterSpringLogger.LogDebug($"SpawnFlowingWater: Completed water spawn process");
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksUntilNextWaterSpawn, "ticksUntilNextWaterSpawn", 60);
            WaterSpringLogger.LogDebug($"Building_WaterSpring.ExposeData: Spring data saved/loaded, ticksUntilNextWaterSpawn: {ticksUntilNextWaterSpawn}");
        }
    }
}