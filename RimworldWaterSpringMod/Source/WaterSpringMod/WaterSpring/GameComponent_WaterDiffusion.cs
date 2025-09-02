using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WaterSpringMod.WaterSpring
{
    // This GameComponent is no longer used for diffusion
    // It's kept as a placeholder for potential future functionality
    public class GameComponent_WaterDiffusion : GameComponent
    {
        public GameComponent_WaterDiffusion(Game game) : base()
        {
            // Constructor
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            // Initialization if needed in the future
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            // No diffusion logic - it's handled by individual FlowingWater tiles
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // No data to save/load
        }
    }
}
