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

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref waterSpringSpawnInterval, "waterSpringSpawnInterval", 120);
            Scribe_Values.Look(ref debugModeEnabled, "debugModeEnabled", false);
        }
    }
}