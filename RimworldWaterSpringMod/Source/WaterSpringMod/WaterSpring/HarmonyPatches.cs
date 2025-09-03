using System;
using Verse;
using RimWorld;
using HarmonyLib;

namespace WaterSpringMod.WaterSpring
{
    [StaticConstructorOnStartup]
    public static class HarmonyPatches
    {
        static HarmonyPatches()
        {
            Harmony harmony = new Harmony("rodrigoschott.waterphysics.patches");
            
            // Patch Thing.SpawnSetup to detect new buildings/walls
            harmony.Patch(
                original: AccessTools.Method(typeof(Thing), nameof(Thing.SpawnSetup)),
                postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Thing_SpawnSetup_Postfix))
            );
            
            // Patch Thing.DeSpawn to detect removed buildings/walls
            harmony.Patch(
                original: AccessTools.Method(typeof(Thing), nameof(Thing.DeSpawn)),
                prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Thing_DeSpawn_Prefix))
            );
            
            WaterSpringLogger.LogDebug("WaterSpringMod harmony patches applied");
        }
        
        // Postfix for Thing.SpawnSetup
        public static void Thing_SpawnSetup_Postfix(Thing __instance, Map map)
        {
            // Only care about buildings and things that block movement
            if (__instance.def.category != ThingCategory.Building || __instance.def.fillPercent <= 0.1f)
                return;
                
            // Get the diffusion manager
            GameComponent_WaterDiffusion diffusionManager = Current.Game?.GetComponent<GameComponent_WaterDiffusion>();
            if (diffusionManager != null)
            {
                // Notify the water system about this terrain change
                diffusionManager.NotifyTerrainChanged(map, __instance.Position);
            }
        }
        
        // Prefix for Thing.DeSpawn to capture position before despawn
        public static bool Thing_DeSpawn_Prefix(Thing __instance, DestroyMode mode = DestroyMode.Vanish)
        {
            // Only care about buildings and things that block movement
            if (__instance.def.category != ThingCategory.Building || __instance.def.fillPercent <= 0.1f)
                return true;
                
            // Get the position and map before despawning
            IntVec3 position = __instance.Position;
            Map map = __instance.Map;
            
            if (map != null)
            {
                // Get the diffusion manager
                GameComponent_WaterDiffusion diffusionManager = Current.Game?.GetComponent<GameComponent_WaterDiffusion>();
                if (diffusionManager != null)
                {
                    // Notify the water system about this terrain change
                    diffusionManager.NotifyTerrainChanged(map, position);
                }
            }
            
            return true; // Continue with original method
        }
    }
}
