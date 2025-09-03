using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using UnityEngine;

namespace WaterSpringMod.WaterSpring
{
    public class Building_WaterSpring : Building
    {
        private int ticksUntilNextWaterSpawn = 60; // Start with a short timer
    private int backlog = 0; // units of water produced but not yet injected
    private int ticksUntilBacklogInject = 0;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            
            if (WaterSpringLogger.DebugEnabled)
            {
                WaterSpringLogger.LogDebug("Building_WaterSpring.SpawnSetup: Spring at " + Position + " being setup. Respawning: " + respawningAfterLoad);
                WaterSpringLogger.LogDebug("Building_WaterSpring.SpawnSetup: ThingID: " + this.ThingID + ", Def: " + this.def.defName + ", TickerType: " + this.def.tickerType);
            }
            
            if (this.def.tickerType == TickerType.Never)
            {
                WaterSpringLogger.LogWarning($"Building_WaterSpring.SpawnSetup: WARNING - TickerType is set to Never! This will prevent Tick from being called.");
            }
            
            // Register for ticking if needed
            if (Find.TickManager != null && WaterSpringLogger.DebugEnabled)
            {
                WaterSpringLogger.LogDebug("Building_WaterSpring.SpawnSetup: TickManager exists, making sure we're registered");
            }
            
            if (Spawned)
            {
                // Force a short initial water spawn time
                ticksUntilNextWaterSpawn = 60;
                if (WaterSpringLogger.DebugEnabled)
                {
                    WaterSpringLogger.LogDebug("Building_WaterSpring.SpawnSetup: Spring is spawned, set initial spawn timer to " + ticksUntilNextWaterSpawn + " ticks");
                }
            }
            
            // Try to spawn water immediately for testing
            if (WaterSpringLogger.DebugEnabled) WaterSpringLogger.LogDebug("Building_WaterSpring.SpawnSetup: Attempting initial water spawn for testing...");
            SpawnFlowingWater();
        }
        
        // Let's make absolutely sure Tick is getting called
        private int tickCounter = 0;
        private bool firstTickLogged = false;
        
        protected override void Tick()
        {
            base.Tick();
            
            // Add an immediate log for the first tick to verify the method is being called
            if (!firstTickLogged && WaterSpringLogger.DebugEnabled)
            {
                WaterSpringLogger.LogDebug("Building_WaterSpring.Tick: FIRST TICK DETECTED for spring at " + Position);
                firstTickLogged = true;
            }
            
            // Count total ticks for diagnostic purposes
            tickCounter++;
            
            if (WaterSpringLogger.DebugEnabled && (tickCounter % 600 == 0))
            {
                WaterSpringLogger.LogDebug("Building_WaterSpring.Tick: Spring at " + Position + " has been ticked " + tickCounter + " times");
            }
            
            if (!Spawned)
            {
                WaterSpringLogger.LogWarning($"Building_WaterSpring.Tick: Spring at {Position} is not spawned!");
                return;
            }
            
            ticksUntilNextWaterSpawn--;
            
            // Log more frequently to catch the issue
            if (WaterSpringLogger.DebugEnabled && (ticksUntilNextWaterSpawn % 60 == 0 || ticksUntilNextWaterSpawn < 5))
            {
                WaterSpringLogger.LogDebug("Building_WaterSpring.Tick: Spring at " + Position + " has " + ticksUntilNextWaterSpawn + " ticks until next spawn");
            }
            
            if (ticksUntilNextWaterSpawn <= 0)
            {
                if (WaterSpringLogger.DebugEnabled)
                {
                    WaterSpringLogger.LogDebug("Building_WaterSpring.Tick: Spring at " + Position + " triggering water spawn!");
                }
                SpawnFlowingWater();
                
                // Get settings from the mod
                WaterSpringModSettings settings = LoadedModManager.GetMod<WaterSpringModMain>()?.GetSettings<WaterSpringModSettings>();
                int newInterval = settings != null ? settings.waterSpringSpawnInterval : 200;
                ticksUntilNextWaterSpawn = newInterval;
                if (WaterSpringLogger.DebugEnabled)
                {
                    WaterSpringLogger.LogDebug("Building_WaterSpring.Tick: Next spawn scheduled in " + newInterval + " ticks");
                }
            }

            // Try to inject backlog periodically
            var s2 = LoadedModManager.GetMod<WaterSpringModMain>()?.GetSettings<WaterSpringModSettings>();
            if (s2 != null && s2.springUseBacklog && backlog > 0)
            {
                if (ticksUntilBacklogInject > 0) ticksUntilBacklogInject--;
                if (ticksUntilBacklogInject <= 0)
                {
                    TryInjectBacklog();
                    ticksUntilBacklogInject = Math.Max(1, s2.springBacklogInjectInterval);
                }
            }
        }

        private void SpawnFlowingWater()
        {
            if (WaterSpringLogger.DebugEnabled) WaterSpringLogger.LogDebug("SpawnFlowingWater: Starting water spawn process at " + Position);
            
            if (Map == null)
            {
                WaterSpringLogger.LogWarning($"SpawnFlowingWater: Map is null, cannot spawn water!");
                return;
            }
            
            IntVec3 position = this.Position;
            if (WaterSpringLogger.DebugEnabled) WaterSpringLogger.LogDebug("SpawnFlowingWater: Target position: " + position);
            
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
            
            if (WaterSpringLogger.DebugEnabled) WaterSpringLogger.LogDebug("SpawnFlowingWater: Position validation passed");
            
            // Check for existing water
            bool waterExists = false;
            FlowingWater existingWater = null;
            
            if (WaterSpringLogger.DebugEnabled) WaterSpringLogger.LogDebug("SpawnFlowingWater: Checking for existing water at " + position);
            // Prefer direct typed lookup when available
            existingWater = position.GetThingList(Map) != null ? position.GetThingList(Map).Find(t => t is FlowingWater) as FlowingWater : null;
            if (existingWater != null)
            {
                waterExists = true;
                if (WaterSpringLogger.DebugEnabled)
                {
                    WaterSpringLogger.LogDebug("SpawnFlowingWater: Found existing water with volume: " + existingWater.Volume);
                }
            }
            
            if (waterExists && existingWater != null)
            {
                // Add volume to existing water
                int oldVolume = existingWater.Volume;
                if (WaterSpringLogger.DebugEnabled)
                {
                    WaterSpringLogger.LogDebug("SpawnFlowingWater: Adding volume to existing water. Current volume: " + oldVolume);
                }
                var s = LoadedModManager.GetMod<WaterSpringModMain>()?.GetSettings<WaterSpringModSettings>();
                if (s != null && s.springUseBacklog && existingWater.Volume >= FlowingWater.MaxVolume)
                {
                    // Store backlog instead of losing water
                    if (backlog < Math.Max(0, s.springBacklogCap))
                    {
                        backlog++;
                        if (WaterSpringLogger.DebugEnabled)
                        {
                            WaterSpringLogger.LogDebug("SpawnFlowingWater: Spring tile full, added 1 to backlog. Backlog now " + backlog + "/" + s.springBacklogCap);
                        }
                    }
                    else
                    {
                        if (WaterSpringLogger.DebugEnabled)
                        {
                            WaterSpringLogger.LogDebug("SpawnFlowingWater: Backlog full (" + backlog + "), discarding produced water");
                        }
                    }
                }
                else
                {
                    existingWater.AddVolume(1);
                }
                if (WaterSpringLogger.DebugEnabled)
                {
                    WaterSpringLogger.LogDebug("SpawnFlowingWater: Added volume to existing water. Old volume: " + oldVolume + ", New volume: " + existingWater.Volume);
                }
            }
            else
            {
                // Create new water
                if (WaterSpringLogger.DebugEnabled) WaterSpringLogger.LogDebug("SpawnFlowingWater: No existing water found, creating new water");
                
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
                
                if (WaterSpringLogger.DebugEnabled) WaterSpringLogger.LogDebug("SpawnFlowingWater: Found FlowingWater ThingDef. Label: " + waterDef.label + ", Category: " + waterDef.category);
                Thing flowingWater = ThingMaker.MakeThing(waterDef);
                
                if (flowingWater != null)
                {
                    if (WaterSpringLogger.DebugEnabled) WaterSpringLogger.LogDebug("SpawnFlowingWater: Successfully created FlowingWater thing of type " + flowingWater.GetType().FullName);
                    
                    if (flowingWater is FlowingWater typedWater)
                    {
                        if (WaterSpringLogger.DebugEnabled) WaterSpringLogger.LogDebug("SpawnFlowingWater: Setting initial volume to 1");
                        typedWater.Volume = 1;
                        // mark as spring source and prevent stabilization if setting enabled
                        var s = LoadedModManager.GetMod<WaterSpringModMain>()?.GetSettings<WaterSpringModSettings>();
                        if (s != null)
                        {
                            typedWater.MarkAsSpringSource(s.springNeverStabilize);
                        }
                        
                        try
                        {
                            // Spawn at position - ONLY at the spring's position
                            if (WaterSpringLogger.DebugEnabled) WaterSpringLogger.LogDebug("SpawnFlowingWater: Attempting to spawn water at " + position);
                            
                            // Check if something might be blocking the spawn
                            var list = position.GetThingList(Map);
                            for (int i = 0; list != null && i < list.Count; i++)
                            {
                                Thing t = list[i];
                                if (t.def.passability == Traversability.Impassable)
                                {
                                    WaterSpringLogger.LogWarning("SpawnFlowingWater: Impassable thing at position: " + t.def.defName);
                                }
                            }
                            
                            GenSpawn.Spawn(flowingWater, position, Map);
                            if (WaterSpringLogger.DebugEnabled) WaterSpringLogger.LogDebug("SpawnFlowingWater: Successfully spawned new water at " + position + " with volume " + typedWater.Volume);
                        }
                        catch (Exception ex)
                        {
                            WaterSpringLogger.LogWarning("SpawnFlowingWater: Exception while spawning water: " + ex.Message + "\n" + ex.StackTrace);
                        }
                    }
                    else
                    {
                        WaterSpringLogger.LogWarning("SpawnFlowingWater: Created thing is not a FlowingWater instance! Actual type: " + flowingWater.GetType().FullName);
                    }
                }
                else
                {
                    WaterSpringLogger.LogWarning($"SpawnFlowingWater: Failed to make FlowingWater thing!");
                }
            }
            if (WaterSpringLogger.DebugEnabled) WaterSpringLogger.LogDebug("SpawnFlowingWater: Completed water spawn process");
        }

        private void TryInjectBacklog()
        {
            if (Map == null || backlog <= 0) return;
            var s = LoadedModManager.GetMod<WaterSpringModMain>()?.GetSettings<WaterSpringModSettings>();
            if (s == null || !s.springUseBacklog) return;

            // Try to find capacity to inject into spring tile or an adjacent water tile
            FlowingWater atSpring = null;
            var list = this.Position.GetThingList(Map);
            for (int i = 0; list != null && i < list.Count; i++)
            {
                if (list[i] is FlowingWater fw)
                {
                    atSpring = fw;
                    break;
                }
            }
            if (atSpring != null && atSpring.Volume < FlowingWater.MaxVolume)
            {
                atSpring.AddVolume(1);
                backlog--;
                WaterSpringLogger.LogDebug($"TryInjectBacklog: Injected 1 into spring tile. Backlog now {backlog}");
                return;
            }

            // Look for the lowest-volume adjacent water to inject into
            FlowingWater best = null;
            int bestVol = int.MaxValue;
            IntVec3 bestPos = IntVec3.Invalid;
            foreach (var dir in GenAdj.CardinalDirections)
            {
                IntVec3 p = Position + dir;
                if (!p.InBounds(Map) || !p.Walkable(Map)) continue;
                FlowingWater w = null;
                var nl = p.GetThingList(Map);
                for (int i = 0; nl != null && i < nl.Count; i++)
                {
                    if (nl[i] is FlowingWater fw)
                    {
                        w = fw;
                        break;
                    }
                }
                if (w != null && w.Volume < FlowingWater.MaxVolume && w.Volume < bestVol)
                {
                    best = w; bestVol = w.Volume; bestPos = p;
                }
            }
            if (best != null)
            {
                best.AddVolume(1);
                backlog--;
                WaterSpringLogger.LogDebug($"TryInjectBacklog: Injected 1 into neighbor {bestPos}. Backlog now {backlog}");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksUntilNextWaterSpawn, "ticksUntilNextWaterSpawn", 60);
            Scribe_Values.Look(ref backlog, "springBacklog", 0);
            Scribe_Values.Look(ref ticksUntilBacklogInject, "ticksUntilBacklogInject", 0);
            WaterSpringLogger.LogDebug($"Building_WaterSpring.ExposeData: Spring data saved/loaded, ticksUntilNextWaterSpawn: {ticksUntilNextWaterSpawn}");
        }
    }
}