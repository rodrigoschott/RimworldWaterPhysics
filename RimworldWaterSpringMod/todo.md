# RimWorld Water Spring Mod - Todo List

## Setup and Structure
- [x] Create basic mod folder structure (already done)
- [x] Write `About.xml` with mod description and metadata
- [x] Create a simple Preview.png for the mod

## XML Definitions
- [x] Create `Buildings_WaterSpring.xml` to define the Water Spring building
- [x] Create `Items_Water.xml` to define the Flowing Water as an item/thing
- [x] Create `Thoughts_Water.xml` for any mood effects related to water (optional)

## Translations
- [x] Create English translations in `Languages/English/DefInjected/ThingDef/Buildings_WaterSpring.xml`
- [x] Create English translations in `Languages/English/DefInjected/ThoughtDef/Thoughts_Water.xml`
- [x] Create English translations in `Languages/English/Keyed/WaterSpringText.xml` for any UI text

## C# Code Implementation
- [x] Create the Visual Studio solution and project files
- [x] Create `Building_WaterSpring.cs` to handle the Water Spring building behavior
  - [x] Implement spawning of Flowing Water at configurable intervals
- [x] Create `FlowingWater.cs` to handle the Flowing Water item/thing behavior
- [x] Create `WaterDiffusionManager.cs` to handle the water diffusion system
  - [x] Implement DwarfFortress-style diffusion algorithm
  - [x] Add logic to check neighboring tiles
  - [x] Add logic to transfer water between tiles
  - [x] Add overflow handling with cap at 7
- [x] Create `WaterSpringModSettings.cs` for mod configuration options
  - [x] Add configurable timer for water spring production
  - [x] Add configurable timer for diffusion checks

## Graphics
- [x] Create `WaterSpring.png` texture for the building
- [x] Create `FlowingWater.png` texture for the water item/thing

## Testing and Debugging
- [x] Fix XML structure errors in About.xml
- [x] Fix class naming conflict (renamed WaterSpringMod to WaterSpringModMain)
- [x] Fix XML errors in ThoughtDefs (wrong root element, missing stages)
- [x] Fix WaterSpring building (added mass for haulable thing)
- [x] Fix FlowingWater class (changed to inherit from ThingWithComps)
- [x] Fix FlowingWater XML definition (updated altitude layer, selectability, hit points)
- [x] Add debug logging to diagnose water spawning issues
- [x] Decrease water spawn interval for faster testing
- [x] Fix invalid AltitudeLayer "FloorOverlay" (changed to "FloorCoverings")
- [x] Fix translation errors in ThoughtDef language files
- [x] Fix FlowingWater tradeability (set to None to match destroyOnDrop)
- [x] Fix duplicate closing tag in Items_Water.xml
- [x] Enhance logging and debugging with Log.Error for better visibility
- [x] Make FlowingWater selectable again to aid debugging
- [x] Add exception handling to water spawning
- [x] Add static constructors and initialization logging
- [x] Add TickLong method to Building_WaterSpring for more reliable ticking
- [x] Improve water spawning with direct construction and detailed logging
- [x] Fix FlowingWater definition (changed altitude layer to Floor, added proper categories)
- [x] Fix water diffusion to properly spread from source
- [x] Implement proper cell prioritization (empty cells first, then lowest volume)
- [x] Add random selection among equal-value cells to eliminate directional bias
- [ ] Test water spring placement
- [ ] Test water generation
- [ ] Test water diffusion in various terrain configurations
- [ ] Test performance with multiple water springs

## Deployment
- [x] Build the mod
- [ ] Test in-game
- [x] Create README.md with usage instructions
- [ ] Package for distribution
- [ ] (Optional) Upload to Steam Workshop

## Potential Enhancements (Future)
- [ ] Add pressure mechanics
- [ ] Add temperature effects (freezing/evaporation)
- [ ] Add interaction with RimWorld's existing water system
- [ ] Add visual effects for flowing water
- [ ] Add water-related events (floods, etc.)

## Performance Optimization
- [ ] Implement spatial partitioning for water entities to reduce search time
- [ ] Add dynamic tick rate adjustment based on water activity (less frequent checks for stable water)
- [ ] Implement "chunks" or regions for diffusion calculations to avoid processing the entire map
- [ ] Create buffer zones around water to prevent unnecessary checks in distant areas
- [ ] Add dormancy system for inactive water tiles to reduce CPU usage
- [ ] Implement volume-based diffusion priority (higher volumes process first)
- [ ] Optimize memory usage with pooled lists/arrays for temporary calculations
- [ ] Implement multi-threading for water diffusion calculations in separate map regions
- [ ] Add configurable water entity limit per map with overflow handling
- [ ] Implement LOD (Level of Detail) system for water simulation based on camera distance
- [ ] Add batched rendering and graphical updates to reduce draw calls
