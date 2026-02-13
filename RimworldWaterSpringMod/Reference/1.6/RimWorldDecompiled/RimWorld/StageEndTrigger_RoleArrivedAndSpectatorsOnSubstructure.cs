using Verse;

namespace RimWorld;

public class StageEndTrigger_RoleArrivedAndSpectatorsOnSubstructure : StageEndTrigger_RolesArrived
{
	protected override bool ArrivedCheck(string r, LordJob_Ritual ritual)
	{
		if (!base.ArrivedCheck(r, ritual))
		{
			return false;
		}
		Building_GravEngine building_GravEngine = ritual.selectedTarget.Thing?.TryGetComp<CompPilotConsole>()?.engine;
		if (building_GravEngine == null)
		{
			Log.Error("Engine could not be found in ritual end trigger");
			return true;
		}
		foreach (Pawn item in ritual.assignments.SpectatorsForReading)
		{
			if (!building_GravEngine.ValidSubstructure.Contains(item.Position))
			{
				return false;
			}
		}
		return true;
	}
}
