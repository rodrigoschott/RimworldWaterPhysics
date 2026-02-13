using RimWorld;
using Verse;

namespace WaterSpringMod.WaterSpring
{
    /// <summary>
    /// A floodgate that can be toggled open/closed via CompFlickable.
    /// Closed = blocks water AND pawns. Open = water and pawns pass freely.
    /// State changes trigger pathgrid recalculation and ReactivateInRadius.
    /// </summary>
    public class Building_WaterFloodgate : Building
    {
        private bool lastOpenState;
        private CompFlickable flickComp;

        public bool IsOpen => flickComp != null && flickComp.SwitchIsOn;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            flickComp = GetComp<CompFlickable>();
            lastOpenState = IsOpen;
        }

        public override void TickRare()
        {
            base.TickRare();
            bool currentOpen = IsOpen;
            if (currentOpen != lastOpenState)
            {
                lastOpenState = currentOpen;
                OnStateChanged();
            }
        }

        private void OnStateChanged()
        {
            if (Map == null) return;

            // Recalculate pathgrid so pawns can/can't walk through
            Map.pathing.RecalculatePerceivedPathCostAt(Position);

            // Wake nearby water tiles
            var dm = Current.Game?.GetComponent<GameComponent_WaterDiffusion>();
            dm?.ReactivateInRadius(Map, Position);
        }

        // Open floodgate should not block pawns
        public override bool BlocksPawn(Pawn p)
        {
            return !IsOpen;
        }
    }
}
