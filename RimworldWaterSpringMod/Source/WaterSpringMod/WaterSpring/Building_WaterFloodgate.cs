using RimWorld;
using Verse;

namespace WaterSpringMod.WaterSpring
{
    /// <summary>
    /// A floodgate that extends Building_Door for proper pathfinding integration.
    /// State is driven entirely by linked WS_Lever buildings via CompAffectedByFacilities.
    /// Any linked lever ON = gate open. All OFF (or no levers) = gate closed.
    /// Pawns can NEVER open the gate themselves.
    /// </summary>
    public class Building_WaterFloodgate : Building_Door
    {
        private CompAffectedByFacilities facilityComp;
        private bool lastOpenState;

        /// <summary>
        /// Whether the gate is open (any linked lever is ON).
        /// Used by FlowingWater.cs for water flow decisions.
        /// </summary>
        public bool IsOpen => AnyLeverOn();

        /// <summary>
        /// When gate is open, prevent auto-close by reporting as always-open.
        /// </summary>
        protected override bool AlwaysOpen => IsOpen;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            facilityComp = GetComp<CompAffectedByFacilities>();
            lastOpenState = IsOpen;

            // Sync door state to lever state on spawn
            if (IsOpen && !Open)
                DoorOpen();
        }

        /// <summary>
        /// Pawns can NEVER open the gate themselves — only levers control it.
        /// </summary>
        public override bool PawnCanOpen(Pawn p) => false;

        protected override void Tick()
        {
            base.Tick();

            bool current = IsOpen;
            if (current != lastOpenState)
            {
                lastOpenState = current;
                if (current)
                {
                    DoorOpen();
                }
                else
                {
                    // Force close
                    openInt = false;
                    Map?.reachability.ClearCache();
                }

                // Wake water tiles around the gate
                var dm = Current.Game?.GetComponent<GameComponent_WaterDiffusion>();
                dm?.MarkChunkDirtyAt(Map, Position);
                foreach (IntVec3 dir in GenAdj.CardinalDirections)
                {
                    IntVec3 adj = Position + dir;
                    if (adj.InBounds(Map))
                    {
                        dm?.MarkChunkDirtyAt(Map, adj);
                        var w = Map.thingGrid.ThingAt<FlowingWater>(adj);
                        if (w != null) w.ClearStatic();
                    }
                }
            }

            // Keep door synced with lever state
            if (current && !Open)
                DoorOpen();
        }

        private bool AnyLeverOn()
        {
            if (facilityComp == null)
                return false;

            foreach (var facility in facilityComp.LinkedFacilitiesListForReading)
            {
                var flick = facility.TryGetComp<CompFlickable>();
                if (flick != null && flick.SwitchIsOn)
                    return true;
            }

            return false;
        }
    }
}
