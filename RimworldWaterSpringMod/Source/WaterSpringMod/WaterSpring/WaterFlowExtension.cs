using Verse;

namespace WaterSpringMod.WaterSpring
{
    /// <summary>
    /// DefModExtension that marks a building as water-passable.
    /// Add to any ThingDef's modExtensions to let water flow through it
    /// regardless of fillPercent.
    /// </summary>
    public class WaterFlowExtension : DefModExtension
    {
        public bool waterPassable = true;
    }

    /// <summary>
    /// Per-def spring settings. Overrides global WaterSpringModSettings values
    /// when attached to a spring building's ThingDef.
    /// </summary>
    public class SpringSettingsExtension : DefModExtension
    {
        public int spawnInterval = 120;
        public int backlogCap = 7;
        public int backlogInjectInterval = 30;
        public bool neverStabilize = true;
    }
}
