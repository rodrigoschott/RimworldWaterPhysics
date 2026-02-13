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
}
