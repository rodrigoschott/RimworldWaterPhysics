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
    }
}
