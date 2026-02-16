using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WaterSpringMod.WaterSpring
{
    public class Building_WaterPump : Building
    {
        private const int CycleInterval = 60;
        private const int MinSourceVolume = 2;
        private const int MinPumpStrength = 1;
        private const int MaxPumpStrength = 3;

        private CompPowerTrader powerComp;
        private CompFlickable flickComp;
        private CompBreakdownable breakdownComp;

        private bool verticalMode;
        private int pumpStrength = 1;
        private int ticksUntilNextPump = CycleInterval;
        private int totalPumped;

        public bool IsOperational =>
            powerComp != null && powerComp.PowerOn
            && (flickComp == null || flickComp.SwitchIsOn)
            && (breakdownComp == null || !breakdownComp.BrokenDown);

        private IntVec3 IntakeCell => Position - Rotation.FacingCell;
        private IntVec3 OutputCell => Position + Rotation.FacingCell;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = GetComp<CompPowerTrader>();
            flickComp = GetComp<CompFlickable>();
            breakdownComp = GetComp<CompBreakdownable>();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref verticalMode, "verticalMode", false);
            Scribe_Values.Look(ref pumpStrength, "pumpStrength", 1);
            Scribe_Values.Look(ref ticksUntilNextPump, "ticksUntilNextPump", CycleInterval);
            Scribe_Values.Look(ref totalPumped, "totalPumped", 0);
        }

        protected override void Tick()
        {
            base.Tick();

            if (!IsOperational)
                return;

            ticksUntilNextPump--;
            if (ticksUntilNextPump > 0)
                return;

            ticksUntilNextPump = CycleInterval;

            if (verticalMode && MultiFloorsIntegration.IsAvailable)
            {
                if (!TryPumpVertical())
                    TryPumpHorizontal();
            }
            else
            {
                TryPumpHorizontal();
            }
        }

        private void TryPumpHorizontal()
        {
            IntVec3 intake = IntakeCell;
            IntVec3 output = OutputCell;

            if (!intake.InBounds(Map) || !output.InBounds(Map))
                return;

            TransferWater(intake, Map, output, Map);
        }

        private bool TryPumpVertical()
        {
            if (!MultiFloorsIntegration.TryGetUpperMap(Map, out Map upperMap))
                return false;

            IntVec3 dest = Position;
            if (!dest.InBounds(upperMap))
                return false;

            if (MultiFloorsIntegration.IsVoidTerrain(upperMap, dest))
                return false;

            IntVec3 intake = IntakeCell;
            if (!intake.InBounds(Map))
                return false;

            TransferWater(intake, Map, dest, upperMap);
            return true;
        }

        private void TransferWater(IntVec3 intakePos, Map intakeMap, IntVec3 destPos, Map destMap)
        {
            FlowingWater source = intakeMap.thingGrid.ThingAt<FlowingWater>(intakePos);
            if (source == null || source.Volume < MinSourceVolume)
                return;

            if (!IsDestinationValid(destPos, destMap))
                return;

            int transferAmount = Mathf.Min(pumpStrength, source.Volume - MinSourceVolume + 1);
            if (transferAmount <= 0)
                return;

            FlowingWater destWater = destMap.thingGrid.ThingAt<FlowingWater>(destPos);
            if (destWater != null)
            {
                int capacity = FlowingWater.MaxVolume - destWater.Volume;
                if (capacity <= 0)
                    return;

                transferAmount = Mathf.Min(transferAmount, capacity);
                destWater.AddVolume(transferAmount);
            }
            else
            {
                ThingDef waterDef = DefDatabase<ThingDef>.GetNamed("FlowingWater", false);
                if (waterDef == null)
                    return;

                Thing newWater = ThingMaker.MakeThing(waterDef);
                if (newWater is FlowingWater typed)
                {
                    typed.Volume = 0;
                    GenSpawn.Spawn(newWater, destPos, destMap);
                    typed.AddVolume(transferAmount);
                }
            }

            source.Volume -= transferAmount;
            totalPumped += transferAmount;

            var dm = Current.Game?.GetComponent<GameComponent_WaterDiffusion>();
            if (dm != null)
            {
                dm.MarkChunkDirtyAt(intakeMap, intakePos);
                dm.MarkChunkDirtyAt(destMap, destPos);
            }
        }

        private bool IsDestinationValid(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map))
                return false;

            // Check for impassable terrain
            if (!cell.Walkable(map))
            {
                TerrainDef t = map.terrainGrid.TerrainAt(cell);
                if (t == null || !t.IsWater)
                    return false;
            }

            // Check for solid buildings that block water
            Building ed = cell.GetEdifice(map);
            if (ed != null && ed.def != null && ed.def.fillPercent > 0.1f)
            {
                var floodgate = ed as Building_WaterFloodgate;
                if (floodgate != null)
                {
                    if (!floodgate.IsOpen)
                        return false;
                }
                else
                {
                    var ext = ed.def.GetModExtension<WaterFlowExtension>();
                    if (ext == null || !ext.waterPassable)
                        return false;
                }
            }

            return true;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;

            yield return new Command_Action
            {
                defaultLabel = "Strength: " + pumpStrength,
                defaultDesc = "Cycle pump strength (1-3). Higher strength moves more water per cycle.",
                action = () =>
                {
                    pumpStrength++;
                    if (pumpStrength > MaxPumpStrength)
                        pumpStrength = MinPumpStrength;
                },
                icon = ContentFinder<Texture2D>.Get("UI/Commands/TempLower", true)
            };

            if (MultiFloorsIntegration.IsAvailable)
            {
                yield return new Command_Toggle
                {
                    defaultLabel = "Vertical mode",
                    defaultDesc = "When enabled, the pump pushes water upward to the level above instead of horizontally.",
                    isActive = () => verticalMode,
                    toggleAction = () => verticalMode = !verticalMode,
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/ToggleVent", true)
                };
            }
        }

        public override string GetInspectString()
        {
            var sb = new System.Text.StringBuilder(128);
            string baseStr = base.GetInspectString();
            if (!string.IsNullOrEmpty(baseStr))
                sb.Append(baseStr.Trim());

            if (sb.Length > 0) sb.Append('\n');
            sb.Append("Mode: ").Append(verticalMode && MultiFloorsIntegration.IsAvailable ? "Vertical" : "Horizontal");

            sb.Append('\n').Append("Pump strength: ").Append(pumpStrength);

            if (!IsOperational)
            {
                sb.Append('\n');
                if (breakdownComp != null && breakdownComp.BrokenDown)
                    sb.Append("Broken down");
                else if (flickComp != null && !flickComp.SwitchIsOn)
                    sb.Append("Switched off");
                else if (powerComp != null && !powerComp.PowerOn)
                    sb.Append("No power");
            }

            sb.Append('\n').Append("Total pumped: ").Append(totalPumped);

            return sb.ToString();
        }
    }
}
