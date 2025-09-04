using Verse;

namespace WaterSpringMod.WaterSpring
{
    public class Building_WSHole : Building
    {
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            WaterSpringLogger.LogDebug($"[Portal] WS_Hole spawned at {Position} on map #{map?.uniqueID}");
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            WaterSpringLogger.LogDebug($"[Portal] WS_Hole despawn at {Position} on map #{Map?.uniqueID}");
            base.DeSpawn(mode);
        }
    }
}
