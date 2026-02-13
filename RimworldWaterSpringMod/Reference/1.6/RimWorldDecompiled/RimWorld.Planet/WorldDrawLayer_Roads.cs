using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace RimWorld.Planet;

public class WorldDrawLayer_Roads : WorldDrawLayer_Paths
{
	private readonly ModuleBase roadDisplacementX = new Perlin(1.0, 2.0, 0.5, 3, 74173887, QualityMode.Medium);

	private readonly ModuleBase roadDisplacementY = new Perlin(1.0, 2.0, 0.5, 3, 67515931, QualityMode.Medium);

	private readonly ModuleBase roadDisplacementZ = new Perlin(1.0, 2.0, 0.5, 3, 87116801, QualityMode.Medium);

	public override bool VisibleWhenLayerNotSelected => false;

	public override bool VisibleInBackground => false;

	public override IEnumerable Regenerate()
	{
		foreach (object item in base.Regenerate())
		{
			yield return item;
		}
		LayerSubMesh subMesh = GetSubMesh(WorldMaterials.Roads);
		List<RoadWorldLayerDef> roadLayerDefs = DefDatabase<RoadWorldLayerDef>.AllDefs.OrderBy((RoadWorldLayerDef def) => def.order).ToList();
		int i = 0;
		while (i < planetLayer.TilesCount)
		{
			if (i % 1000 == 0)
			{
				yield return null;
			}
			if (subMesh.verts.Count > 60000)
			{
				subMesh = GetSubMesh(WorldMaterials.Roads);
			}
			SurfaceTile surfaceTile = (SurfaceTile)planetLayer[i];
			if (!surfaceTile.WaterCovered)
			{
				List<OutputDirection> list = new List<OutputDirection>();
				if (surfaceTile.potentialRoads != null)
				{
					bool allowSmoothTransition = true;
					for (int j = 0; j < surfaceTile.potentialRoads.Count - 1; j++)
					{
						if (surfaceTile.potentialRoads[j].road.worldTransitionGroup != surfaceTile.potentialRoads[j + 1].road.worldTransitionGroup)
						{
							allowSmoothTransition = false;
						}
					}
					for (int k = 0; k < roadLayerDefs.Count; k++)
					{
						bool flag = false;
						list.Clear();
						for (int l = 0; l < surfaceTile.potentialRoads.Count; l++)
						{
							RoadDef road = surfaceTile.potentialRoads[l].road;
							float layerWidth = road.GetLayerWidth(roadLayerDefs[k]);
							if (layerWidth > 0f)
							{
								flag = true;
							}
							list.Add(new OutputDirection
							{
								neighbor = surfaceTile.potentialRoads[l].neighbor,
								width = layerWidth,
								distortionFrequency = road.distortionFrequency,
								distortionIntensity = road.distortionIntensity
							});
						}
						if (flag)
						{
							GeneratePaths(subMesh, new PlanetTile(i, planetLayer), list, roadLayerDefs[k].color, allowSmoothTransition);
						}
					}
				}
			}
			int num = i + 1;
			i = num;
		}
		FinalizeMesh(MeshParts.All);
	}

	public override Vector3 FinalizePoint(Vector3 inp, float distortionFrequency, float distortionIntensity)
	{
		Vector3 coordinate = inp * distortionFrequency;
		float magnitude = inp.magnitude;
		Vector3 vector = new Vector3(roadDisplacementX.GetValue(coordinate), roadDisplacementY.GetValue(coordinate), roadDisplacementZ.GetValue(coordinate));
		if ((double)vector.magnitude > 0.0001)
		{
			float num = (1f / (1f + Mathf.Exp((0f - vector.magnitude) / 1f * 2f)) * 2f - 1f) * 1f;
			vector = vector.normalized * num;
		}
		inp = (inp + vector * distortionIntensity).normalized * magnitude;
		return inp + inp.normalized * 0.02f;
	}
}
