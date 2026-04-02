# TerrainSystem - Complete Terrain Revamp for BasterBoerv3

## Overview

TerrainSystem is a complete replacement for the legacy terrain generation in BasterBoerv3. It transforms the terrain from a simple Perlin noise height function into a rich, layered, deterministic terrain pipeline that generates authentic South African bushveld landscapes.

### Key Architectural Shift

**Old Model:** "Sample height from noise and build a chunk"

**New Model:** "Build each chunk from layered terrain intelligence"

The terrain is now generated as a **stack of deterministic world-space scalar fields**, not as one Perlin height function.

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           TERRAIN SYSTEM ARCHITECTURE                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌──────────────┐     ┌─────────────────────────────────────────────────┐  │
│  │   GameState  │────▶│  World Seed, Map Dimensions, Season State      │  │
│  └──────────────┘     └─────────────────────────────────────────────────┘  │
│         │                                                                   │
│         ▼                                                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                      TerrainSystem (Singleton)                      │   │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐              │   │
│  │  │   Config     │  │   Noise      │  │   Global     │              │   │
│  │  │   (Params)   │  │   Generators │  │   Features   │              │   │
│  │  └──────────────┘  └──────────────┘  └──────────────┘              │   │
│  │                                                                     │   │
│  │  ┌─────────────────────────────────────────────────────────────┐   │   │
│  │  │              Generation Pipeline (9 Phases)                  │   │   │
│  │  │  1. Macro Landform → 2. Flat Areas → 3. Ridges/Hills        │   │   │
│  │  │  4. Hydrology → 5. Waterholes → 6. Erosion                  │   │   │
│  │  │  7. Roads → 8. Biome Masks → 9. Compute Derivatives         │   │   │
│  │  └─────────────────────────────────────────────────────────────┘   │   │
│  │                                                                     │   │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐              │   │
│  │  │ Chunk Cache  │  │   Seasonal   │  │   Query      │              │   │
│  │  │   (LRU)      │  │   State      │  │   API        │              │   │
│  │  └──────────────┘  └──────────────┘  └──────────────┘              │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│         │                                                                   │
│         │  TerrainChunkData (rich payload)                                  │
│         ▼                                                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                    WorldChunkStreamer                               │   │
│  │         (manages active chunks, background loading)                 │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│         │                                                                   │
│         ▼                                                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                      Render Layer                                   │   │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐              │   │
│  │  │ WorldChunk   │  │   Shader     │  │   Flora      │              │   │
│  │  │ (Mesh+Data)  │  │ (Masks+Season)│  │ (MultiMesh)  │              │   │
│  │  └──────────────┘  └──────────────┘  └──────────────┘              │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                   Gameplay Systems                                  │   │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐              │   │
│  │  │AnimalMovement│  │BuildingSystem│  │ WaterSystem  │              │   │
│  │  │TerrainQuery  │  │  (Placement) │  │ (Sources)    │              │   │
│  │  └──────────────┘  └──────────────┘  └──────────────┘              │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Files

| File | Purpose |
|------|---------|
| `TerrainSystem.cs` | Core singleton managing terrain generation, caching, and queries |
| `TerrainConfig.cs` | ScriptableObject with all tweakable terrain parameters |
| `TerrainChunkData.cs` | Rich data payload containing heightmaps, masks, and metadata |
| `TerrainQuery.cs` | Query interface for height, slope, moisture, walkability, etc. |
| `WorldChunk.cs` | Updated chunk class consuming TerrainChunkData |
| `WorldChunkStreamer.cs` | Updated streamer with TerrainSystem integration |
| `terrain_system.gdshader` | Shader supporting masks and seasonal variation |

## Installation

### 1. Copy Files

Copy all files from the `TerrainSystem` folder to your project's `Scripts/Terrain/` directory:

```
Scripts/
  Terrain/
    TerrainSystem.cs
    TerrainConfig.cs
    TerrainChunkData.cs
shaders/
  terrain_system.gdshader
```

### 2. Update Existing Files

Replace your existing files with the updated versions:
- `TerrainQuery.cs` → Use the new TerrainQuery.cs
- `WorldChunk.cs` → Use the new WorldChunk.cs
- `WorldChunkStreamer.cs` → Use the new WorldChunkStreamer.cs

### 3. Create TerrainConfig Resource

Create a `TerrainConfig` resource file:
1. In Godot, go to **File > New Resource**
2. Select `TerrainConfig`
3. Save as `res://resources/terrain_config.tres`
4. Adjust parameters as needed

### 4. Update Scene

In your main scene:
1. Ensure `WorldChunkStreamer` node exists
2. Check "Use Terrain System" in the inspector
3. The TerrainSystem will be auto-initialized

## Configuration Parameters

### World Generation
- `WorldSeed` - Deterministic seed for reproducible worlds
- `ChunkSize` - Size of each terrain chunk (default: 256m)
- `ChunkResolution` - Vertices per chunk side (default: 128)
- `HeightScale` - Maximum elevation variation (default: 40m)

### Macro Landform
- `MacroNoiseFrequency` - Large terrain feature scale
- `MacroOctaves` - Detail levels for macro noise
- `RidgeInfluence` - Strength of ridgeline features
- `ValleyInfluence` - Strength of valley features

### Flat Areas
- `FlatAreaFrequency` - How often flat areas appear
- `FlatAreaThreshold` - Control for flat area selection
- `MinFlatAreaSize` / `MaxFlatAreaSize` - Size range for flats
- `FlatAreaBlendStrength` - How smoothly flats blend with terrain

### Waterholes
- `WaterholeCount` - Number of waterholes in the world
- `WaterholeMinRadius` / `WaterholeMaxRadius` - Size range
- `WaterholeDepth` - Depth of waterhole basins
- `WaterholeBasinSteepness` - Slope of basin walls

### Roads
- `RoadDensity` - How much road coverage
- `RoadWidth` - Width of roads in meters
- `RoadFlattening` - How much roads flatten terrain
- `RoadDepressionDepth` - Wheel track depression

### Seasonal
- `WetnessResponse` - How terrain responds to rain
- `DrySeasonBleaching` - Color desaturation in dry season
- `GreenFlushIntensity` - Vegetation greening after rain

## Usage

### Basic Height Query

```csharp
float height = TerrainQuery.GetHeight(playerPosition);
```

### Rich Terrain Sample

```csharp
TerrainSample sample = TerrainQuery.GetSample(position);
bool canBuild = sample.IsBuildable();
bool canWalk = sample.IsWalkable();
float movementCost = sample.GetMovementCost();
```

### Waterhole Queries

```csharp
WaterholeInfo nearest = TerrainQuery.GetNearestWaterhole(position, out float distance);
WaterholeInfo[] nearby = TerrainQuery.GetWaterholesInRange(position, 500f);
```

### Movement Validation

```csharp
bool canMove = TerrainQuery.CanMove(fromPos, toPos);
bool valid = TerrainQuery.TryValidateMove(current, desired, out Vector3 adjusted);
```

### Seasonal Updates

Called automatically by TimeSystem:

```csharp
// In TimeSystem or WeatherSystem
WorldChunkStreamer.Instance.UpdateSeasonalState(wetness, dryness, greenBias);
```

## Generation Pipeline Phases

### Phase 1: Macro Landform
Creates the broad shape of the world: rolling veld, escarpment-like variation, large plains, shallow basins. Uses low-frequency world-space noise mixed with directional structure.

### Phase 2: Flat Area Preservation
Creates controllable flat areas for building and grazing. Uses a large-scale field to designate buildable flats vs rough terrain with soft blending.

### Phase 3: Ridge and Hill Shaping
Adds believable uplifts, ridgelines, shoulders, and hill chains with directional bias for regional character.

### Phase 4: Hydrology
Computes flow direction, accumulation, drainage corridors, and moisture distribution. Carves drainage channels into terrain.

### Phase 5: Waterhole Carving
Carves waterhole basins into terrain based on globally-placed waterhole positions. Creates realistic basin profiles.

### Phase 6: Erosion
Applies procedural erosion based on slope, flow, and rock hardness. Creates gullies, worn channels, and rock exposures.

### Phase 7: Road Stamping
Stamps road masks into terrain with flattening and depression effects. Roads blend naturally into the landscape.

### Phase 8: Biome Masks
Generates soil type, rockiness, and biome variation masks based on height, slope, moisture, and erosion.

### Phase 9: Compute Derivatives
Computes final slope map and other derived values from the complete heightmap.

## Performance

### Background Generation
- All heavy terrain generation runs on background threads
- Main thread only applies completed results
- Configurable concurrent build limit

### Chunk Caching
- LRU cache for recently used chunks
- Configurable cache size
- Cache hit/miss tracking

### Average Build Times
- 128x128 chunk: ~15-30ms on modern hardware
- 256x256 chunk: ~50-100ms on modern hardware

## Debugging

### TerrainSystem Debug Info
```csharp
GD.Print(TerrainSystem.Instance.GetDebugInfo());
```

Output:
```
[TerrainSystem]
  Seed: 12345
  Waterholes: 15
  Road nodes: 25
  Cache: 9 chunks, 85.3% hit rate
  Avg build time: 22.5ms
  Global wetness: 0.65
```

### WorldChunkStreamer Stats
```csharp
GD.Print(WorldChunkStreamer.Instance.GetPerformanceStats());
```

### Visualize Masks
Enable `EnableDebugVisualization` in TerrainConfig to see mask visualization.

## Integration with Existing Systems

### AnimalMovement
No changes needed - continues using `TerrainQuery.GetHeight()` and `TerrainQuery.CanMove()`.

### BuildingSystem
No changes needed - continues using `TerrainQuery.IsBuildable()`.

### WaterSystem
Can now query waterholes directly:
```csharp
WaterholeInfo[] waterholes = TerrainQuery.GetWaterholesInRange(position, range);
```

### TimeSystem
Call seasonal update:
```csharp
WorldChunkStreamer.Instance.UpdateSeasonalState(wetness, dryness, greenBias);
```

## Migration from Old System

1. **Backup** your existing terrain files
2. **Copy** new TerrainSystem files
3. **Replace** TerrainQuery.cs, WorldChunk.cs, WorldChunkStreamer.cs
4. **Create** TerrainConfig resource
5. **Test** in editor
6. **Tune** parameters to match your vision

The old `TerrainGenerator.cs` can be kept for reference but is no longer used.

## Troubleshooting

### Terrain not generating
- Check that `UseTerrainSystem` is enabled in WorldChunkStreamer
- Verify TerrainConfig resource exists
- Check console for errors

### Chunks loading slowly
- Reduce `ChunkResolution` (try 64 instead of 128)
- Reduce `MaxConcurrentLoads` to 1
- Check background thread priority

### Memory issues
- Reduce `HeightmapCacheSize` in TerrainConfig
- Reduce `MaxActiveChunks` in WorldChunkStreamer

### Visual artifacts
- Ensure shader is assigned to terrain material
- Check that vertex colors are being set
- Verify normal calculations

## Future Enhancements

Potential additions for later versions:
- Terrain deformation (animal tracks, vehicle ruts)
- Dynamic water flow
- Fire spread using terrain masks
- Grazing impact on terrain
- Seasonal waterhole level changes

## License

Part of BasterBoerv3 - BasterBoer Plaas Speletjie
