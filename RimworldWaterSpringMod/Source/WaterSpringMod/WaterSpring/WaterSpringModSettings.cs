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

        public int minVolumeDifferenceForTransfer = 1; // Default: 1 unit minimum difference to transfer water
        
    // Update frequency settings
        public bool useFrequencyBasedProcessing = false; // Default: disabled until Strategy 5 is fully implemented
        public int globalUpdateFrequency = 1; // Default: process water every tick
        public bool useAdaptiveTPS = false; // Default: disabled - automatically adjusts processing based on game TPS
        public float minTPS = 15.0f; // Default: Minimum target TPS before throttling kicks in

    // Local diffusion timing (normal path) – replaces hardcoded 30..60
    public int localCheckIntervalMin = 30; // Default min ticks between attempts
    public int localCheckIntervalMax = 60; // Default max ticks between attempts

    // Anti-backflow controls (Normal diffusion)
    public bool antiBackflowEnabled = true; // Default: enable anti-backflow heuristics
    public int backflowCooldownTicks = 120; // Default: 2s cooldown window where backflow is discouraged
    public int backflowMinDiffBonus = 1; // Default: require +1 extra volume difference to flow back during cooldown

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

            // Save/load local diffusion timing
            Scribe_Values.Look(ref localCheckIntervalMin, "localCheckIntervalMin", 30);
            Scribe_Values.Look(ref localCheckIntervalMax, "localCheckIntervalMax", 60);

            // Save/load anti-backflow settings
            Scribe_Values.Look(ref antiBackflowEnabled, "antiBackflowEnabled", true);
            Scribe_Values.Look(ref backflowCooldownTicks, "backflowCooldownTicks", 120);
            Scribe_Values.Look(ref backflowMinDiffBonus, "backflowMinDiffBonus", 1);

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

            // Anti-backflow
            backflowCooldownTicks = Mathf.Clamp(backflowCooldownTicks, 0, 3600);
            backflowMinDiffBonus = Mathf.Clamp(backflowMinDiffBonus, 0, 3);

            // Transfer threshold
            minVolumeDifferenceForTransfer = Mathf.Clamp(minVolumeDifferenceForTransfer, 1, WaterSpringMod.WaterSpring.FlowingWater.MaxVolume);

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
        }
    }
}