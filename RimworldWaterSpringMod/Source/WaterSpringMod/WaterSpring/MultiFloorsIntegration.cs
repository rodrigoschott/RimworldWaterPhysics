using System.Collections.Generic;
using System.Linq;
using MultiFloors;
using MultiFloors.Maps;
using Verse;

namespace WaterSpringMod.WaterSpring
{
    /// <summary>
    /// MultiFloors integration via runtime detection (standard RimWorld pattern).
    /// 
    /// ⚠️ COMPILATION NOTE (Mono lazy JIT):
    /// This file compiles with MultiFloors.dll as a reference (copy-local = false).
    /// At runtime, methods that reference MF types are only JIT-compiled when called.
    /// ALL MF type references (StairEntrance, StairExit, Elevator, map.Level(), etc.)
    /// MUST stay in methods that are ONLY called when IsAvailable == true.
    /// The IsAvailable property itself uses only vanilla Verse types (safe always).
    /// 
    /// ❌ NEVER put MF types in unconditionally-called methods.
    /// ✅ ALL MF-specific code is behind IsAvailable guard → lazy JIT → safe.
    /// </summary>
    public static class MultiFloorsIntegration
    {
        private static bool? _isAvailable;

        /// <summary>
        /// Check if MultiFloors is loaded (cached after first call).
        /// This property uses ONLY vanilla types — safe to call always.
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable == null)
                {
                    _isAvailable = GenTypes.GetTypeInAnyAssembly("MultiFloors.MF_LevelMapComp") != null;

                    if (_isAvailable.Value)
                    {
                        Log.Message("[WaterPhysics] MultiFloors integration ACTIVE — using direct API");
                    }
                    else
                    {
                        Log.Message("[WaterPhysics] Standalone mode (generic portal bridge)");
                    }
                }
                return _isAvailable.Value;
            }
        }

        // =====================================================================
        // MAP LINKAGE — Direct Prepatcher field access
        // =====================================================================

        /// <summary>
        /// Try to get the map directly below using MF Prepatcher fields.
        /// </summary>
        public static bool TryGetLowerMap(Map map, out Map lower)
        {
            lower = null;
            if (!IsAvailable || map == null) return false;

            try
            {
                lower = map.LowerMap();
                return lower != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Try to get the map directly above using MF Prepatcher fields.
        /// </summary>
        public static bool TryGetUpperMap(Map map, out Map upper)
        {
            upper = null;
            if (!IsAvailable || map == null) return false;

            try
            {
                upper = map.UpperMap();
                return upper != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get the level integer for a map (ground=0, upper>0, basement&lt;0).
        /// Returns 0 (ground) if MF not available.
        /// </summary>
        public static int GetLevel(Map map)
        {
            if (!IsAvailable || map == null) return 0;

            try
            {
                return map.Level();
            }
            catch
            {
                return 0;
            }
        }

        // =====================================================================
        // VOID TERRAIN — Uses GroundsOnLevel (NO hardcoded defNames!)
        // =====================================================================

        /// <summary>
        /// Check if cell is void terrain using GroundsOnLevel.
        /// ⚠️ CRITICAL: Do NOT use hardcoded defNames — void is biome-dependent!
        /// </summary>
        public static bool IsVoidTerrain(Map map, IntVec3 cell)
        {
            if (!IsAvailable || map == null) return false;

            try
            {
                int level = map.Level();
                if (level <= 0) return false;

                if (!map.TryGetLevelControllerOnCurrentTile(out var controller))
                    return false;

                // ✅ PRIMARY METHOD: Cell NOT in GroundsOnLevel = void
                var grounds = controller.UpperLevelTerrainGrid?.GetGroundsAtLevel(level);
                if (grounds == null) return false;

                return !grounds.Contains(cell);
            }
            catch
            {
                return false;
            }
        }

        // =====================================================================
        // CROSS-LEVEL ACTIVATION
        // =====================================================================

        /// <summary>
        /// Get vertically outward levels for cross-level activation.
        /// Uses TryGetValue for defensive coding.
        /// </summary>
        public static List<(int level, Map map)> GetVerticallyOutwardLevels(Map map)
        {
            if (!IsAvailable || map == null) return null;

            try
            {
                if (!map.TryGetLevelControllerOnCurrentTile(out var controller))
                    return null;

                int currentLevel = map.Level();

                if (!controller.VerticallyOutwardLevels.TryGetValue(currentLevel, out var outwardLevels))
                    return null;

                return outwardLevels;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Check if map is connected to other levels.
        /// </summary>
        public static bool IsMultiLevel(Map map)
        {
            if (!IsAvailable || map == null) return false;

            try
            {
                return map.ConnectedToOtherLevel();
            }
            catch
            {
                return false;
            }
        }

        // =====================================================================
        // STAIR WATER FLOW — Direct types (StairEntrance, StairExit)
        // ⚠️ These methods reference MF types directly. Mono lazy JIT ensures
        //    they are only compiled when called (always behind IsAvailable guard).
        // =====================================================================

        /// <summary>
        /// Check for stairs at a cell and return destination info for water flow.
        /// Uses direct MF types (StairEntrance, StairExit) — no reflection.
        /// 
        /// Direction logic:
        /// - StairEntrance with Direction.Down → water flows DOWN (gravity, always allowed)
        /// - StairExit → compare exitMap.Level() vs current map.Level():
        ///     - destLevel &lt; currentLevel → downward (gravity, free)
        ///     - destLevel &gt; currentLevel → upward (pressure-gated by settings)
        /// </summary>
        public static bool TryGetStairDestination(Map map, IntVec3 cell, int currentVolume,
            WaterSpringModSettings settings, out Map destMap, out IntVec3 destCell, out bool isDownward)
        {
            destMap = null;
            destCell = IntVec3.Invalid;
            isDownward = true;

            if (!IsAvailable || map == null || settings == null) return false;

            try
            {
                int currentLevel = map.Level();

                // --- Check StairEntrance (top of stairs → water flows down) ---
                var stairEntrance = map.thingGrid.ThingAt<MultiFloors.StairEntrance>(cell);
                if (stairEntrance != null 
                    && stairEntrance.Direction == MultiFloors.StairDirection.Down 
                    && stairEntrance.ConnectedMap != null)
                {
                    IntVec3 dest = stairEntrance.GetDestinationLocation();
                    Map connected = stairEntrance.ConnectedMap;

                    if (dest.IsValid && dest.InBounds(connected) && dest.Walkable(connected))
                    {
                        destMap = connected;
                        destCell = dest;
                        isDownward = true;
                        return true;
                    }
                }

                // --- Check StairExit (bottom of stairs → direction depends on level comparison) ---
                var stairExit = map.thingGrid.ThingAt<MultiFloors.StairExit>(cell);
                if (stairExit != null)
                {
                    Map otherMap = stairExit.GetOtherMap();
                    if (otherMap == null) return false;

                    IntVec3 dest = stairExit.GetDestinationLocation();
                    if (!dest.IsValid || !dest.InBounds(otherMap)) return false;

                    int otherLevel = otherMap.Level();
                    if (otherLevel == currentLevel) return false; // Same level, not a vertical portal

                    bool isUpward = otherLevel > currentLevel;

                    if (isUpward)
                    {
                        // Upward flow: pressure-gated
                        if (!settings.upwardStairFlowEnabled) return false;
                        if (currentVolume < settings.minVolumeForUpwardFlow) return false;

                        // Don't push water up if destination already has significant volume
                        var destWater = otherMap.thingGrid.ThingAt<FlowingWater>(dest);
                        if (destWater != null && destWater.Volume >= 3) return false;
                    }
                    // Downward: always allowed (gravity)

                    destMap = otherMap;
                    destCell = dest;
                    isDownward = !isUpward;
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // =====================================================================
        // ELEVATOR WATER FLOW (Phase 5)
        // Direct types: Elevator, ElevatorNet
        //
        // Elevator.Functional semantics (COUNTERINTUITIVE):
        //   Returns FALSE when ElevatorNet.Working == true
        //   Working = currently animating/transporting a pawn
        //   Functional = idle + powered + connected to other level
        //   Water should ONLY flow when Functional == true (elevator at rest)
        //
        // Elevator.GetOtherMap() → always returns null
        // Elevator.GetDestinationLocation() → always returns IntVec3.Invalid
        // Must use: ElevatorNet.GetElevatorOnMap(destMap) → elevator.Position
        // =====================================================================

        /// <summary>
        /// Check for an elevator at a cell and return the best destination for water flow.
        /// Water flows downward through elevator shafts to the lowest connected level.
        /// Optionally requires power (setting: elevatorRequiresPower).
        /// </summary>
        public static bool TryGetElevatorDestination(Map map, IntVec3 cell, int currentVolume,
            WaterSpringModSettings settings, out Map destMap, out IntVec3 destCell)
        {
            destMap = null;
            destCell = IntVec3.Invalid;

            if (!IsAvailable || map == null || settings == null) return false;
            if (!settings.elevatorWaterFlowEnabled) return false;

            try
            {
                var elevator = map.thingGrid.ThingAt<MultiFloors.Elevator>(cell);
                if (elevator == null) return false;

                // Functional = idle + powered (if required) + connected to other level
                // NOT animating (Working == false)
                if (!elevator.Functional) return false;

                var net = elevator.ElevatorNet;
                if (net == null) return false;

                int currentLevel = map.Level();

                // Find the lowest-level elevator in the network below current level
                // Water flows DOWN by gravity through the shaft
                MultiFloors.Elevator bestTarget = null;
                int bestLevel = currentLevel;

                foreach (var linked in net.LinkedElevators)
                {
                    if (linked == elevator) continue; // Skip self
                    if (linked.Map == null) continue;

                    int linkedLevel = linked.Map.Level();
                    if (linkedLevel < bestLevel)
                    {
                        bestLevel = linkedLevel;
                        bestTarget = linked;
                    }
                }

                if (bestTarget == null) return false; // No lower elevator found

                // Use the elevator's position on the destination map
                IntVec3 elevPos = ((Thing)bestTarget).Position;
                if (!elevPos.InBounds(bestTarget.Map)) return false;

                destMap = bestTarget.Map;
                destCell = elevPos;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
