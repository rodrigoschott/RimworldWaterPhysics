using UnityEngine;
using Verse;

namespace WaterSpringMod
{
    public class WaterSpringModSettings : ModSettings
    {
        // Spring produces water every X ticks (60 ticks = 1 second of real time)
        public int waterSpringSpawnInterval = 120; // Default: 2 seconds
        
        // Debug mode for water spring logging
        public bool debugModeEnabled = false; // Default: disabled
        
    // Performance optimization settings
        public bool useActiveTileSystem = true; // Default: enabled
    // Stability rule: a tile is stable ONLY when it reaches the stability cap (no-change attempts)
    public int stabilityCap = 100; // Default: 100 no-change attempts required to be stable
        public int maxProcessedTilesPerTick = 500; // Default: 500 tiles max per tick
        public bool showPerformanceStats = false; // Default: disabled
        public bool showDetailedDebug = false; // Default: disabled - shows stable water tiles in debug view
        

    // Chunk-based processing settings
        public bool useChunkBasedProcessing = false; // Default: disabled until Strategy 3 is fully implemented
        public int chunkSize = 8; // Default: 8x8 chunk size
        public int maxProcessedChunksPerTick = 20; // Default: process up to 20 chunks per tick
        public int maxProcessedTilesPerChunk = 50; // Default: process up to 50 tiles per chunk
        public bool useCheckerboardPattern = true; // Default: enabled - alternates processing between chunks

        public int minVolumeDifferenceForTransfer = 2; // Default: 2 unit minimum difference to transfer water (equilibrium: diff/2)
        
    // Update frequency settings
        public bool useFrequencyBasedProcessing = false; // Default: disabled until Strategy 5 is fully implemented
        public int globalUpdateFrequency = 1; // Default: process water every tick
        public bool useAdaptiveTPS = false; // Default: disabled - automatically adjusts processing based on game TPS
        public float minTPS = 15.0f; // Default: Minimum target TPS before throttling kicks in

    // Terrain sync: mirror terrain to water bands (1-4 shallow, 5-7 deep)
    public bool syncTerrainToWaterVolume = true; // Default: enabled

    // Evaporation settings
    public bool evaporationEnabled = true; // Default: enabled
    public int evaporationIntervalTicks = 300; // X: every X ticks
    public int evaporationMaxVolumeThreshold = 1; // Y: only if volume <= Y
    public int evaporationChancePercent = 10; // Z: unroofed chance percent per check
    public bool evaporationOnlyUnroofed = true; // If true, roofed tiles never evaporate
    public int evaporationChancePercentRoofed = 10; // Separate chance for roofed tiles when allowed

    // Local diffusion timing (normal path) – replaces hardcoded 30..60
    public int localCheckIntervalMin = 30; // Default min ticks between attempts
    public int localCheckIntervalMax = 60; // Default max ticks between attempts

    // Spring behavior controls
    public bool springUseBacklog = true; // Default: enable backlog when the spring tile is full
    public int springBacklogCap = 7; // Default: store up to 7 units when blocked
    public int springBacklogInjectInterval = 30; // Default: drip 1 backlog unit every 30 ticks when capacity exists
    public bool springPrioritizeTiles = true; // Default: process spring tiles every tick
    public bool springNeverStabilize = true; // Default: spring tiles never stabilize

    // Reactivation wave settings
    public int reactivationRadius = 8; // Default: wake tiles within 8 cells
    public int reactivationMaxTiles = 128; // Default: process up to 128 tiles immediately
    public bool reactivationImmediateTransfers = true; // Default: attempt a single immediate transfer on wake

    // Vertical portal: no settings required; uses WS_Hole and a fixed cache TTL

    // Multi-level integration (MultiFloors)
    public bool stairWaterFlowEnabled = true; // Default: enabled
    public bool upwardStairFlowEnabled = true; // Default: enabled
    public int minVolumeForUpwardFlow = 5; // Default: 5 (high pressure required)
    public bool elevatorWaterFlowEnabled = false; // Default: disabled (opt-in feature)
    public bool elevatorRequiresPower = true; // Default: enabled
    public bool useMultiFloorsVoidTerrain = true; // Default: enabled
    public int maxVerticalPropagationDepth = 3; // Default: 3 levels

    // Pressure propagation (DF-inspired)
    public bool pressurePropagationEnabled = true;
    public int pressureMaxSearchDepth = 256;       // Max BFS tiles to explore
    public int pressureCooldownTicks = 0;            // Min ticks between pressure events per tile (0 = every tick)

    // Gravity splash distribution
    public int splashMaxOutlets = 12;   // Max candidate tiles per splash
    public int splashMaxDepth = 32;     // Max BFS tiles explored per splash

    // Periodic equalization (entropy fix for staircase gradients)
    public bool equalizationEnabled = true;
    public int equalizationIntervalTicks = 60;       // Run every ~1 second
    public int equalizationMaxRegionSize = 4096;     // Safety cap on BFS

    // Vanilla water sink (#001)
    public bool vanillaWaterSinkEnabled = true;      // Vanilla water cells absorb FlowingWater
    public int vanillaWaterAbsorptionRate = 1;        // Units absorbed per tick (1-7)
    public bool vanillaWaterPreventSpawn = true;      // Never spawn FlowingWater on vanilla water

    // Channel flow restriction (#002)
    public bool channelFlowRestrictionEnabled = true; // Channels restrict flow to their axis

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref waterSpringSpawnInterval, "waterSpringSpawnInterval", 120);
            Scribe_Values.Look(ref debugModeEnabled, "debugModeEnabled", false);
            
            // Save/load performance settings
            Scribe_Values.Look(ref useActiveTileSystem, "useActiveTileSystem", true);
            Scribe_Values.Look(ref stabilityCap, "stabilityCap", 100);
            Scribe_Values.Look(ref maxProcessedTilesPerTick, "maxProcessedTilesPerTick", 500);
            Scribe_Values.Look(ref showPerformanceStats, "showPerformanceStats", false);
            Scribe_Values.Look(ref showDetailedDebug, "showDetailedDebug", false);
            
            // Save/load chunk-based processing settings
            Scribe_Values.Look(ref useChunkBasedProcessing, "useChunkBasedProcessing", false);
            Scribe_Values.Look(ref chunkSize, "chunkSize", 8);
            Scribe_Values.Look(ref maxProcessedChunksPerTick, "maxProcessedChunksPerTick", 20);
            Scribe_Values.Look(ref maxProcessedTilesPerChunk, "maxProcessedTilesPerChunk", 50);
            Scribe_Values.Look(ref useCheckerboardPattern, "useCheckerboardPattern", true);
            
            Scribe_Values.Look(ref minVolumeDifferenceForTransfer, "minVolumeDifferenceForTransfer", 1);
            
            // Save/load update frequency settings
            Scribe_Values.Look(ref useFrequencyBasedProcessing, "useFrequencyBasedProcessing", false);
            Scribe_Values.Look(ref globalUpdateFrequency, "globalUpdateFrequency", 1);
            Scribe_Values.Look(ref useAdaptiveTPS, "useAdaptiveTPS", false);
            Scribe_Values.Look(ref minTPS, "minTPS", 15.0f);

            // Save/load terrain sync setting
            Scribe_Values.Look(ref syncTerrainToWaterVolume, "syncTerrainToWaterVolume", true);

            // Save/load evaporation settings
            Scribe_Values.Look(ref evaporationEnabled, "evaporationEnabled", true);
            Scribe_Values.Look(ref evaporationIntervalTicks, "evaporationIntervalTicks", 300);
            Scribe_Values.Look(ref evaporationMaxVolumeThreshold, "evaporationMaxVolumeThreshold", 1);
            Scribe_Values.Look(ref evaporationChancePercent, "evaporationChancePercent", 10);
            Scribe_Values.Look(ref evaporationOnlyUnroofed, "evaporationOnlyUnroofed", true);
            Scribe_Values.Look(ref evaporationChancePercentRoofed, "evaporationChancePercentRoofed", 10);

            // Save/load local diffusion timing
            Scribe_Values.Look(ref localCheckIntervalMin, "localCheckIntervalMin", 30);
            Scribe_Values.Look(ref localCheckIntervalMax, "localCheckIntervalMax", 60);

            // Save/load spring behavior settings
            Scribe_Values.Look(ref springUseBacklog, "springUseBacklog", true);
            Scribe_Values.Look(ref springBacklogCap, "springBacklogCap", 7);
            Scribe_Values.Look(ref springBacklogInjectInterval, "springBacklogInjectInterval", 30);
            Scribe_Values.Look(ref springPrioritizeTiles, "springPrioritizeTiles", true);
            Scribe_Values.Look(ref springNeverStabilize, "springNeverStabilize", true);
            
            // Save/load reactivation settings
            Scribe_Values.Look(ref reactivationRadius, "reactivationRadius", 8);
            Scribe_Values.Look(ref reactivationMaxTiles, "reactivationMaxTiles", 128);
            Scribe_Values.Look(ref reactivationImmediateTransfers, "reactivationImmediateTransfers", true);
            // No per-setting fields for vertical portal bridge
            
            // Save/load multi-level integration settings
            Scribe_Values.Look(ref stairWaterFlowEnabled, "stairWaterFlowEnabled", true);
            Scribe_Values.Look(ref upwardStairFlowEnabled, "upwardStairFlowEnabled", true);
            Scribe_Values.Look(ref minVolumeForUpwardFlow, "minVolumeForUpwardFlow", 5);
            Scribe_Values.Look(ref elevatorWaterFlowEnabled, "elevatorWaterFlowEnabled", false);
            Scribe_Values.Look(ref elevatorRequiresPower, "elevatorRequiresPower", true);
            Scribe_Values.Look(ref useMultiFloorsVoidTerrain, "useMultiFloorsVoidTerrain", true);
            Scribe_Values.Look(ref maxVerticalPropagationDepth, "maxVerticalPropagationDepth", 3);
            
            // Save/load pressure settings
            Scribe_Values.Look(ref pressurePropagationEnabled, "pressurePropagationEnabled", true);
            Scribe_Values.Look(ref pressureMaxSearchDepth, "pressureMaxSearchDepth", 256);
            Scribe_Values.Look(ref pressureCooldownTicks, "pressureCooldownTicks", 0);

            // Save/load gravity splash settings
            Scribe_Values.Look(ref splashMaxOutlets, "splashMaxOutlets", 12);
            Scribe_Values.Look(ref splashMaxDepth, "splashMaxDepth", 16);

            // Save/load equalization settings
            Scribe_Values.Look(ref equalizationEnabled, "equalizationEnabled", true);
            Scribe_Values.Look(ref equalizationIntervalTicks, "equalizationIntervalTicks", 60);
            Scribe_Values.Look(ref equalizationMaxRegionSize, "equalizationMaxRegionSize", 4096);

            // Save/load vanilla water sink settings
            Scribe_Values.Look(ref vanillaWaterSinkEnabled, "vanillaWaterSinkEnabled", true);
            Scribe_Values.Look(ref vanillaWaterAbsorptionRate, "vanillaWaterAbsorptionRate", 1);
            Scribe_Values.Look(ref vanillaWaterPreventSpawn, "vanillaWaterPreventSpawn", true);

            // Save/load channel settings
            Scribe_Values.Look(ref channelFlowRestrictionEnabled, "channelFlowRestrictionEnabled", true);

            // Migration: bump old default (32) to new default (256)
            if (Scribe.mode == LoadSaveMode.LoadingVars && pressureMaxSearchDepth <= 32)
                pressureMaxSearchDepth = 256;

            // Migration: old default cooldown was 10, new is 0
            if (Scribe.mode == LoadSaveMode.LoadingVars && pressureCooldownTicks >= 10)
                pressureCooldownTicks = 0;

            // Sanitize values after loading/applying defaults
            ClampAndSanitize();
        }

        // Enforce safe bounds and invariants to prevent pathological settings
        private void ClampAndSanitize()
        {
            // Spawn interval: [1, 6000] (~100s)
            waterSpringSpawnInterval = Mathf.Clamp(waterSpringSpawnInterval, 1, 6000);

            // Stability
            stabilityCap = Mathf.Clamp(stabilityCap, 1, WaterSpringMod.WaterSpring.FlowingWater.MaxStability);

            // Local diffusion timing
            localCheckIntervalMin = Mathf.Clamp(localCheckIntervalMin, 1, 600);
            localCheckIntervalMax = Mathf.Clamp(localCheckIntervalMax, localCheckIntervalMin, 1200);

            // Transfer threshold
            minVolumeDifferenceForTransfer = Mathf.Clamp(minVolumeDifferenceForTransfer, 2, WaterSpringMod.WaterSpring.FlowingWater.MaxVolume);

            // Chunk processing
            chunkSize = Mathf.Clamp(chunkSize, 4, 64);
            maxProcessedChunksPerTick = Mathf.Clamp(maxProcessedChunksPerTick, 1, 2500);
            maxProcessedTilesPerChunk = Mathf.Clamp(maxProcessedTilesPerChunk, 1, 1000);

            // Global frequency / TPS
            globalUpdateFrequency = Mathf.Clamp(globalUpdateFrequency, 1, 600);
            minTPS = Mathf.Clamp(minTPS, 1.0f, 60.0f);

            // Spring behavior
            springBacklogCap = Mathf.Clamp(springBacklogCap, 0, WaterSpringMod.WaterSpring.FlowingWater.MaxVolume);
            springBacklogInjectInterval = Mathf.Clamp(springBacklogInjectInterval, 1, 600);

            // Reactivation wave
            reactivationRadius = Mathf.Clamp(reactivationRadius, 1, 64);
            reactivationMaxTiles = Mathf.Clamp(reactivationMaxTiles, 1, 10000);

            // Evaporation
            evaporationIntervalTicks = Mathf.Clamp(evaporationIntervalTicks, 60, 6000);
            evaporationMaxVolumeThreshold = Mathf.Clamp(evaporationMaxVolumeThreshold, 0, WaterSpringMod.WaterSpring.FlowingWater.MaxVolume);
            evaporationChancePercent = Mathf.Clamp(evaporationChancePercent, 0, 100);
            evaporationChancePercentRoofed = Mathf.Clamp(evaporationChancePercentRoofed, 0, 100);

            // No clamps needed for vertical portal bridge
            
            // Multi-level integration
            minVolumeForUpwardFlow = Mathf.Clamp(minVolumeForUpwardFlow, 1, WaterSpringMod.WaterSpring.FlowingWater.MaxVolume);
            maxVerticalPropagationDepth = Mathf.Clamp(maxVerticalPropagationDepth, 1, 10);

            // Pressure propagation
            pressureMaxSearchDepth = Mathf.Clamp(pressureMaxSearchDepth, 4, 4096);
            pressureCooldownTicks = Mathf.Clamp(pressureCooldownTicks, 0, 600);

            // Gravity splash distribution
            splashMaxOutlets = Mathf.Clamp(splashMaxOutlets, 1, 32);
            splashMaxDepth = Mathf.Clamp(splashMaxDepth, 4, 128);

            // Equalization
            equalizationIntervalTicks = Mathf.Clamp(equalizationIntervalTicks, 10, 600);
            equalizationMaxRegionSize = Mathf.Clamp(equalizationMaxRegionSize, 16, 16384);

            // Vanilla water sink
            vanillaWaterAbsorptionRate = Mathf.Clamp(vanillaWaterAbsorptionRate, 1, WaterSpringMod.WaterSpring.FlowingWater.MaxVolume);
        }
    }
}