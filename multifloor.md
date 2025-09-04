How the MultiFloors mod works (from the Defs)
Z-levels via separate “upper/basement/space” maps

MapGeneratorDefs create pocket maps for levels: basements (underground), upper floors, and optionally space (Odyssey).
Each level has its own BiomeDef and GenSteps; “void” terrains mark unsupported/transparent areas on upper levels.
A central settings Def (MF_UpperLevelSettings) maps planet layers to which MapGenerator, default/deck/void terrains are used; also defines “transparentTerrains” (renders below).
Movement between levels via stairs and elevators

Stairs/elevators are pairs of entrance/exit ThingDefs linked by a StairsModExtension:
Entrance: isUpstairs true/false and connectedToExitDef.
Exit: references upstairsEntranceDef and downstairsEntranceDef (+ rotation rules).
Pawns change level using JobDefs with MultiFloors.* JobDrivers.
WorkGivers enable cross-level hauling/rescue/fixing.
UI and control surface

Main button opens per-pawn level settings table (PawnTableDef defining columns).
Keybindings (N/M) to switch levels.
Designation category “MultiFloors” groups the buildables and special designators.
Terrain rules for upper levels

“Void” terrains (MF_SurfaceVoid, MF_SpaceVoid) are Impassable with dontRender true (and affordance Walkable so foundations can be built).
Foundations (MF_RoofedFoundation) are buildable floors marked isFoundation=true; a transparent variant allows seeing the level below.
Defs by file (type → defNames)
Designations

DesignationCategoryDef
MultiFloors
DesignationDef
MF_HaulToOtherLevelDesignation
DesignatorDropdownGroupDefs

DesignatorDropdownGroupDef
MF_SurfaceStonyRoofedFoundationDropDown
JobDefs

JobDef
MF_ChangeLevelThroughStair
MF_DeliverResourcesAcrossLevel
MF_DeliverToContainer
MF_HaulDesignatedThingToDestMap
MF_UnloadInventory
MF_CaptureEntityAcrossLevel
MF_TransferEntityAcrossLevel
MF_TakeToGround
MF_TakeKidnapeeToGround
MF_FixBrokenDownBuildingAcrossLevel
MF_FixBrokenDownBuilding
MF_RescueToOtherLevel
MF_CaptureToOtherLevel
MF_ArrestToOtherLevel
KeyBindings

KeyBindingCategoryDef
MultiFloorHotKeys
KeyBindingDef
MF_LowerLevelHotKey
MF_UpperLevelHotKey
MainButtonDef

MainButtonDef
MF_FloorSettingButton
MapGeneration

MapGeneratorDef
MF_Basement
MF_BasementWithoutCaves
MF_SurfaceUpperLevel
MF_SpaceUpperLevel (MayRequire Odyssey)
TileMutatorDef
MF_UndergroundCave
GenStepDef
MF_UndercaveRocksFromGrid
MF_UndercaveScatterRuinsSimple
MF_FogAllMap
MF_SurfaceUpperLevelTerrain
MF_SurfaceUpperLevelRock
MF_SpaceUpperLevelTerrain (MayRequire Odyssey)
BiomeDef
MF_BasementBiome
MF_UpperLevelBiome
MultiFloors.UpperLevelSettingsDef

MultiFloors.UpperLevelSettingsDef
MF_UpperLevelSettings
layers: Surface (+ Orbit if Odyssey)
settings (per layer):
mapGenerator: MF_SurfaceUpperLevel / MF_SpaceUpperLevel
defaultTerrain: MF_SurfaceStonyRoofedFoundationMarble / MF_SurfaceStonyRoofedFoundationVacstone (Vacstone referenced; its Def isn’t in these files—likely external)
deckTerrain: Substructure (from Odyssey)
voidTerrain: MF_SurfaceVoid / MF_SpaceVoid (MF_SpaceVoid referenced; its Def isn’t in these files—likely external)
transparentTerrains: MF_SurfaceVoid, MF_SpaceVoid, MF_TransparentFoundation
PawnColumns and PawnTable

PawnColumnDef
MF_LivingLevel
MF_DietLevel
MF_JoyLevel
MF_MeditateLevel
MF_CurrentLevel
MF_StayOnCurrentMap
MF_CrossLevelWorkScanningPriority
PawnTableDef
MF_FloorsTable
TerrainDefs

Abstract bases
TerrainDef (Abstract): MF_VoidTerrainBase (Parent NaturalTerrainBase)
TerrainDef (Abstract): MF_RoofedFoundationBase (Parent FloorBase)
Foundation variants
TerrainDef (Abstract): MF_SurfaceStonyRoofedFoundation
TerrainDef: MF_SurfaceStonyRoofedFoundationSandstone
TerrainDef: MF_SurfaceStonyRoofedFoundationGranite
TerrainDef: MF_SurfaceStonyRoofedFoundationLimestone
TerrainDef: MF_SurfaceStonyRoofedFoundationSlate
TerrainDef: MF_SurfaceStonyRoofedFoundationMarble
TerrainDef: MF_SurfaceWoodenRoofedFoundation
TerrainDef: MF_TransparentFoundation
Void
TerrainDef: MF_SurfaceVoid
ThingDefs_Buildings (Stairs base)

ThingDef (Abstract): MF_StairEntranceBase (thingClass MultiFloors.StairEntrance)
ThingDef (Abstract): MF_StairExitBase (thingClass MultiFloors.StairExit)
Ladders (large)

ThingDef: MF_LadderLUpA
ThingDef: MF_LadderLDownA
ThingDef: MF_LadderLExitA
Ladders (small)

ThingDef: MF_LadderSUpA
ThingDef: MF_LadderSDownA
ThingDef: MF_LadderSExitA
Handrail stairs (small)

ThingDef: MF_HandrailStairsSUpA
ThingDef: MF_HandrailStairsSUpAFlipped
ThingDef: MF_HandrailStairsSDownA
ThingDef: MF_HandrailStairsSDownAFlipped
ThingDef: MF_HandrailStairsSExitA
ThingDef: MF_HandrailStairsSExitAFlipped
Handrail stairs (medium)

ThingDef: MF_HandrailStairsMUpA
ThingDef: MF_HandrailStairsMUpAFlipped
ThingDef: MF_HandrailStairsMDownA
ThingDef: MF_HandrailStairsMDownAFlipped
ThingDef: MF_HandrailStairsMExitA
ThingDef: MF_HandrailStairsMExitAFlipped
Brick stairs (medium)

ThingDef: MF_BrickStairsMUpA
ThingDef: MF_BrickStairsMUpAFlipped
ThingDef: MF_BrickStairsMDownA
ThingDef: MF_BrickStairsMDownAFlipped
ThingDef: MF_BrickStairsMExitA
ThingDef: MF_BrickStairsMExitAFlipped
Elevators

ThingDef (Abstract base): MF_ElevatorBase
ThingDef: MF_WoodenElevatorA
ThingDef: MF_ModernElevatorA
WeatherDef

WeatherDef: MF_UpperLevelWeather
WeatherDef: MF_UndergroundWeather
WorkGiverDefs

WorkGiverDef: MF_DeliverResourcesAcrossLevel
WorkGiverDef: MF_FixBrokenDownBuildingAcrossLevel
WorkGiverDef: MF_HaulDesignatedToDestMap
WorkGiverDef: MF_RescueDownedAcrossLevel
Key mechanics/settings that matter for water Z-movement
Portals and links

Stairs/elevators are explicit entrance/exit pairs with defNames above. They’re the canonical path for cross-level movement and likely the simplest “portals” to mirror water between levels at matching cells.
Level topology and rendering

Transparent terrains: MF_SurfaceVoid, MF_TransparentFoundation (and MF_SpaceVoid) render-through to lower levels; water visibility/overlay should account for that.
Void is Impassable but has Walkable affordance so foundations can be built—don’t treat void as traversable for fluid logic unless explicitly required.
Map components/hooks (from MapGeneratorDefs)

MF_UpperLevelMapComp and MF_BasementMapComp are custom MapComponents (in the DLL) that manage upper/basement behavior; we may need to query them for level relations.
Dependencies (MayRequire Odyssey)

Some settings/terrains (MF_SpaceVoid, Substructure, MF_SpaceUpperLevel) exist only if the Odyssey DLC/mod is present; integration should check for their presence.
Assemblies referenced (implementation backing)
Reference/1.6/Assemblies
MultiFloors.dll (defines MultiFloors.* classes used by ThingDefs, JobDrivers, MapComps, PlaceWorkers).
0PrepatcherAPI.dll (infrastructure; not directly relevant to behavior).