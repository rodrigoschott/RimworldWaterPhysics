using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimWorld;

public class ScenPart_PursuingMechanoids : ScenPart
{
	private bool onStartMap = true;

	private Dictionary<Map, int> mapWarningTimers = new Dictionary<Map, int>();

	private Dictionary<Map, int> mapRaidTimers = new Dictionary<Map, int>();

	private bool questCompleted;

	private const int InitialWarningDelay = 2700;

	private const int InitialRaidDelay = 30000;

	private static IntRange WarningDelayRange = new IntRange(840000, 960000);

	private static IntRange RaidDelayRange = new IntRange(1080000, 2100000);

	private const int MinRaidPoints = 5000;

	private const float RaidPointMultiplier = 1.5f;

	private Map cachedAlertMap;

	private Alert_MechThreat alertCached;

	private bool hasGravEngineCached = true;

	private int lastCheckedGravEngineTick = -999999;

	private const int GravEngineCheckInterval = 2500;

	private List<Map> tmpWarningKeys;

	private List<int> tmpWarningValues;

	private List<Map> tmpRaidKeys;

	private List<int> tmpRaidValues;

	private Alert_MechThreat AlertCached
	{
		get
		{
			if (Disabled)
			{
				return null;
			}
			if (cachedAlertMap != Find.CurrentMap)
			{
				alertCached = null;
			}
			if (alertCached != null)
			{
				return alertCached;
			}
			if (mapWarningTimers.TryGetValue(Find.CurrentMap, out var value) && Find.TickManager.TicksGame > value)
			{
				alertCached = new Alert_MechThreat
				{
					raidTick = mapRaidTimers[Find.CurrentMap]
				};
				cachedAlertMap = Find.CurrentMap;
			}
			return alertCached;
		}
	}

	private bool Disabled
	{
		get
		{
			if (!questCompleted)
			{
				return !hasGravEngineCached;
			}
			return true;
		}
	}

	public override bool OverrideDangerMusic => onStartMap;

	public override void ExposeData()
	{
		base.ExposeData();
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			foreach (Map item in mapWarningTimers.Keys.ToList())
			{
				if (item?.Parent == null || item.Parent.Destroyed)
				{
					mapWarningTimers.Remove(item);
					mapRaidTimers.Remove(item);
				}
			}
		}
		Scribe_Values.Look(ref onStartMap, "initialMap", defaultValue: false);
		Scribe_Collections.Look(ref mapWarningTimers, "mapWarningTimers", LookMode.Reference, LookMode.Value, ref tmpWarningKeys, ref tmpWarningValues);
		Scribe_Collections.Look(ref mapRaidTimers, "mapRaidTimers", LookMode.Reference, LookMode.Value, ref tmpRaidKeys, ref tmpRaidValues);
		Scribe_Values.Look(ref questCompleted, "questCompleted", defaultValue: false);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (mapWarningTimers == null)
			{
				mapWarningTimers = new Dictionary<Map, int>();
			}
			if (mapRaidTimers == null)
			{
				mapRaidTimers = new Dictionary<Map, int>();
			}
		}
		lastCheckedGravEngineTick = -999999;
	}

	public override void PostWorldGenerate()
	{
		onStartMap = true;
		mapWarningTimers.Clear();
		mapRaidTimers.Clear();
	}

	public override void PostMapGenerate(Map map)
	{
		if (map.IsPlayerHome)
		{
			StartTimers(map);
			lastCheckedGravEngineTick = -999999;
		}
	}

	public override void MapRemoved(Map map)
	{
		if (mapWarningTimers.Remove(map))
		{
			mapRaidTimers.Remove(map);
			onStartMap = false;
		}
	}

	public override void Tick()
	{
		if (Find.TickManager.TicksGame > lastCheckedGravEngineTick + 2500)
		{
			hasGravEngineCached = GravshipUtility.PlayerHasGravEngine();
			lastCheckedGravEngineTick = Find.TickManager.TicksGame;
		}
		if (Disabled)
		{
			foreach (Map map in Find.Maps)
			{
				if (mapWarningTimers.ContainsKey(map))
				{
					mapWarningTimers[map]++;
					mapRaidTimers[map]++;
				}
			}
			return;
		}
		foreach (Map key in mapWarningTimers.Keys)
		{
			if (Find.TickManager.TicksGame == mapWarningTimers[key])
			{
				Thing thing = key.listerThings.ThingsOfDef(ThingDefOf.PilotConsole).FirstOrDefault();
				Find.LetterStack.ReceiveLetter("LetterLabelMechanoidThreat".Translate(), "LetterTextMechanoidThreat".Translate(), LetterDefOf.ThreatSmall, thing);
			}
		}
		foreach (Map key2 in mapRaidTimers.Keys)
		{
			if (Find.TickManager.TicksGame >= mapRaidTimers[key2] && (Find.TickManager.TicksGame - mapRaidTimers[key2]) % 30000 == 0)
			{
				FireRaid(key2);
			}
		}
	}

	private void StartTimers(Map map)
	{
		if (map.generatorDef != MapGeneratorDefOf.Mechhive)
		{
			if (onStartMap)
			{
				mapWarningTimers[map] = Find.TickManager.TicksGame + 2700;
				mapRaidTimers[map] = Find.TickManager.TicksGame + 30000;
			}
			else
			{
				mapWarningTimers[map] = Find.TickManager.TicksGame + WarningDelayRange.RandomInRange;
				mapRaidTimers[map] = Find.TickManager.TicksGame + RaidDelayRange.RandomInRange;
			}
		}
	}

	public void Notify_QuestCompleted()
	{
		questCompleted = true;
	}

	private void FireRaid(Map map)
	{
		IncidentParms incidentParms = new IncidentParms();
		incidentParms.forced = true;
		incidentParms.target = map;
		incidentParms.points = Mathf.Max(5000f, StorytellerUtility.DefaultThreatPointsNow(map) * 1.5f);
		incidentParms.faction = Faction.OfMechanoids;
		incidentParms.raidArrivalMode = PawnsArrivalModeDefOf.RandomDrop;
		incidentParms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
		IncidentDefOf.RaidEnemy.Worker.TryExecute(incidentParms);
	}

	public override IEnumerable<Alert> GetAlerts()
	{
		Map currentMap = Find.CurrentMap;
		if (currentMap != null && currentMap.IsPlayerHome && AlertCached != null)
		{
			yield return AlertCached;
		}
	}
}
