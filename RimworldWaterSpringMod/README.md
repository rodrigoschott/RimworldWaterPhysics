# RimWorld Water Spring Mod

## Overview
The RimWorld Water Spring Mod introduces a new buildable structure called "Water Spring" to the game. This mod allows players to create a water source that generates "Flowing Water" over time, simulating a natural water flow system with basic diffusion physics.

## Features
- **Water Spring Building**: A new buildable structure that generates flowing water at configurable intervals
- **Water Diffusion System**: Water spreads to neighboring tiles following a simplified diffusion algorithm inspired by Dwarf Fortress
- **Configurable Settings**: Adjust the water generation rate and diffusion check frequency through the mod settings
- **Persistence**: Water volume persists through game saves

## How It Works
1. Build a Water Spring structure on any valid tile
2. The spring generates flowing water at its location
3. When water accumulates beyond a certain volume, it starts to spread to neighboring tiles
4. Water continues to spread following the path of least resistance
5. Each water tile can hold up to 7 units of water before overflowing

## Installation
1. Download the mod
2. Extract the contents to your RimWorld Mods folder
3. Enable the mod in the RimWorld mod manager

## Configuration
In the mod settings menu you can adjust:
- Water Spring Spawn Interval: How frequently the spring generates new water (in ticks)
- Water Diffusion Check Interval: How frequently the water diffusion system runs its calculations (in ticks)

## Technical Details
The mod uses a simplified water diffusion model:
- Water spreads from higher volume cells to lower volume cells
- Only walkable tiles can contain flowing water
- Each transfer is one volume unit at a time
- The maximum water volume per cell is capped at 7 units

## Compatibility
- Compatible with RimWorld 1.4, 1.5, and 1.6
- Requires Harmony

## Future Plans
- Add pressure mechanics
- Add temperature effects (freezing/evaporation)
- Add visual effects for different water volume levels
- Add interaction with RimWorld's existing water system
- Add water-related events (floods, etc.)

## Acknowledgments
This mod is inspired by the water systems in Dwarf Fortress and aims to bring a similar experience to RimWorld.