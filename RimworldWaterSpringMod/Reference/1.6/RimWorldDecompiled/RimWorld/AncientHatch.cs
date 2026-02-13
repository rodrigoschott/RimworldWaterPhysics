using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.AI;

namespace RimWorld;

[StaticConstructorOnStartup]
public class AncientHatch : MapPortal
{
	public TileMutatorWorker_Stockpile.StockpileType stockpileType;

	public LayoutDef layout;

	private bool isSealed;

	private CompHackable hackableInt;

	private GraphicData openGraphicData;

	public static CachedTexture SealHatchIcon = new CachedTexture("UI/Commands/SealHatch");

	private const string OpenTexturePath = "Things/Building/AncientHatch/AncientHatch_Open";

	private CompHackable Hackable => hackableInt ?? (hackableInt = GetComp<CompHackable>());

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref stockpileType, "stockpileType", TileMutatorWorker_Stockpile.StockpileType.Medicine);
		Scribe_Defs.Look(ref layout, "layout");
		Scribe_Values.Look(ref isSealed, "isSealed", defaultValue: false);
	}

	public override void SpawnSetup(Map map, bool respawningAfterLoad)
	{
		base.SpawnSetup(map, respawningAfterLoad);
		openGraphicData = new GraphicData();
		openGraphicData.CopyFrom(def.graphicData);
		openGraphicData.texPath = "Things/Building/AncientHatch/AncientHatch_Open";
	}

	public override void Print(SectionLayer layer)
	{
		if (IsEnterable(out var _))
		{
			openGraphicData.Graphic.Print(layer, this, 0f);
		}
		else
		{
			Graphic.Print(layer, this, 0f);
		}
	}

	protected override IEnumerable<GenStepWithParams> GetExtraGenSteps()
	{
		if (layout != null)
		{
			yield return new GenStepWithParams(GenStepDefOf.AncientStockpile, new GenStepParams
			{
				layout = layout
			});
		}
		else
		{
			yield return new GenStepWithParams(GenStepDefOf.AncientStockpile, default(GenStepParams));
		}
	}

	public override bool IsEnterable(out string reason)
	{
		if (!Hackable.IsHacked)
		{
			reason = "Locked".Translate();
			return false;
		}
		if (isSealed)
		{
			reason = "Sealed".Translate();
			return false;
		}
		return base.IsEnterable(out reason);
	}

	public void Seal()
	{
		if (!base.PocketMapExists)
		{
			Log.Error("Tried to seal ancient hatch but pocket map doesn't exist");
			return;
		}
		PocketMapUtility.DestroyPocketMap(pocketMap);
		DirtyMapMesh(base.Map);
		isSealed = true;
	}

	public override string GetInspectString()
	{
		StringBuilder stringBuilder = new StringBuilder(base.GetInspectString());
		if (isSealed)
		{
			stringBuilder.AppendLineIfNotEmpty();
			stringBuilder.Append("HatchSealed".Translate());
		}
		else if (Hackable.IsHacked)
		{
			stringBuilder.AppendLineIfNotEmpty();
			stringBuilder.Append("HatchUnlocked".Translate());
		}
		return stringBuilder.ToString();
	}

	public override IEnumerable<Gizmo> GetGizmos()
	{
		foreach (Gizmo gizmo in base.GetGizmos())
		{
			yield return gizmo;
		}
		if (!base.PocketMapExists)
		{
			yield break;
		}
		yield return new Command_Action
		{
			defaultLabel = "SealHatch".Translate(),
			defaultDesc = "SealHatchDesc".Translate(),
			icon = SealHatchIcon.Texture,
			action = delegate
			{
				Find.Targeter.BeginTargeting(TargetingParameters.ForColonist(), delegate(LocalTargetInfo target)
				{
					string text = "";
					List<Pawn> list = new List<Pawn>();
					List<Pawn> list2 = new List<Pawn>();
					foreach (Pawn allPawn in base.PocketMap.mapPawns.AllPawns)
					{
						if (allPawn.Faction == Faction.OfPlayer)
						{
							if (allPawn.RaceProps.Humanlike)
							{
								list.Add(allPawn);
							}
							else
							{
								list2.Add(allPawn);
							}
						}
					}
					if (list.Count > 0)
					{
						text = text + "\n\n" + ("GravEngineWarning".Translate() + ": ").Colorize(ColorLibrary.RedReadable) + "PeopleWillBeLeftBehind".Translate().Resolve() + ":\n" + list.Select((Pawn p) => p.NameFullColored.Resolve()).ToLineList("  - ", capitalizeItems: true);
					}
					if (list2.Count > 0)
					{
						text = text + "\n\n" + ("GravEngineWarning".Translate() + ": ").Colorize(ColorLibrary.RedReadable) + "AnimalsWillBeLeftBehind".Translate().Resolve() + ":\n" + list2.Select((Pawn p) => p.NameFullColored.Resolve()).ToLineList("  - ", capitalizeItems: true);
					}
					Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmSealHatch".Translate(text), delegate
					{
						target.Pawn?.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Seal, this), JobTag.Misc);
					}));
				}, delegate(LocalTargetInfo target)
				{
					Pawn pawn2 = target.Pawn;
					if (pawn2 != null && pawn2.IsColonistPlayerControlled)
					{
						GenDraw.DrawTargetHighlight(target);
					}
				}, (LocalTargetInfo target) => ValidateSealer(target).Accepted, null, null, SealHatchIcon.Texture, playSoundOnAction: true, delegate(LocalTargetInfo target)
				{
					AcceptanceReport acceptanceReport = ValidateSealer(target);
					Pawn pawn = target.Pawn;
					if (pawn != null && pawn.IsColonistPlayerControlled && !acceptanceReport.Accepted)
					{
						if (!acceptanceReport.Reason.NullOrEmpty())
						{
							Widgets.MouseAttachedLabel(("CannotChooseSealer".Translate() + ": " + acceptanceReport.Reason.CapitalizeFirst()).Colorize(ColorLibrary.RedReadable));
						}
						else
						{
							Widgets.MouseAttachedLabel("CannotChooseSealer".Translate());
						}
					}
				});
			}
		};
	}

	private AcceptanceReport ValidateSealer(LocalTargetInfo target)
	{
		if (!(target.Thing is Pawn pawn))
		{
			return false;
		}
		if (!pawn.CanReach(this, PathEndMode.Touch, Danger.Deadly))
		{
			return "NoPath".Translate();
		}
		if (pawn.Downed)
		{
			return "DownedLower".Translate();
		}
		return true;
	}
}
