using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RimWorld;

public class RitualBehaviorWorker_GravshipLaunch : RitualBehaviorWorker
{
	private Dictionary<Pawn, bool> cachedCanReachGravship = new Dictionary<Pawn, bool>();

	private int cacheTick = -1;

	public override bool ChecksReservations => false;

	public RitualBehaviorWorker_GravshipLaunch()
	{
	}

	public RitualBehaviorWorker_GravshipLaunch(RitualBehaviorDef def)
		: base(def)
	{
	}

	public override bool TargetStillAllowed(TargetInfo selectedTarget, LordJob_Ritual ritual)
	{
		if (!base.TargetStillAllowed(selectedTarget, ritual))
		{
			return false;
		}
		return true;
	}

	public override string ExpectedDuration(Precept_Ritual ritual, RitualRoleAssignments assignments, float quality)
	{
		return null;
	}

	private bool CanReachGravship(Pawn pawn, Building_GravEngine engine)
	{
		if (Find.TickManager.TicksGame != cacheTick)
		{
			cacheTick = Find.TickManager.TicksGame;
			cachedCanReachGravship.Clear();
		}
		if (cachedCanReachGravship.TryGetValue(pawn, out var value))
		{
			return value;
		}
		IntVec3 spot;
		bool flag = GravshipUtility.TryFindSpotOnGravship(pawn, engine, out spot);
		cachedCanReachGravship[pawn] = flag;
		return flag;
	}

	public override bool PawnCanFillRole(Pawn pawn, RitualRole role, out string reason, TargetInfo ritualTarget)
	{
		reason = null;
		Building_GravEngine building_GravEngine = ritualTarget.Thing.TryGetComp<CompPilotConsole>()?.engine;
		if (building_GravEngine == null)
		{
			Log.ErrorOnce("Could not find engine for gravship launch", 23184679);
			return false;
		}
		if (role == null)
		{
			if (!CanReachGravship(pawn, building_GravEngine))
			{
				reason = "NoPathToGravship".Translate();
				return false;
			}
		}
		else if (!pawn.IsPrisoner && !pawn.CanReach(ritualTarget.Thing, PathEndMode.InteractionCell, Danger.Deadly))
		{
			reason = "NoPathToPilotConsole".Translate();
			return false;
		}
		return true;
	}
}
