using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using WaterSpringMod.WaterSpring;

namespace WaterSpringMod
{
    public class WaterSpringModMain : Mod
    {
        // Make settings publicly accessible
        public WaterSpringModSettings settings;
        private WaterDiffusionManager diffusionManager;
    private Vector2 scrollPosition = Vector2.zero;
    private const float FixedScrollHeight = 4000f; // large fixed scrollable height for all tabs
    private int lastSelectedTab = -1;
        
        // Tab system for settings
        private int selectedTab = 0;
        
        // Static reference for easy access
        public static WaterSpringModMain Instance { get; private set; }

        public WaterSpringModMain(ModContentPack content) : base(content)
        {
            Instance = this; // Set the static reference
            settings = GetSettings<WaterSpringModSettings>();
            diffusionManager = new WaterDiffusionManager();
        }

        public override string SettingsCategory()
        {
            return "WaterSpringMod.SettingsCategory".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // Create a tabbed interface
            Rect tabRect = new Rect(inRect.x, inRect.y, inRect.width, 25f);
            List<TabRecord> tabs = new List<TabRecord>();
            
            // Create tabs
            tabs.Add(new TabRecord("General", () => selectedTab = 0, selectedTab == 0));
            tabs.Add(new TabRecord("Strategy 1", () => selectedTab = 1, selectedTab == 1));
            tabs.Add(new TabRecord("Strategy 3", () => selectedTab = 2, selectedTab == 2));
            tabs.Add(new TabRecord("Strategy 5", () => selectedTab = 3, selectedTab == 3));
            tabs.Add(new TabRecord("Debug", () => selectedTab = 4, selectedTab == 4));
            
            TabDrawer.DrawTabs(tabRect, tabs);
            
            // Create scrollable view for the selected tab content
            Rect contentRect = new Rect(inRect.x, inRect.y + 30f, inRect.width, inRect.height - 30f);

            // Reset scroll on tab change
            if (lastSelectedTab != selectedTab)
            {
                scrollPosition = Vector2.zero;
                lastSelectedTab = selectedTab;
            }
            
            // Create a listing standard for the tab content (fixed large height, regardless of content)
            Rect viewRect = new Rect(0f, 0f, contentRect.width - 16f, FixedScrollHeight);
            
            Widgets.BeginScrollView(contentRect, ref scrollPosition, viewRect);
            
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(viewRect);
            
            // Clamp selectedTab to available range in case of legacy saved index
            if (selectedTab < 0 || selectedTab > 4) selectedTab = 0;

            switch (selectedTab)
            {
                case 0: // General Settings
                    // General settings header
                    listingStandard.Label("<color=yellow>General Settings</color>");
                    listingStandard.Gap();
                    
                    // Spring spawn interval
                    listingStandard.Label(
                        "WaterSpringMod.SpawnIntervalLabel".Translate(settings.waterSpringSpawnInterval),
                        tooltip: "Interval between spring spawns (ticks). Lower = faster water production; higher = slower.");
                    settings.waterSpringSpawnInterval = (int)listingStandard.Slider(settings.waterSpringSpawnInterval, 10, 1000);
                    
                    // Debug mode
                    listingStandard.Gap();
                    listingStandard.CheckboxLabeled("Debug Mode (Enable Logging)", ref settings.debugModeEnabled, 
                        "Enable detailed logging for water springs. Warning: This may impact performance and create large log files.");
                    break;
                
                case 1: // Strategy 1: Active Tile Management
                    // Strategy 1 header
                    listingStandard.Label("<color=yellow>Strategy 1: Active Tile Management</color>");
                    listingStandard.Gap();
                    
                    // Active tile system checkbox
                    listingStandard.CheckboxLabeled("Use Active Tile System", ref settings.useActiveTileSystem, 
                        "Enables the active tile optimization system to improve performance. Only actively changing water tiles are processed.");
                    
                    // Stability cap (only rule): tile becomes stable when reaching this many no-change attempts
                    listingStandard.Gap();
                    listingStandard.Label($"Stability Cap: {settings.stabilityCap} no-change attempts",
                        tooltip: $"A tile is considered stable ONLY when its stability counter reaches this cap (currently {settings.stabilityCap}).\\nStability increases by 1 each time the tile is processed and no diffusion occurs.");
                    settings.stabilityCap = (int)listingStandard.Slider(settings.stabilityCap, 1, 1000);
                    listingStandard.Gap(4f);
                    listingStandard.Label("Counts only when a tile attempts diffusion and nothing changes. Raise to require more cycles; lower to stabilize sooner.");
                    listingStandard.Label("How many ticks a water tile must remain unchanged before it's considered stable and removed from active processing.");
                    
                    // Max processed tiles
                    listingStandard.Gap();
                    listingStandard.Label($"Max Processed Tiles Per Tick: {settings.maxProcessedTilesPerTick}",
                        tooltip: "Upper bound on how many active water tiles are processed per game tick. Lower improves performance but slows diffusion.");
                    settings.maxProcessedTilesPerTick = (int)listingStandard.Slider(settings.maxProcessedTilesPerTick, 50, 2000);
                    listingStandard.Gap(4f);
                    listingStandard.Label("Maximum number of water tiles processed each tick. Lower values improve performance but slow down water diffusion.");

                    // Local diffusion timing
                    listingStandard.Gap();
                    listingStandard.Label($"Local Check Interval Min: {settings.localCheckIntervalMin} ticks",
                        tooltip: "Minimum random wait between a tile’s diffusion attempts in normal mode. Lower = more frequent attempts.");
                    settings.localCheckIntervalMin = (int)listingStandard.Slider(settings.localCheckIntervalMin, 1, Mathf.Min(600, settings.localCheckIntervalMax));
                    listingStandard.Label($"Local Check Interval Max: {settings.localCheckIntervalMax} ticks",
                        tooltip: "Maximum random wait between a tile’s diffusion attempts in normal mode. Higher = more spread-out activity.");
                    settings.localCheckIntervalMax = (int)listingStandard.Slider(settings.localCheckIntervalMax, settings.localCheckIntervalMin, 1200);
                    listingStandard.Gap(4f);
                    listingStandard.Label("The random wait applied to individual tiles between diffusion attempts in the normal path.");

                    // Anti-backflow controls
                    listingStandard.Gap();
                    listingStandard.CheckboxLabeled("Anti-backflow (reduce ping-pong)", ref settings.antiBackflowEnabled,
                        "Discourage immediate backflow into the cell that just sent water. Adds a temporary extra diff requirement.");
                    if (settings.antiBackflowEnabled)
                    {
                        listingStandard.Label($"Backflow Cooldown: {settings.backflowCooldownTicks} ticks",
                            tooltip: "Window after outbound flow during which backflow is discouraged.");
                        settings.backflowCooldownTicks = (int)listingStandard.Slider(settings.backflowCooldownTicks, 0, 600);
                        listingStandard.Label($"Backflow Min-Diff Bonus: +{settings.backflowMinDiffBonus}",
                            tooltip: "Extra volume difference required to allow backflow during cooldown. 0 disables the penalty.");
                        settings.backflowMinDiffBonus = (int)listingStandard.Slider(settings.backflowMinDiffBonus, 0, 3);
                    }

                    // Diffusion rules (Normal method)
                    listingStandard.Gap();
                    listingStandard.Label("<color=yellow>Diffusion Rules</color>");
                    listingStandard.Label($"Minimum Volume Difference for Transfer: {settings.minVolumeDifferenceForTransfer}",
                        tooltip: "Minimum volume difference required for water to flow between tiles in the normal path. Prevents equal-volume oscillation. Lower spreads more; higher stabilizes faster.");
                    settings.minVolumeDifferenceForTransfer = (int)listingStandard.Slider(settings.minVolumeDifferenceForTransfer, 1, 3);
                    listingStandard.Label("The minimum volume difference required for water to flow between tiles. Lower values allow more natural spreading, higher values improve stability and performance.");

                    // Spring behavior (Normal method)
                    listingStandard.Gap(10f);
                    listingStandard.Label("<color=yellow>Spring Behavior</color>");
                    listingStandard.CheckboxLabeled("Spring uses backlog when full", ref settings.springUseBacklog,
                        "When the spring tile is full, store produced water in a small backlog and drip it out when capacity appears.");
                    if (settings.springUseBacklog)
                    {
                        listingStandard.Label($"Backlog Cap: {settings.springBacklogCap} units",
                            tooltip: "Maximum water units the spring can store when the source tile is full.");
                        settings.springBacklogCap = (int)listingStandard.Slider(settings.springBacklogCap, 0, 20);
                        listingStandard.Label($"Backlog Inject Interval: every {settings.springBacklogInjectInterval} ticks",
                            tooltip: "How often one backlog unit is injected when there is capacity.");
                        settings.springBacklogInjectInterval = (int)listingStandard.Slider(settings.springBacklogInjectInterval, 1, 300);
                    }
                    listingStandard.CheckboxLabeled("Prioritize spring tiles", ref settings.springPrioritizeTiles,
                        "Process spring tiles more aggressively so produced water moves out quickly.");
                    listingStandard.CheckboxLabeled("Spring tiles never stabilize", ref settings.springNeverStabilize,
                        "Spring source tiles are never removed from active processing based on stability.");
                    break;
                
                case 2: // Strategy 3: Chunk-based Processing
                    // Strategy 3 header
                    listingStandard.Label("<color=yellow>Strategy 3: Chunk-based Processing</color>");
                    listingStandard.Gap();
                    
                    // Chunk processing checkbox
                    listingStandard.CheckboxLabeled("Use Chunk-based Processing", ref settings.useChunkBasedProcessing, 
                        "Organizes water tiles into spatial chunks for more efficient processing. Experimental feature.");
                    
                    if (settings.useChunkBasedProcessing)
                    {
                        // Chunk size
                        listingStandard.Gap();
                        listingStandard.Label($"Chunk Size: {settings.chunkSize}x{settings.chunkSize} tiles",
                            tooltip: "Tile width/height of spatial chunks used for batching. Smaller = more chunks; larger = fewer, larger chunks.");
                        settings.chunkSize = (int)listingStandard.Slider(settings.chunkSize, 4, 16);
                        
                        // Max processed chunks
                        listingStandard.Gap();
                        listingStandard.Label($"Max Processed Chunks Per Tick: {settings.maxProcessedChunksPerTick}",
                            tooltip: "Upper bound on how many chunks are processed each tick. Lower improves performance but slows diffusion.");
                        settings.maxProcessedChunksPerTick = (int)listingStandard.Slider(settings.maxProcessedChunksPerTick, 5, 100);
                        
                        // Max processed tiles per chunk
                        listingStandard.Gap();
                        listingStandard.Label($"Max Processed Tiles Per Chunk: {settings.maxProcessedTilesPerChunk}",
                            tooltip: "Cap on water tiles processed per chunk each tick. Lower improves performance but slows diffusion.");
                        settings.maxProcessedTilesPerChunk = (int)listingStandard.Slider(settings.maxProcessedTilesPerChunk, 10, 200);
                        
                        // Checkerboard pattern
                        listingStandard.Gap();
                        listingStandard.CheckboxLabeled("Use Checkerboard Update Pattern", ref settings.useCheckerboardPattern, 
                            "Alternate processing of chunks to improve performance. Odd chunks process on odd ticks, even chunks on even ticks.");
                    }
                    break;
                
                case 3: // Strategy 5: Update Frequency Optimization
                    // Strategy 5 header
                    listingStandard.Label("<color=yellow>Strategy 5: Update Frequency Optimization</color>");
                    listingStandard.Gap();
                    
                    // Frequency-based processing checkbox
                    listingStandard.CheckboxLabeled("Use Frequency-based Processing", ref settings.useFrequencyBasedProcessing, 
                        "Controls how often water processing occurs. Experimental feature.");
                    
                    if (settings.useFrequencyBasedProcessing)
                    {
                        // Global update frequency
                        listingStandard.Gap();
                        listingStandard.Label($"Global Update Frequency: every {settings.globalUpdateFrequency} tick(s)",
                            tooltip: "Run water processing only every N ticks (global gate). Larger values reduce CPU use and slow diffusion.");
                        settings.globalUpdateFrequency = (int)listingStandard.Slider(settings.globalUpdateFrequency, 1, 20);
                        
                        // Adaptive TPS throttling
                        listingStandard.Gap();
                        listingStandard.CheckboxLabeled("Use Adaptive TPS Throttling", ref settings.useAdaptiveTPS, 
                            "Automatically reduces water processing when game performance drops.");
                        
                        if (settings.useAdaptiveTPS)
                        {
                            listingStandard.Gap();
                            listingStandard.Label($"Minimum Target TPS: {settings.minTPS}",
                                tooltip: "Adaptive throttle target ticks-per-second. When TPS falls below this, processing is delayed more.");
                            settings.minTPS = (float)Math.Round(listingStandard.Slider(settings.minTPS, 10f, 1000f), 1);
                        }
                    }
                    break;
                
                case 4: // Debug & Developer Tools
                    // Debug header
                    listingStandard.Label("<color=yellow>Debug & Visualization</color>");
                    listingStandard.Gap();
                    
                    // Performance stats
                    listingStandard.CheckboxLabeled("Show Performance Stats", ref settings.showPerformanceStats, 
                        "Shows water system performance statistics on screen.");
                    
                    // Debug visualization
                    listingStandard.Gap();
                    listingStandard.CheckboxLabeled("Show Detailed Debug Visualization", ref settings.showDetailedDebug, 
                        "Shows additional debug information like stable water tiles. Enable with Alt+W in developer mode.");
                    
                    // Developer tools
                    if (Verse.Prefs.DevMode)
                    {
                        listingStandard.Gap();
                        listingStandard.GapLine();
                        listingStandard.Label("<color=orange>DEVELOPER TOOLS</color>");
                        
                        if (listingStandard.ButtonText("Remove All Water"))
                        {
                            if (Find.CurrentMap != null)
                            {
                                diffusionManager.RemoveAllWaterFromMap(Find.CurrentMap);
                                Messages.Message("Removed all water from the current map", MessageTypeDefOf.NeutralEvent);
                            }
                            else
                            {
                                Messages.Message("No map loaded", MessageTypeDefOf.RejectInput);
                            }
                        }
                    }
                    break;
            }
            
            // Finish drawing
            listingStandard.End();
            Widgets.EndScrollView();
            base.DoSettingsWindowContents(inRect);
        }
        
        public WaterSpringModSettings GetModSettings()
        {
            return settings;
        }
        
        public WaterDiffusionManager GetDiffusionManager()
        {
            return diffusionManager;
        }
        
    // Diffusion method switching removed
    }
    
    public class WaterDiffusionGameComponent : GameComponent
    {
        private WaterSpringModMain mod;
        
        // Static constructor for automatic loading
        static WaterDiffusionGameComponent()
        {
            // No logging
        }
        
        public WaterDiffusionGameComponent(Game game) : base()
        {
            // Constructor for loading a saved game
        }
        
        public WaterDiffusionGameComponent(WaterSpringModMain mod) : base()
        {
            this.mod = mod;
        }
        
        public override void FinalizeInit()
        {
            base.FinalizeInit();
            
            // If this was loaded from a save, get the mod instance
            if (mod == null)
            {
                mod = LoadedModManager.GetMod<WaterSpringModMain>();
            }
        }
        
        public override void GameComponentTick()
        {
            base.GameComponentTick();
            // No diffusion logic - handled by individual FlowingWater tiles
        }
        
        public override void ExposeData()
        {
            base.ExposeData();
            // No data to save/load
        }
    }
}
