using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WaterSpringMod.WaterSpring
{
    // Lightweight map component to render the active water overlay every frame
    public class ActiveWaterOverlayMapComponent : MapComponent
    {
        private readonly List<IntVec3> _scratch = new List<IntVec3>(2048);

        public ActiveWaterOverlayMapComponent(Map map) : base(map) { }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            if (Event.current.type != EventType.Repaint) return;

            var comp = Current.Game?.GetComponent<GameComponent_WaterDiffusion>();
            if (comp == null || !comp.ShowActiveWaterDebug) return;

            // Draw tiles in dirty chunks for this map
            var si = comp.GetSpatialIndex(map);
            if (si == null) return;

            _scratch.Clear();
            foreach (var chunk in si.GetDirtyChunks())
            {
                var tiles = si.GetWaterTilesInChunk(chunk);
                if (tiles != null) _scratch.AddRange(tiles);
            }
            if (_scratch.Count == 0) return;

            for (int i = 0; i < _scratch.Count; i++)
            {
                IntVec3 pos = _scratch[i];
                Vector3 drawPos = pos.ToVector3Shifted();
                drawPos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                GenDraw.DrawCircleOutline(drawPos, 0.5f, SimpleColor.Red);
            }

            // Draw dirty chunk outlines
            foreach (var chunk in si.GetDirtyChunks())
            {
                IntVec3 min = chunk.MinCorner;
                IntVec3 max = chunk.MaxCorner;
                float y = AltitudeLayer.MetaOverlays.AltitudeFor();
                Vector3 v1 = new Vector3(min.x, y, min.z);
                Vector3 v2 = new Vector3(max.x + 1, y, min.z);
                Vector3 v3 = new Vector3(max.x + 1, y, max.z + 1);
                Vector3 v4 = new Vector3(min.x, y, max.z + 1);
                GenDraw.DrawLineBetween(v1, v2, SimpleColor.Green);
                GenDraw.DrawLineBetween(v2, v3, SimpleColor.Green);
                GenDraw.DrawLineBetween(v3, v4, SimpleColor.Green);
                GenDraw.DrawLineBetween(v4, v1, SimpleColor.Green);
            }
        }
    }
}
