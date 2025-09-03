// Dev-only perf harness. Compiled only when WATERPHYSICS_DEV is defined (Debug default).
#if WATERPHYSICS_DEV
using RimWorld;
using UnityEngine;
using Verse;

namespace WaterSpringMod.WaterSpring
{
    [StaticConstructorOnStartup]
    internal static class PerfHarness
    {
        static PerfHarness()
        {
            // No auto-run; exposed via DevMode hotkey below
        }

        // Alt+P to spawn N springs around the mouse
        public static void OnGUI()
        {
            if (!Prefs.DevMode) return;
            if (Event.current.type == EventType.KeyDown && Event.current.alt && Event.current.keyCode == KeyCode.P)
            {
                var map = Find.CurrentMap;
                if (map == null) return;
                IntVec3 c = UI.MouseCell();
                int n = 10; // small default
                for (int i = 0; i < n; i++)
                {
                    IntVec3 pos = c + new IntVec3(Rand.Range(-5, 5), 0, Rand.Range(-5, 5));
                    if (!pos.InBounds(map) || !pos.Walkable(map)) continue;
                    var def = DefDatabase<ThingDef>.GetNamed("WaterSpring", false);
                    if (def != null)
                    {
                        GenSpawn.Spawn(def, pos, map);
                    }
                }
                Messages.Message($"PerfHarness: spawned {n} springs near {c}.", MessageTypeDefOf.TaskCompletion);
                Event.current.Use();
            }
        }
    }
}
#endif