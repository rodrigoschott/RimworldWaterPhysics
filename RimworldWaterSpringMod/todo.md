# RimWorld Water Spring Mod - Todo List



## Performance Optimization

### Implementation Strategy and Integration Plan

Our performance optimization approach consists of six complementary strategies that build upon each other. Each strategy focuses on a different aspect of performance optimization, and they're designed to work together as an integrated system:

1. **Active Tile Management** forms the foundation by tracking which tiles need processing
2. **Stability Detection** extends this by determining how frequently each tile needs updates
3. **Chunked Processing** groups tiles into spatial regions for more efficient batch processing
4. **Multi-Phase Processing** optimizes how each batch of tiles is processed
5. **Update Frequency Optimization** controls when processing occurs
6. **Data Structure Optimization** improves the underlying memory representation

These strategies will be implemented in phases, with each phase building on the previous one while maintaining compatibility through clear interfaces and modular design.

### Strategy 1: Active Tile Management System (Foundation)
- [x] Create a central registry in GameComponent_WaterDiffusion to track active water tiles
- [x] Modify FlowingWater.Tick to register with the active tile system
- [x] Add methods to mark tiles as active when water volume changes
- [x] Implement logic to mark neighbors as active when water spreads
- [x] Create a processing queue that only handles active water tiles
- [x] Add de-registration logic to remove stable tiles from active list
- [x] Add debug visualization toggle to highlight active water tiles
- [x] Design registry with extension points for Strategy 3 (chunk-based organization)
- [x] Include hooks for Strategy 5 (frequency-based processing)

### Strategy 2: Stability Detection (Builds on Strategy 1)
- [x] Add stability counter property to FlowingWater class (extends the de-registration logic from Strategy 1)
- [x] Implement logic to increment counter when no volume changes occur
- [x] Reset counter when water volume changes
- [x] Create tiered tick frequency based on stability level
- [x] Implement immediate activation when adjacent tiles change
- [x] Add threshold settings for stability tiers (config options)
- [x] Create debug overlay to visualize stability levels
- [x] Integrate with active tile registry from Strategy 1
- [x] Cap stability at 100 and completely deactivate water tiles at max stability
- [x] Make processing frequency inversely proportional to stability level
- [x] Ensure proper reactivation when neighboring tiles change

### Strategy 3: Chunked Processing (Organizes Strategy 1's registry)
- [x] Define chunk size constants and create chunk data structure
- [x] Implement methods to calculate chunk ID from map coordinates
- [x] Create chunk activation/deactivation system based on active tiles from Strategy 1
- [x] Modify water diffusion to process by chunks instead of individual tiles
- [x] Implement staggered processing of chunks across multiple ticks
- [x] Add logic to activate neighboring chunks when water reaches chunk borders
- [x] Create chunk-based spatial index for faster water tile lookups
- [x] Add debug visualization for active chunks
- [x] Implement checkerboard update pattern (update alternating chunks on different ticks)
- [x] Ensure compatibility with Strategy 2's stability detection

### Strategy 4: Multi-Phase Processing (Optimizes how Strategy 1's active tiles are processed)
#### Summary
Strategy 4 fundamentally changes how water diffusion is calculated by separating the process into three distinct phases instead of processing each water tile independently. This approach prevents cascading updates and ensures more accurate water distribution.

**Current Implementation (Problem)**: Currently, each water tile independently decides to transfer volume to adjacent tiles. This leads to potential issues:
1. When multiple water tiles transfer to the same destination in a single tick, the receiving tile's state changes between each transfer, creating cascading and unpredictable updates
2. The order of processing can affect the final state, leading to inconsistent behavior
3. Each tile makes decisions without knowing what other tiles are doing, causing inefficient water movement

**Strategy 4 Solution**: Implement a multi-phase processing approach:
1. **Collection Phase**: Gather all potential water movements without making any changes
2. **Calculation Phase**: Determine the final volumes based on all pending transfers
3. **Application Phase**: Apply all changes at once

**Benefits**:
- More accurate water diffusion that doesn't depend on processing order
- Better performance by batching similar operations
- Prevents cascading updates that can cause performance spikes
- Provides a solid foundation for future water pressure mechanics

#### Implementation Tasks
- [x] Create data structures to store pending water transfers
- [x] Split water diffusion into collection, calculation, and application phases
- [x] Implement collection phase to gather all potential water movements from active tiles
- [x] Create calculation phase to determine final volumes after all transfers
- [x] Add application phase to apply all changes at once
- [x] Optimize by batching similar operations together
- [x] Add concurrency protection for multi-phase processing
- [x] Implement fallback mechanism for very large water systems
- [x] Add maximum spread limit per tick to prevent performance spikes during floods
- [x] Integrate with chunked processing from Strategy 3
- [x] Enhance entropy detection to better reach equilibrium
- [x] Add significant volume difference check before transferring
- [x] Add equilibrium detection to prevent unnecessary transfers
- [x] Track and analyze system-wide stability metrics
- [x] Boost stability when the system approaches equilibrium

#### Implementation Details
We'll create a new `WaterTransferManager` class that will handle the multi-phase processing:

```csharp
// Planned class structure:
public class WaterTransferManager
{
    // Store pending transfers
    private Dictionary<IntVec3, List<WaterTransfer>> pendingTransfers = new Dictionary<IntVec3, List<WaterTransfer>>();
    
    // Track final volumes after all transfers
    private Dictionary<IntVec3, int> finalVolumes = new Dictionary<IntVec3, int>();
    
    // Collection phase
    public void CollectTransfers(Map map, IEnumerable<IntVec3> activeTiles);
    
    // Calculation phase
    public void CalculateFinalVolumes();
    
    // Application phase
    public void ApplyTransfers(Map map);
    
    // Helper struct to represent a transfer
    public struct WaterTransfer
    {
        public IntVec3 Source;
        public IntVec3 Destination;
        public int Amount;
    }
}
```

Integration with our existing systems:
1. We'll modify `GameComponent_WaterDiffusion` to use the multi-phase approach
2. For chunk-based processing, we'll collect transfers per chunk, then process them all together
3. We'll maintain compatibility with the stability detection system by tracking which tiles change

### Strategy 5: Update Frequency Optimization (Controls when Strategies 1-4 run)
- [ ] Implement configurable global update frequency (process water every N ticks)
- [ ] Create dynamic update frequency system based on current TPS
- [ ] Use TickRare() and TickLong() for different water processes (diffusion vs. evaporation)
- [ ] Add throttling system that automatically reduces update frequency when water entities exceed threshold
- [ ] Implement visual vs. simulation layer separation (full simulation for nearby water, simplified for distant)
- [ ] Create distance-based update frequency (update nearby water more frequently than distant water)
- [ ] Hook into the processing systems from Strategies 1-4

### Strategy 6: Data Structure Optimization (Improves the foundation of all other strategies)
- [ ] Replace water entity collections with flat arrays for better cache locality
- [ ] Implement a custom water grid using a 1D array (x + z * width indexing)
- [ ] Precompute and cache neighbor offsets to avoid repeated calculations
- [ ] Ensure all water calculations use integer math instead of floating point
- [ ] Use bitwise operations for level calculations where applicable
- [ ] Create efficient lookup tables for common calculations
- [ ] Optimize memory usage with struct-based water data instead of class references
- [ ] Update the registries and processing from Strategies 1-5 to use these optimized structures

### General Performance Improvements (Compatible with all strategies)
- [ ] Implement object pooling for frequently created collections
- [ ] Add configurable processing limits per tick
- [ ] Create emergency throttling system for extreme water volumes
- [ ] Add performance monitoring and reporting tools for each strategy
- [ ] Implement adaptive performance scaling based on current FPS
- [ ] Create detailed debug logging options for performance analysis





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