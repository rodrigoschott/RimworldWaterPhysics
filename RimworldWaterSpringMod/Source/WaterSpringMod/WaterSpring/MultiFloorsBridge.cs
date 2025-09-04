using Verse;

namespace WaterSpringMod.WaterSpring
{
    // Obsolete: MultiFloors integration removed. Kept as a no-op stub for compatibility.
    internal static class MultiFloorsBridge
    {
        public static bool IsActive() => false;
        public static bool IsVoidTerrain(TerrainDef t) => false;
        public static bool TryGetLowerMap(Map current, out Map lower) { lower = null; return false; }
    }
}
