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
- [x] Test water spring placement
- [x] Test water generation
- [x] Test water diffusion in various terrain configurations
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

### Strategy 1: Active Tile Management System
- [ ] Create a central registry in GameComponent_WaterDiffusion to track active water tiles
- [ ] Modify FlowingWater.Tick to register with the active tile system
- [ ] Add methods to mark tiles as active when water volume changes
- [ ] Implement logic to mark neighbors as active when water spreads
- [ ] Create a processing queue that only handles active water tiles
- [ ] Add de-registration logic to remove stable tiles from active list
- [ ] Add debug visualization toggle to highlight active water tiles

### Strategy 2: Stability Detection
- [ ] Add stability counter property to FlowingWater class
- [ ] Implement logic to increment counter when no volume changes occur
- [ ] Reset counter when water volume changes
- [ ] Create tiered tick frequency based on stability level
- [ ] Implement immediate activation when adjacent tiles change
- [ ] Add threshold settings for stability tiers (config options)
- [ ] Create debug overlay to visualize stability levels

### Strategy 3: Chunked Processing
- [ ] Define chunk size constants and create chunk data structure
- [ ] Implement methods to calculate chunk ID from map coordinates
- [ ] Create chunk activation/deactivation system
- [ ] Modify water diffusion to process by chunks instead of individual tiles
- [ ] Implement staggered processing of chunks across multiple ticks
- [ ] Add logic to activate neighboring chunks when water reaches chunk borders
- [ ] Create chunk-based spatial index for faster water tile lookups
- [ ] Add debug visualization for active chunks
- [ ] Implement checkerboard update pattern (update alternating chunks on different ticks)

### Strategy 4: Multi-Phase Processing
- [ ] Create data structures to store pending water transfers
- [ ] Split water diffusion into collection, calculation, and application phases
- [ ] Implement collection phase to gather all potential water movements
- [ ] Create calculation phase to determine final volumes after all transfers
- [ ] Add application phase to apply all changes at once
- [ ] Optimize by batching similar operations together
- [ ] Add concurrency protection for multi-phase processing
- [ ] Implement fallback mechanism for very large water systems
- [ ] Add maximum spread limit per tick to prevent performance spikes during floods

### Strategy 5: Update Frequency Optimization
- [ ] Implement configurable global update frequency (process water every N ticks)
- [ ] Create dynamic update frequency system based on current TPS
- [ ] Use TickRare() and TickLong() for different water processes (diffusion vs. evaporation)
- [ ] Add throttling system that automatically reduces update frequency when water entities exceed threshold
- [ ] Implement visual vs. simulation layer separation (full simulation for nearby water, simplified for distant)
- [ ] Create distance-based update frequency (update nearby water more frequently than distant water)

### Strategy 6: Data Structure Optimization
- [ ] Replace water entity collections with flat arrays for better cache locality
- [ ] Implement a custom water grid using a 1D array (x + z * width indexing)
- [ ] Precompute and cache neighbor offsets to avoid repeated calculations
- [ ] Ensure all water calculations use integer math instead of floating point
- [ ] Use bitwise operations for level calculations where applicable
- [ ] Create efficient lookup tables for common calculations
- [ ] Optimize memory usage with struct-based water data instead of class references

### General Performance Improvements
- [ ] Implement object pooling for frequently created collections
- [ ] Add configurable processing limits per tick
- [ ] Create emergency throttling system for extreme water volumes
- [ ] Add performance monitoring and reporting tools
- [ ] Implement adaptive performance scaling based on current FPS
- [ ] Create detailed debug logging options for performance analysis
