using Verse;

namespace WaterSpringMod.WaterSpring
{
    /// <summary>
    /// A water channel that connects to adjacent channels like conduits.
    /// Water flows freely between connected channels.
    /// Water only leaves the channel network (to non-channel tiles) at max volume (overflow).
    /// </summary>
    public class Building_WaterChannel : Building
    {
        public override void Print(SectionLayer layer)
        {
            // Hide channel graphic when water is present (water terrain takes over)
            if (Spawned && Map != null)
            {
                FlowingWater water = Map.thingGrid.ThingAt<FlowingWater>(Position);
                if (water != null && water.Spawned && !water.Destroyed)
                    return; // Skip rendering — water terrain is visible instead
            }
            base.Print(layer);
        }
    }
}
