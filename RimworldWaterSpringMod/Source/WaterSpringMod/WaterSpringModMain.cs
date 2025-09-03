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
        // UI caching helpers
        private static readonly GUIContent GC_GeneralHeader = new GUIContent("<color=yellow>General Settings</color>");
        private static readonly GUIContent GC_S1_Header = new GUIContent("<color=yellow>Strategy 1: Active Tile Management</color>");
        private static readonly GUIContent GC_S1_DiffusionRules = new GUIContent("<color=yellow>Diffusion Rules</color>");
        private static readonly GUIContent GC_S1_SpringBehavior = new GUIContent("<color=yellow>Spring Behavior</color>");
        private static readonly GUIContent GC_S1_Reactivation = new GUIContent("<color=yellow>Reactivation Wave</color>");
        private static readonly GUIContent GC_S3_Header = new GUIContent("<color=yellow>Strategy 3: Chunk-based Processing</color>");
        private static readonly GUIContent GC_S5_Header = new GUIContent("<color=yellow>Strategy 5: Update Frequency Optimization</color>");
    private static readonly GUIContent GC_Debug_Header = new GUIContent("<color=yellow>Debug & Visualization</color>");
    private static readonly GUIContent GC_Evap_Header = new GUIContent("<color=yellow>Evaporation</color>");
        private static readonly GUIContent GC_Dev_Header = new GUIContent("<color=orange>DEVELOPER TOOLS</color>");
        // Reusable dynamic GUIContent and string builder to avoid per-frame allocations
        private readonly GUIContent _tmpContent = new GUIContent();
        private readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder(128);
        private void LabelCached(Listing_Standard listing, GUIContent content)
        {
            Rect r = listing.GetRect(Text.LineHeight);
            Widgets.Label(r, content);
            if (!string.IsNullOrEmpty(content.tooltip))
            {
                TooltipHandler.TipRegion(r, content.tooltip);
            }
        }
        private void LabelDynamicInt(Listing_Standard listing, string prefix, int value, string suffix = null, string tooltip = null)
        {
            _sb.Clear();
            if (!string.IsNullOrEmpty(prefix)) _sb.Append(prefix);
            _sb.Append(value);
            if (!string.IsNullOrEmpty(suffix)) _sb.Append(suffix);
            _tmpContent.text = _sb.ToString();
            _tmpContent.tooltip = tooltip;
            LabelCached(listing, _tmpContent);
        }
        private void LabelDynamicIntPair(Listing_Standard listing, string prefix, int v1, string middle, int v2, string suffix = null, string tooltip = null)
        {
            _sb.Clear();
            if (!string.IsNullOrEmpty(prefix)) _sb.Append(prefix);
            _sb.Append(v1);
            if (!string.IsNullOrEmpty(middle)) _sb.Append(middle);
            _sb.Append(v2);
            if (!string.IsNullOrEmpty(suffix)) _sb.Append(suffix);
            _tmpContent.text = _sb.ToString();
            _tmpContent.tooltip = tooltip;
            LabelCached(listing, _tmpContent);
        }
        private void LabelDynamicFloat(Listing_Standard listing, string prefix, float value, string suffix = null, string tooltip = null)
        {
            _sb.Clear();
            if (!string.IsNullOrEmpty(prefix)) _sb.Append(prefix);
            _sb.Append(value.ToString("F1"));
            if (!string.IsNullOrEmpty(suffix)) _sb.Append(suffix);
            _tmpContent.text = _sb.ToString();
            _tmpContent.tooltip = tooltip;
            LabelCached(listing, _tmpContent);
        }
        
        // Static reference for easy access
        public static WaterSpringModMain Instance { get; private set; }

        public WaterSpringModMain(ModContentPack content) : base(content)
        {
            Instance = this; // Set the static reference
            settings = GetSettings<WaterSpringModSettings>();
            diffusionManager = new WaterDiffusionManager();
            // Sync logger cache with current settings
            WaterSpring.WaterSpringLogger.SetDebugEnabled(settings?.debugModeEnabled == true);
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
            tabs.Add(new TabRecord("Evaporation", () => selectedTab = 4, selectedTab == 4));
            tabs.Add(new TabRecord("Debug", () => selectedTab = 5, selectedTab == 5));
            
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
            if (selectedTab < 0 || selectedTab > 5) selectedTab = 0;

            switch (selectedTab)
            {
                case 0: // General Settings
                    // General settings header
                    LabelCached(listingStandard, GC_GeneralHeader);
                    listingStandard.Gap();
                    
                    // Terrain sync toggle
                    listingStandard.CheckboxLabeled("Sync terrain to water volume (shallow 1-4, deep 5-7)", ref settings.syncTerrainToWaterVolume,
                        "When enabled, terrain under water tiles will be set to Shallow water for volumes 1–4 and Deep water for 5–7. When volume returns to 0, original terrain is restored.");
                    listingStandard.Gap();

                    // Spring spawn interval
                    _tmpContent.text = "WaterSpringMod.SpawnIntervalLabel".Translate(settings.waterSpringSpawnInterval);
                    _tmpContent.tooltip = "Interval between spring spawns (ticks). Lower = faster water production; higher = slower.";
                    LabelCached(listingStandard, _tmpContent);
                    settings.waterSpringSpawnInterval = (int)listingStandard.Slider(settings.waterSpringSpawnInterval, 10, 1000);
                    
                    // Debug mode
                    listingStandard.Gap();
                    bool dbg = settings.debugModeEnabled;
                    listingStandard.CheckboxLabeled("Debug Mode (Enable Logging)", ref dbg, 
                        "Enable detailed logging for water springs. Warning: This may impact performance and create large log files.");
                    if (dbg != settings.debugModeEnabled)
                    {
                        settings.debugModeEnabled = dbg;
                        WaterSpring.WaterSpringLogger.SetDebugEnabled(dbg);
                    }
                    break;
                
                case 1: // Strategy 1: Active Tile Management
                    // Strategy 1 header
                    LabelCached(listingStandard, GC_S1_Header);
                    listingStandard.Gap();
                    
                    // Active tile system checkbox
                    listingStandard.CheckboxLabeled("Use Active Tile System", ref settings.useActiveTileSystem, 
                        "Enables the active tile optimization system to improve performance. Only actively changing water tiles are processed.");
                    
                    // Stability cap (only rule): tile becomes stable when reaching this many no-change attempts
                    listingStandard.Gap();
                    LabelDynamicInt(listingStandard, "Stability Cap: ", settings.stabilityCap, " no-change attempts",
                        tooltip: "A tile is considered stable ONLY when its stability counter reaches this cap.\nStability increases by 1 each time the tile is processed and no diffusion occurs.");
                    settings.stabilityCap = (int)listingStandard.Slider(settings.stabilityCap, 1, 1000);
                    listingStandard.Gap(4f);
                    _tmpContent.text = "Counts only when a tile attempts diffusion and nothing changes. Raise to require more cycles; lower to stabilize sooner."; _tmpContent.tooltip = null; LabelCached(listingStandard, _tmpContent);
                    _tmpContent.text = "How many ticks a water tile must remain unchanged before it's considered stable and removed from active processing."; _tmpContent.tooltip = null; LabelCached(listingStandard, _tmpContent);
                    
                    // Max processed tiles
                    listingStandard.Gap();
                    LabelDynamicInt(listingStandard, "Max Processed Tiles Per Tick: ", settings.maxProcessedTilesPerTick, null,
                        tooltip: "Upper bound on how many active water tiles are processed per game tick. Lower improves performance but slows diffusion.");
                    settings.maxProcessedTilesPerTick = (int)listingStandard.Slider(settings.maxProcessedTilesPerTick, 50, 2000);
                    listingStandard.Gap(4f);
                    _tmpContent.text = "Maximum number of water tiles processed each tick. Lower values improve performance but slow down water diffusion."; _tmpContent.tooltip = null; LabelCached(listingStandard, _tmpContent);

                    // Local diffusion timing
                    listingStandard.Gap();
                    LabelDynamicInt(listingStandard, "Local Check Interval Min: ", settings.localCheckIntervalMin, " ticks",
                        tooltip: "Minimum random wait between a tile’s diffusion attempts in normal mode. Lower = more frequent attempts.");
                    settings.localCheckIntervalMin = (int)listingStandard.Slider(settings.localCheckIntervalMin, 1, Mathf.Min(600, settings.localCheckIntervalMax));
                    LabelDynamicInt(listingStandard, "Local Check Interval Max: ", settings.localCheckIntervalMax, " ticks",
                        tooltip: "Maximum random wait between a tile’s diffusion attempts in normal mode. Higher = more spread-out activity.");
                    settings.localCheckIntervalMax = (int)listingStandard.Slider(settings.localCheckIntervalMax, settings.localCheckIntervalMin, 1200);
                    listingStandard.Gap(4f);
                    _tmpContent.text = "The random wait applied to individual tiles between diffusion attempts in the normal path."; _tmpContent.tooltip = null; LabelCached(listingStandard, _tmpContent);

                    // Anti-backflow controls
                    listingStandard.Gap();
                    listingStandard.CheckboxLabeled("Anti-backflow (reduce ping-pong)", ref settings.antiBackflowEnabled,
                        "Discourage immediate backflow into the cell that just sent water. Adds a temporary extra diff requirement.");
                    if (settings.antiBackflowEnabled)
                    {
                        LabelDynamicInt(listingStandard, "Backflow Cooldown: ", settings.backflowCooldownTicks, " ticks",
                            tooltip: "Window after outbound flow during which backflow is discouraged.");
                        settings.backflowCooldownTicks = (int)listingStandard.Slider(settings.backflowCooldownTicks, 0, 600);
                        LabelDynamicInt(listingStandard, "Backflow Min-Diff Bonus: +", settings.backflowMinDiffBonus, null,
                            tooltip: "Extra volume difference required to allow backflow during cooldown. 0 disables the penalty.");
                        settings.backflowMinDiffBonus = (int)listingStandard.Slider(settings.backflowMinDiffBonus, 0, 3);
                    }

                    // Diffusion rules (Normal method)
                    listingStandard.Gap();
                    LabelCached(listingStandard, GC_S1_DiffusionRules);
                    LabelDynamicInt(listingStandard, "Minimum Volume Difference for Transfer: ", settings.minVolumeDifferenceForTransfer, null,
                        tooltip: "Minimum volume difference required for water to flow between tiles in the normal path. Prevents equal-volume oscillation. Lower spreads more; higher stabilizes faster.");
                    settings.minVolumeDifferenceForTransfer = (int)listingStandard.Slider(settings.minVolumeDifferenceForTransfer, 1, 3);
                    _tmpContent.text = "The minimum volume difference required for water to flow between tiles. Lower values allow more natural spreading, higher values improve stability and performance."; _tmpContent.tooltip = null; LabelCached(listingStandard, _tmpContent);

                    // Spring behavior (Normal method)
                    listingStandard.Gap(10f);
                    LabelCached(listingStandard, GC_S1_SpringBehavior);
                    listingStandard.CheckboxLabeled("Spring uses backlog when full", ref settings.springUseBacklog,
                        "When the spring tile is full, store produced water in a small backlog and drip it out when capacity appears.");
                    if (settings.springUseBacklog)
                    {
                        LabelDynamicInt(listingStandard, "Backlog Cap: ", settings.springBacklogCap, " units",
                            tooltip: "Maximum water units the spring can store when the source tile is full.");
                        settings.springBacklogCap = (int)listingStandard.Slider(settings.springBacklogCap, 0, 20);
                        _sb.Clear(); _sb.Append("Backlog Inject Interval: every "); _sb.Append(settings.springBacklogInjectInterval); _sb.Append(" ticks");
                        _tmpContent.text = _sb.ToString();
                        _tmpContent.tooltip = "How often one backlog unit is injected when there is capacity.";
                        LabelCached(listingStandard, _tmpContent);
                        settings.springBacklogInjectInterval = (int)listingStandard.Slider(settings.springBacklogInjectInterval, 1, 300);
                    }
                    listingStandard.CheckboxLabeled("Prioritize spring tiles", ref settings.springPrioritizeTiles,
                        "Process spring tiles more aggressively so produced water moves out quickly.");
                    listingStandard.CheckboxLabeled("Spring tiles never stabilize", ref settings.springNeverStabilize,
                        "Spring source tiles are never removed from active processing based on stability.");

                    // Reactivation wave controls
                    listingStandard.Gap(10f);
                    LabelCached(listingStandard, GC_S1_Reactivation);
                    LabelDynamicInt(listingStandard, "Radius: ", settings.reactivationRadius, " tiles",
                        tooltip: "When a cell changes or a wall is removed, wake tiles within this radius.");
                    settings.reactivationRadius = (int)listingStandard.Slider(settings.reactivationRadius, 1, 32);
                    LabelDynamicInt(listingStandard, "Immediate Transfers Cap: ", settings.reactivationMaxTiles, " tiles",
                        tooltip: "Upper bound of tiles allowed to perform 1 immediate transfer on wake.");
                    settings.reactivationMaxTiles = (int)listingStandard.Slider(settings.reactivationMaxTiles, 0, 512);
                    listingStandard.CheckboxLabeled("Attempt one immediate transfer on wake", ref settings.reactivationImmediateTransfers,
                        "Helps restart flow quickly after openings.");
                    break;
                
                case 2: // Strategy 3: Chunk-based Processing
                    // Strategy 3 header
                    LabelCached(listingStandard, GC_S3_Header);
                    listingStandard.Gap();
                    
                    // Chunk processing checkbox
                    listingStandard.CheckboxLabeled("Use Chunk-based Processing", ref settings.useChunkBasedProcessing, 
                        "Organizes water tiles into spatial chunks for more efficient processing. Experimental feature.");
                    
                    if (settings.useChunkBasedProcessing)
                    {
                        // Chunk size
                        listingStandard.Gap();
                        LabelDynamicIntPair(listingStandard, "Chunk Size: ", settings.chunkSize, "x", settings.chunkSize, " tiles",
                            tooltip: "Tile width/height of spatial chunks used for batching. Smaller = more chunks; larger = fewer, larger chunks.");
                        settings.chunkSize = (int)listingStandard.Slider(settings.chunkSize, 4, 16);
                        
                        // Max processed chunks
                        listingStandard.Gap();
                        LabelDynamicInt(listingStandard, "Max Processed Chunks Per Tick: ", settings.maxProcessedChunksPerTick, null,
                            tooltip: "Upper bound on how many chunks are processed each tick. Lower improves performance but slows diffusion.");
                        settings.maxProcessedChunksPerTick = (int)listingStandard.Slider(settings.maxProcessedChunksPerTick, 5, 100);
                        
                        // Max processed tiles per chunk
                        listingStandard.Gap();
                        LabelDynamicInt(listingStandard, "Max Processed Tiles Per Chunk: ", settings.maxProcessedTilesPerChunk, null,
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
                    LabelCached(listingStandard, GC_S5_Header);
                    listingStandard.Gap();
                    
                    // Frequency-based processing checkbox
                    listingStandard.CheckboxLabeled("Use Frequency-based Processing", ref settings.useFrequencyBasedProcessing, 
                        "Controls how often water processing occurs. Experimental feature.");
                    
                    if (settings.useFrequencyBasedProcessing)
                    {
                        // Global update frequency
                        listingStandard.Gap();
                        _sb.Clear(); _sb.Append("Global Update Frequency: every "); _sb.Append(settings.globalUpdateFrequency); _sb.Append(" tick(s)");
                        _tmpContent.text = _sb.ToString();
                        _tmpContent.tooltip = "Run water processing only every N ticks (global gate). Larger values reduce CPU use and slow diffusion.";
                        LabelCached(listingStandard, _tmpContent);
                        settings.globalUpdateFrequency = (int)listingStandard.Slider(settings.globalUpdateFrequency, 1, 20);
                        
                        // Adaptive TPS throttling
                        listingStandard.Gap();
                        listingStandard.CheckboxLabeled("Use Adaptive TPS Throttling", ref settings.useAdaptiveTPS, 
                            "Automatically reduces water processing when game performance drops.");
                        
                        if (settings.useAdaptiveTPS)
                        {
                            listingStandard.Gap();
                            LabelDynamicFloat(listingStandard, "Minimum Target TPS: ", settings.minTPS, null,
                                tooltip: "Adaptive throttle target ticks-per-second. When TPS falls below this, processing is delayed more.");
                            settings.minTPS = (float)Math.Round(listingStandard.Slider(settings.minTPS, 10f, 1000f), 1);
                        }
                    }
                    break;
                
                case 4: // Evaporation
                    LabelCached(listingStandard, GC_Evap_Header);
                    listingStandard.Gap();

                    // Enable evaporation
                    listingStandard.CheckboxLabeled("Enable Evaporation", ref settings.evaporationEnabled,
                        "When enabled, stable, unroofed water tiles at or below the volume threshold may evaporate by 1 at periodic checks.");

                    // Interval
                    listingStandard.Gap();
                    LabelDynamicInt(listingStandard, "Evaporation Interval: ", settings.evaporationIntervalTicks, " ticks",
                        tooltip: "How often each water tile checks for evaporation.");
                    settings.evaporationIntervalTicks = (int)listingStandard.Slider(settings.evaporationIntervalTicks, 60, 6000);

                    // Threshold
                    listingStandard.Gap();
                    LabelDynamicInt(listingStandard, "Max Volume Threshold: ≤ ", settings.evaporationMaxVolumeThreshold, null,
                        tooltip: "Evaporation only applies when tile volume is at or below this value.");
                    settings.evaporationMaxVolumeThreshold = (int)listingStandard.Slider(settings.evaporationMaxVolumeThreshold, 0, 7);

                    // Chance
                    listingStandard.Gap();
                    LabelDynamicInt(listingStandard, "Evaporation Chance: ", settings.evaporationChancePercent, "%",
                        tooltip: "Percent chance that a qualified tile evaporates at each check.");
                    settings.evaporationChancePercent = (int)listingStandard.Slider(settings.evaporationChancePercent, 0, 100);

                    // Roof behavior
                    listingStandard.Gap(10f);
                    listingStandard.CheckboxLabeled("Only allow evaporation when unroofed", ref settings.evaporationOnlyUnroofed,
                        "When checked, roofed water tiles never evaporate. When unchecked, roofed tiles can evaporate using a separate chance.");
                    if (!settings.evaporationOnlyUnroofed)
                    {
                        LabelDynamicInt(listingStandard, "Evaporation Chance (Roofed): ", settings.evaporationChancePercentRoofed, "%",
                            tooltip: "Percent chance for roofed tiles when allowed.");
                        settings.evaporationChancePercentRoofed = (int)listingStandard.Slider(settings.evaporationChancePercentRoofed, 0, 100);
                    }
                    break;

                case 5: // Debug & Developer Tools
                    // Debug header
                    LabelCached(listingStandard, GC_Debug_Header);
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
                        LabelCached(listingStandard, GC_Dev_Header);
                        
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

        public override void WriteSettings()
        {
            base.WriteSettings();
            // Ensure cache reflects persisted value
            WaterSpring.WaterSpringLogger.SetDebugEnabled(settings?.debugModeEnabled == true);
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
