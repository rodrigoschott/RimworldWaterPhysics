using RimWorld;
using RimWorld.Planet;
using UnityEngine;

internal static class _0024BurstDirectCallInitializer
{
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
	private static void Initialize()
	{
		MapGenUtility.ComputeLargestRects_0000B6CE_0024BurstDirectCall.Initialize();
		MapGenUtility.RectsComputeSpaces_0000B6CF_0024BurstDirectCall.Initialize();
		FastTileFinder.Initialize_0024ComputeQueryJob_SphericalDistance_00014E48_0024BurstDirectCall();
		PlanetLayer.CalculateAverageTileSize_00015309_0024BurstDirectCall.Initialize();
		PlanetLayer.IntGetTileSize_0001530B_0024BurstDirectCall.Initialize();
		PlanetLayer.IntGetTileCenter_0001530E_0024BurstDirectCall.Initialize();
	}
}
