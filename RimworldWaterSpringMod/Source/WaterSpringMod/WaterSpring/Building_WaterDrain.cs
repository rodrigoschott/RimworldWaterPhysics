using RimWorld;
using Verse;

namespace WaterSpringMod.WaterSpring
{
    public class Building_WaterDrain : Building
    {
        private CompAffectedByFacilities facilityComp;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            facilityComp = GetComp<CompAffectedByFacilities>();
        }

        /// <summary>
        /// Drain is active when no levers are linked, or when any linked lever is ON.
        /// </summary>
        public bool IsActive
        {
            get
            {
                if (facilityComp == null)
                    return true;

                var linked = facilityComp.LinkedFacilitiesListForReading;
                if (linked == null || linked.Count == 0)
                    return true;

                foreach (var facility in linked)
                {
                    var flick = facility.TryGetComp<CompFlickable>();
                    if (flick != null && flick.SwitchIsOn)
                        return true;
                }
                return false;
            }
        }

        protected override void Tick()
        {
            base.Tick();
            if (!Spawned || Map == null || !IsActive) return;

            FlowingWater water = Map.thingGrid.ThingAt<FlowingWater>(Position);
            if (water != null && water.Volume > 0)
            {
                water.Volume -= 1;
            }
        }
    }
}
