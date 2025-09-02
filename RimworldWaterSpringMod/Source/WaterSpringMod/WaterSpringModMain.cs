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
        private WaterSpringModSettings settings;
        private WaterDiffusionManager diffusionManager;
        
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
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);
            
            listingStandard.Label("WaterSpringMod.SpawnIntervalLabel".Translate(settings.waterSpringSpawnInterval));
            settings.waterSpringSpawnInterval = (int)listingStandard.Slider(settings.waterSpringSpawnInterval, 10, 1000);
            
            // Removed diffusion interval since we no longer use it
            
            listingStandard.Gap();
            listingStandard.CheckboxLabeled("Debug Mode (Enable Logging)", ref settings.debugModeEnabled, "Enable detailed logging for water springs. Warning: This may impact performance and create large log files.");
            
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
            
            listingStandard.End();
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
