using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace WaterSpringMod.WaterSpring
{
    // Generic vertical portal bridge for WS_Hole. No dependency on other mods.
    internal static class VerticalPortalBridge
    {
        private static ThingDef _holeDef;

        private class MapCacheEntry { public Map lower; public int expireTick; }
        private static readonly Dictionary<int, MapCacheEntry> _lowerMapCache = new();
    // Inverse index: lower map id -> set of known upper maps stacked above it
    private static readonly Dictionary<int, HashSet<Map>> _uppersByLower = new();
    // Optional: remember last lower chosen for each upper to reduce duplicate set entries
    private static readonly Dictionary<int, int> _lastLowerOfUpper = new();

        // MultiFloors markers (string-only, no hard dependency)
        private const string CompUpper = "MultiFloors.MF_UpperLevelMapComp";
        private const string CompBasement = "MultiFloors.MF_BasementMapComp";
        private const string BiomeUpper = "MF_UpperLevelBiome";
        private const string BiomeBasement = "MF_BasementBiome";
        private const string BiomeSpace = "Orbit"; // Odyssey space biome used by MF space upper level

        private enum MapLevel
        {
            Basement = 0,
            Surface = 1,
            Upper = 2,
            SpaceUpper = 3,
        }

        private static ThingDef HoleDef
        {
            get
            {
                if (_holeDef == null)
                {
                    _holeDef = DefDatabase<ThingDef>.GetNamedSilentFail("WS_Hole");
                }
                return _holeDef;
            }
        }

        public static bool IsHoleAt(Map map, IntVec3 cell)
        {
            var settings = LoadedModManager.GetMod<WaterSpringModMain>()?.settings;
            bool debug = settings?.debugModeEnabled ?? false;
            if (map == null)
            {
                if (debug) WaterSpringLogger.LogDebug("[Portal] IsHoleAt: map is null");
                return false;
            }
            if (!cell.InBounds(map))
            {
                if (debug) WaterSpringLogger.LogDebug($"[Portal] IsHoleAt: cell {cell} out of bounds");
                return false;
            }
            
            // NEW: Check for MultiFloors void terrain FIRST (if enabled and available)
            if (MultiFloorsIntegration.IsAvailable && settings != null && settings.useMultiFloorsVoidTerrain)
            {
                // Check if this is void terrain on an upper level
                if (MultiFloorsIntegration.IsVoidTerrain(map, cell))
                {
                    if (debug) WaterSpringLogger.LogDebug($"[Portal] IsHoleAt: {cell} is MF void terrain");
                    return true;
                }
            }
            
            var def = HoleDef;
            if (def == null)
            {
                if (debug) WaterSpringLogger.LogDebug("[Portal] IsHoleAt: HoleDef WS_Hole not loaded");
                return false;
            }
            // Prefer edifice when present
            var edifice = cell.GetEdifice(map);
            if (edifice != null)
            {
                bool ematch = edifice.def == def;
                if (debug) WaterSpringLogger.LogDebug($"[Portal] IsHoleAt: edifice={edifice.def?.defName}, expected=WS_Hole, match={ematch}");
                if (ematch) return true;
            }
            // Fallback: scan all things (covers non-edifice buildings, frames, blueprints)
            var list = map.thingGrid.ThingsListAtFast(cell);
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    if (t == null) continue;
                    // Direct def match
                    if (t.def == def)
                    {
                        if (debug) WaterSpringLogger.LogDebug($"[Portal] IsHoleAt: found WS_Hole thing in list ({t.GetType().Name}) at {cell}");
                        return true;
                    }
                    // Frame/blueprint building target
                    var entBuild = t.def?.entityDefToBuild as ThingDef;
                    if (entBuild != null && entBuild == def)
                    {
                        if (debug) WaterSpringLogger.LogDebug($"[Portal] IsHoleAt: found WS_Hole under construction ({t.def?.defName}) at {cell}");
                        return true;
                    }
                }
                if (debug)
                {
                    // Brief list of things for diagnostics
                    try
                    {
                        System.Text.StringBuilder sb = new System.Text.StringBuilder(64);
                        foreach (var t in list) { if (sb.Length>0) sb.Append(", "); sb.Append(t?.def?.defName); }
                        WaterSpringLogger.LogDebug($"[Portal] IsHoleAt: no hole at {cell}; things: [{sb}]");
                    }
                    catch { }
                }
            }
            else if (debug)
            {
                WaterSpringLogger.LogDebug($"[Portal] IsHoleAt: no things list at {cell}");
            }
            return false;
        }

        public static bool TryGetLowerMap(Map current, out Map lower)
        {
            lower = null;
            var settings = LoadedModManager.GetMod<WaterSpringModMain>()?.settings;
            bool debug = settings?.debugModeEnabled ?? false;
            if (current == null)
            {
                if (debug) WaterSpringLogger.LogDebug("[Portal] TryGetLowerMap: current map is null");
                return false;
            }
            
            // NEW: Try MultiFloors integration first (direct API, no reflection)
            if (MultiFloorsIntegration.IsAvailable)
            {
                if (MultiFloorsIntegration.TryGetLowerMap(current, out lower))
                {
                    if (debug) WaterSpringLogger.LogDebug($"[Portal] TryGetLowerMap: MF direct API -> map #{lower.uniqueID}");
                    return true;
                }
                // MF is available but no lower map found
                if (debug) WaterSpringLogger.LogDebug("[Portal] TryGetLowerMap: MF API found no lower map");
                return false;
            }
            
            // FALLBACK: Use reflection-based detection (original code)
            int now = Find.TickManager?.TicksGame ?? 0;
            // Fixed TTL to avoid hot scans but adapt if maps appear mid-game
            const int ttl = 1200; // 20 seconds at 60 tps

            if (_lowerMapCache.TryGetValue(current.uniqueID, out var cached))
            {
                // Positive cache hit
                if (cached.lower != null && cached.expireTick > now && (Find.Maps == null || Find.Maps.Contains(cached.lower)))
                {
                    if (debug) WaterSpringLogger.LogDebug($"[Portal] TryGetLowerMap: cache hit -> map #{cached.lower.uniqueID} (expires {cached.expireTick})");
                    lower = cached.lower;
                    return true;
                }
                // Negative cache within TTL -> skip expensive scans this tick
                if (cached.lower == null && cached.expireTick > now)
                {
                    if (debug) WaterSpringLogger.LogDebug("[Portal] TryGetLowerMap: negative cache hit; skipping scan");
                    return false;
                }
            }

            // Reflection pass: scan MapComps for fields/properties pointing to another Map
            try
            {
                var comps = current.components;
                if (comps != null)
                {
                    if (debug) WaterSpringLogger.LogDebug($"[Portal] TryGetLowerMap: scanning {comps.Count} map comps on map #{current.uniqueID}");
                    for (int ci = 0; ci < comps.Count; ci++)
                    {
                        var comp = comps[ci];
                        if (comp == null) continue;
                        var t = comp.GetType();
                        // fields
                        foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (f.FieldType == typeof(Map))
                            {
                                var mv = f.GetValue(comp) as Map;
                                if (IsCandidateLowerMap(current, mv))
                                {
                                    lower = mv;
                                    _lowerMapCache[current.uniqueID] = new MapCacheEntry { lower = lower, expireTick = now + ttl };
                                    if (debug) WaterSpringLogger.LogDebug($"[Portal] TryGetLowerMap: comp field {t.Name}.{f.Name} -> map #{lower.uniqueID}");
                                    return true;
                                }
                            }
                        }
                        // properties
                        foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (p.PropertyType == typeof(Map) && p.CanRead)
                            {
                                Map mv = null;
                                try { mv = p.GetValue(comp, null) as Map; } catch { }
                                if (IsCandidateLowerMap(current, mv))
                                {
                                    lower = mv;
                                    _lowerMapCache[current.uniqueID] = new MapCacheEntry { lower = lower, expireTick = now + ttl };
                                    if (debug) WaterSpringLogger.LogDebug($"[Portal] TryGetLowerMap: comp prop {t.Name}.{p.Name} -> map #{lower.uniqueID}");
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // Fallback: choose another loaded map with MF-aware heuristics
            try
            {
                var maps = Find.Maps;
                if (maps != null && maps.Count > 1)
                {
                    if (debug) WaterSpringLogger.LogDebug($"[Portal] TryGetLowerMap: fallback scanning {maps.Count} maps");

                    // Build candidate sets with level awareness
                    List<Map> sameSizeSameTile = null;
                    List<Map> sameSize = null;
                    List<Map> others = null;
                    List<Map> sameTileAll = null;
                    int tile = current.Tile;
                    MapLevel currentLevel = DetectLevel(current);
                    string currName = SafeName(current);
                    if (debug) WaterSpringLogger.LogDebug($"[Portal] TryGetLowerMap: current map #{current.uniqueID} '{currName}' tile {tile} size {current.Size} level {currentLevel}");

                    for (int i = 0; i < maps.Count; i++)
                    {
                        var m = maps[i];
                        if (m == null || m == current) continue;
                        bool sizeMatch = m.Size.x == current.Size.x && m.Size.z == current.Size.z;
                        MapLevel lvl = DetectLevel(m);
                        if (debug)
                        {
                            WaterSpringLogger.LogDebug($"[Portal] TryGetLowerMap: candidate map #{m.uniqueID} '{SafeName(m)}' size {m.Size} tile {m.Tile} level {lvl} sizeMatch={sizeMatch}");
                        }

                        if (m.Tile == tile) { (sameTileAll ??= new List<Map>()).Add(m); }
                        if (sizeMatch && m.Tile == tile) { (sameSizeSameTile ??= new List<Map>()).Add(m); }
                        else if (sizeMatch) { (sameSize ??= new List<Map>()).Add(m); }
                        else { (others ??= new List<Map>()).Add(m); }
                    }

                    Map pick = null;

                    // Prefer immediate neighbor on same tile: same level (if present), else closest lower level; within group prefer same size and uniqueID just below current
                    if (sameTileAll != null && sameTileAll.Count > 0)
                    {
                        // Sort by uniqueID for deterministic selection
                        sameTileAll.Sort((a,b) => a.uniqueID.CompareTo(b.uniqueID));
                        // Find same-level neighbor below current
                        Map sameLevelPick = null;
                        for (int i = sameTileAll.Count - 1; i >= 0; i--)
                        {
                            var m = sameTileAll[i];
                            if (m.uniqueID >= current.uniqueID) continue;
                            if (DetectLevel(m) == currentLevel)
                            {
                                if (m.Size.x == current.Size.x && m.Size.z == current.Size.z)
                                {
                                    sameLevelPick = m; // same-size wins
                                    break;
                                }
                                sameLevelPick ??= m; // keep as fallback if no same-size
                            }
                        }
                        if (sameLevelPick != null)
                        {
                            pick = sameLevelPick;
                            if (debug) WaterSpringLogger.LogDebug($"[Portal] TryGetLowerMap: same-tile immediate same-level chosen #{pick.uniqueID} '{SafeName(pick)}' size {pick.Size}");
                        }
                        else
                        {
                            // Choose closest lower level present on same tile
                            bool found = false;
                            for (int lvlVal = (int)currentLevel - 1; lvlVal >= (int)MapLevel.Basement && !found; lvlVal--)
                            {
                                var targetLvl = (MapLevel)lvlVal;
                                Map lvlPick = null;
                                for (int i = sameTileAll.Count - 1; i >= 0; i--)
                                {
                                    var m = sameTileAll[i];
                                    if (m.uniqueID >= current.uniqueID) continue;
                                    if (DetectLevel(m) != targetLvl) continue;
                                    if (m.Size.x == current.Size.x && m.Size.z == current.Size.z)
                                    {
                                        lvlPick = m; // same-size wins
                                        break;
                                    }
                                    lvlPick ??= m;
                                }
                                if (lvlPick != null)
                                {
                                    pick = lvlPick;
                                    found = true;
                                    if (debug) WaterSpringLogger.LogDebug($"[Portal] TryGetLowerMap: same-tile immediate lower level {targetLvl} chosen #{pick.uniqueID} '{SafeName(pick)}' size {pick.Size}");
                                }
                            }
                        }
                    }

                    // Helper to prefer candidates that are a lower level than current (ordinal smaller)
                    Func<List<Map>, Map> preferLowerLevel = (lst) =>
                    {
                        if (lst == null || lst.Count == 0) return null;
                        // First pass: same tile list already filtered if needed; sort by uniqueID for stability
                        lst.Sort((a,b) => a.uniqueID.CompareTo(b.uniqueID));
                        // Choose the closest lower level (max lvl where lvl < currentLevel)
                        bool anyLower = false;
                        MapLevel maxLower = MapLevel.Basement;
                        for (int i = 0; i < lst.Count; i++)
                        {
                            var lvl = DetectLevel(lst[i]);
                            if ((int)lvl < (int)currentLevel)
                            {
                                if (!anyLower || (int)lvl > (int)maxLower)
                                {
                                    maxLower = lvl;
                                    anyLower = true;
                                }
                            }
                        }
                        if (anyLower)
                        {
                            // From maps with lvl == maxLower, pick the one with uniqueID just below current if possible
                            Map candidate = null;
                            for (int i = 0; i < lst.Count; i++)
                            {
                                var m = lst[i];
                                if (DetectLevel(m) == maxLower)
                                {
                                    if (candidate == null) candidate = m;
                                    if (m.uniqueID < current.uniqueID) { candidate = m; break; }
                                }
                            }
                            if (debug) WaterSpringLogger.LogDebug($"[Portal] TryGetLowerMap: closest lower target level {maxLower}");
                            return candidate;
                        }
                        // If no lower-level signals, fallback within list: prefer lower uniqueID than current
                        for (int i = 0; i < lst.Count; i++)
                        {
                            if (lst[i].uniqueID < current.uniqueID) return lst[i];
                        }
                        return lst[0];
                    };

                    // Prefer same size and same tile
                    if (pick == null && sameSizeSameTile != null && sameSizeSameTile.Count > 0)
                    {
                        pick = preferLowerLevel(sameSizeSameTile);
                    }
                    else if (pick == null && sameSize != null && sameSize.Count > 0)
                    {
                        pick = preferLowerLevel(sameSize);
                    }
                    else if (pick == null && others != null && others.Count > 0)
                    {
                        pick = preferLowerLevel(others);
                    }

                    if (pick != null)
                    {
                        if (debug)
                        {
                            WaterSpringLogger.LogDebug($"[Portal] TryGetLowerMap: fallback picked map #{pick.uniqueID} '{SafeName(pick)}' size {pick.Size} tile {pick.Tile} level {DetectLevel(pick)}");
                        }
                        lower = pick;
                        _lowerMapCache[current.uniqueID] = new MapCacheEntry { lower = lower, expireTick = now + ttl };
                        // Maintain inverse index for efficient upward activation
                        try { RegisterUpperForLower(current, lower, debug); } catch { }
                        return true;
                    }
                }
            }
            catch { }

            // Cache miss with short TTL to avoid hot loop
            if (debug) WaterSpringLogger.LogDebug("[Portal] TryGetLowerMap: not found; caching null with short TTL");
            _lowerMapCache[current.uniqueID] = new MapCacheEntry { lower = null, expireTick = now + Math.Min(ttl, 300) };
            return false;
        }

        private static bool IsCandidateLowerMap(Map self, Map other)
        {
            if (other == null || other == self) return false;
            if (Find.Maps != null && !Find.Maps.Contains(other)) return false;
            // Prefer maps with same size (x,z)
            return other.Size.x == self.Size.x && other.Size.z == self.Size.z;
        }

        // Determine a map's level using MF hints and biome names
        private static MapLevel DetectLevel(Map map)
        {
            if (map == null) return MapLevel.Surface;
            // 1) Map components
            try
            {
                var comps = map.components;
                if (comps != null)
                {
                    for (int i = 0; i < comps.Count; i++)
                    {
                        var t = comps[i]?.GetType();
                        var name = t?.FullName;
                        if (name == CompUpper) return MapLevel.Upper;
                        if (name == CompBasement) return MapLevel.Basement;
                    }
                }
            }
            catch { }
            // 2) Biome
            try
            {
                var biome = map.Biome;
                var defName = biome?.defName;
                if (defName == BiomeUpper) return MapLevel.Upper;
                if (defName == BiomeBasement) return MapLevel.Basement;
                if (defName == BiomeSpace) return MapLevel.SpaceUpper;
            }
            catch { }
            // Default: treat unknown as surface
            return MapLevel.Surface;
        }

        private static string SafeName(Map map)
        {
            try
            {
                string parent = map?.Parent?.LabelCap ?? map?.Parent?.Label ?? "";
                if (!string.IsNullOrWhiteSpace(parent)) return parent;
            }
            catch { }
            try
            {
                var biome = map?.Biome;
                if (biome != null) return biome.LabelCap;
            }
            catch { }
            return $"Map#{map?.uniqueID ?? -1}";
        }

        // Record that 'upper' uses 'lower' as its downward link
        private static void RegisterUpperForLower(Map upper, Map lower, bool debug)
        {
            if (upper == null || lower == null) return;
            // Track last mapping and avoid redundant work
            if (_lastLowerOfUpper.TryGetValue(upper.uniqueID, out int last) && last == lower.uniqueID)
            {
                return;
            }
            _lastLowerOfUpper[upper.uniqueID] = lower.uniqueID;
            if (!_uppersByLower.TryGetValue(lower.uniqueID, out var set))
            {
                set = new HashSet<Map>();
                _uppersByLower[lower.uniqueID] = set;
            }
            if (!set.Contains(upper))
            {
                set.Add(upper);
                if (debug)
                {
                    WaterSpringLogger.LogDebug($"[Portal] Link index: lower #{lower.uniqueID} <- upper #{upper.uniqueID}");
                }
            }
        }

        // Wake corresponding cells on all known upper maps when a lower-map cell at 'pos' changes.
        public static void PropagateVerticalActivationIfHole(Map map, IntVec3 pos)
        {
            var settings = LoadedModManager.GetMod<WaterSpringModMain>()?.settings;
            bool debug = settings?.debugModeEnabled ?? false;
            if (map == null) return;
            
            var gameComp = Current.Game?.GetComponent<GameComponent_WaterDiffusion>();
            
            // NEW: Try MultiFloors VerticallyOutwardLevels first
            if (MultiFloorsIntegration.IsAvailable)
            {
                var outwardLevels = MultiFloorsIntegration.GetVerticallyOutwardLevels(map);
                if (outwardLevels != null && outwardLevels.Count > 0)
                {
                    int currentLevel = MultiFloorsIntegration.GetLevel(map);
                    int maxDepth = Math.Max(1, settings?.maxVerticalPropagationDepth ?? 3);
                    int depth = 0;
                    
                    // Iterate upward levels (vertically outward)
                    foreach (var (level, upperMap) in outwardLevels)
                    {
                        if (level <= currentLevel) continue; // Only upward propagation
                        if (upperMap == null) continue;
                        if (!pos.InBounds(upperMap)) continue;
                        
                        // Check depth limit
                        depth++;
                        if (depth > maxDepth) break;
                        
                        // Only wake if upper map has hole/void at this position
                        if (!IsHoleAt(upperMap, pos)) continue;
                        
                        // Wake water on upper level
                        var w = upperMap.thingGrid.ThingAt<FlowingWater>(pos);
                        if (w != null)
                        {
                            w.ClearStatic();
                        }

                        gameComp?.MarkChunkDirtyAt(upperMap, pos);
                        // Also dirty cardinal neighbors
                        foreach (IntVec3 dir in GenAdj.CardinalDirections)
                        {
                            IntVec3 adj = pos + dir;
                            if (!adj.InBounds(upperMap)) continue;
                            gameComp?.MarkChunkDirtyAt(upperMap, adj);
                            var adjW = upperMap.thingGrid.ThingAt<FlowingWater>(adj);
                            if (adjW != null) adjW.ClearStatic();
                        }
                        
                        if (debug)
                        {
                            WaterSpringLogger.LogDebug($"[Portal] MF vertical reactivation: woke upper #{upperMap.uniqueID} level {level} at {pos}");
                        }
                    }
                    return; // MF path complete
                }
            }
            
            // FALLBACK: Use reflection-based _uppersByLower index
            if (!_uppersByLower.TryGetValue(map.uniqueID, out var uppers) || uppers == null || uppers.Count == 0)
            {
                // Lazy discovery: find uppers on the same tile that link down to this map and have a hole at this pos
                try
                {
                    var maps = Find.Maps;
                    if (maps != null)
                    {
                        for (int i = 0; i < maps.Count; i++)
                        {
                            var up = maps[i];
                            if (up == null || up == map) continue;
                            if (up.Tile != map.Tile) continue;
                            if (!pos.InBounds(up)) continue;
                            if (!IsHoleAt(up, pos)) continue;
                            // Prefer genuine upper levels relative to current
                            if ((int)DetectLevel(up) <= (int)DetectLevel(map)) continue;
                            RegisterUpperForLower(up, map, debug);
                        }
                    }
                }
                catch { }
            }
            if (!_uppersByLower.TryGetValue(map.uniqueID, out uppers) || uppers == null || uppers.Count == 0) return;

            foreach (var upper in uppers)
            {
                if (upper == null || upper == map) continue;
                // Only wake if the upper map also has a hole at this position
                if (!pos.InBounds(upper) || !IsCellPassableForUpper(upper, pos) || !IsHoleAt(upper, pos)) continue;
                var w = upper.thingGrid.ThingAt<FlowingWater>(pos);
                if (w != null)
                {
                    w.ClearStatic();
                }
                // Mark chunk dirty + wake cardinal neighbors
                gameComp?.MarkChunkDirtyAt(upper, pos);
                foreach (IntVec3 dir in GenAdj.CardinalDirections)
                {
                    IntVec3 adj = pos + dir;
                    if (!adj.InBounds(upper)) continue;
                    gameComp?.MarkChunkDirtyAt(upper, adj);
                    var adjW = upper.thingGrid.ThingAt<FlowingWater>(adj);
                    if (adjW != null) adjW.ClearStatic();
                }
                if (debug)
                {
                    WaterSpringLogger.LogDebug($"[Portal] Vertical reactivation: woke upper #{upper.uniqueID} at {pos} due to change on lower #{map.uniqueID}");
                }
            }
        }

        // Quick passability check for the upper map cell
        private static bool IsCellPassableForUpper(Map map, IntVec3 cell)
        {
            if (cell.InBounds(map) && cell.Walkable(map)) return true;
            try
            {
                var t = map.terrainGrid?.TerrainAt(cell);
                if (t != null && (t == RimWorld.TerrainDefOf.WaterShallow || t == RimWorld.TerrainDefOf.WaterDeep)) return true;
            }
            catch { }
            return true; // default permissive; water logic will filter further
        }

        // Convenience: check center and its 4 cardinals; propagate for whichever are hole cells on this map
        public static void PropagateVerticalActivationForCellAndCardinals(Map map, IntVec3 center)
        {
            PropagateVerticalActivationIfHole(map, center);
            foreach (var d in GenAdj.CardinalDirections)
            {
                var p = center + d;
                if (!p.InBounds(map)) continue;
                PropagateVerticalActivationIfHole(map, p);
            }
        }
    }
}
