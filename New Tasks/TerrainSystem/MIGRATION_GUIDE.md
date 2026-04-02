# TerrainSystem Migration Guide

This guide helps you migrate from the legacy terrain system to the new TerrainSystem architecture.

## Quick Migration Checklist

- [ ] Backup existing terrain files
- [ ] Copy new TerrainSystem files to project
- [ ] Update/replace existing files
- [ ] Create TerrainConfig resource
- [ ] Update main scene
- [ ] Test and tune

## Step-by-Step Migration

### Step 1: Backup

Before making any changes, backup these files:
```
TerrainGenerator.cs
TerrainQuery.cs
WorldChunk.cs
WorldChunkStreamer.cs
```

### Step 2: Copy New Files

Copy these files to your project:

```
Scripts/Terrain/
  ├── TerrainSystem.cs       (NEW)
  ├── TerrainConfig.cs       (NEW)
  ├── TerrainChunkData.cs    (NEW)
  ├── TerrainQuery.cs        (REPLACE)
  ├── WorldChunk.cs          (REPLACE)
  └── WorldChunkStreamer.cs  (REPLACE)

shaders/
  └── terrain_system.gdshader (NEW)
```

### Step 3: Create TerrainConfig Resource

1. In Godot, go to **File > New Resource**
2. Select `TerrainConfig` from the list
3. Save as `res://resources/terrain_config.tres`
4. Adjust these key parameters:
   - `WorldSeed` - Set your desired world seed
   - `HeightScale` - Adjust for your terrain amplitude (default: 40)
   - `WaterholeCount` - Number of waterholes (default: 15)
   - `FlatAreaThreshold` - Control flat area frequency (default: 0.6)

### Step 4: Update Main Scene

1. Select your `WorldChunkStreamer` node
2. In the Inspector:
   - Check **"Use Terrain System"**
   - Set **"Grid Radius"** to 1 (for 3x3 grid) or 2 (for 5x5 grid)

### Step 5: Update Project Settings (Optional)

If you want to use the new shader by default:

1. Open `WorldChunk.cs`
2. Find the shader path in `ApplyTerrainMaterial()`
3. Update if your shader path is different

### Step 6: Test

1. Run the game
2. Check console for initialization messages:
   ```
   [TerrainSystem] Initializing with seed 12345
   [TerrainSystem] Generated 15 waterholes, 25 road nodes
   [TerrainSystem] Initialization complete
   ```
3. Verify terrain generates correctly
4. Check that player doesn't fall through

## API Changes

### TerrainQuery (Backwards Compatible)

The new TerrainQuery maintains the same API:

```csharp
// These work exactly as before:
float height = TerrainQuery.GetHeight(position);
float slope = TerrainQuery.GetSlope(position);
bool walkable = TerrainQuery.IsWalkable(position);
bool canMove = TerrainQuery.CanMove(from, to);
```

New capabilities:

```csharp
// Rich terrain sample
TerrainSample sample = TerrainQuery.GetSample(position);
float wetness = sample.Wetness;
float rockiness = sample.Rockiness;
float roadInfluence = sample.RoadInfluence;

// Waterhole queries
WaterholeInfo nearest = TerrainQuery.GetNearestWaterhole(position, out float dist);
WaterholeInfo[] nearby = TerrainQuery.GetWaterholesInRange(position, 500f);
```

### WorldChunkStreamer (New Features)

New properties:
- `UseTerrainSystem` - Enable new system
- `GridRadius` - Control active chunk radius

New methods:
```csharp
// Get rich terrain data
TerrainChunkData data = streamer.GetChunkTerrainData(coord);

// Update seasonal state
streamer.UpdateSeasonalState(wetness, dryness, greenBias);

// Performance stats
string stats = streamer.GetPerformanceStats();
```

## Common Issues and Solutions

### Issue: "TerrainSystem not found"

**Cause:** TerrainSystem node not created

**Solution:** Check "Use Terrain System" in WorldChunkStreamer inspector

### Issue: Terrain looks flat/boring

**Cause:** Default parameters too conservative

**Solution:** Adjust in TerrainConfig:
```
HeightScale = 60f (was 40f)
RidgeIntensity = 0.7f (was 0.5f)
ErosionStrength = 0.6f (was 0.4f)
```

### Issue: Too many/few waterholes

**Solution:** Adjust `WaterholeCount` in TerrainConfig

### Issue: Chunks loading too slowly

**Solutions:**
1. Reduce `ChunkResolution` from 128 to 64
2. Reduce `MaxConcurrentLoads` from 2 to 1
3. Reduce `HeightmapCacheSize` from 64 to 32

### Issue: Memory usage too high

**Solutions:**
1. Reduce `MaxActiveChunks` in WorldChunkStreamer
2. Reduce `HeightmapCacheSize` in TerrainConfig
3. Reduce `ChunkResolution` in TerrainConfig

## Performance Comparison

| Metric | Old System | New System | Notes |
|--------|------------|------------|-------|
| Build Time | ~10ms | ~25ms | More complex generation |
| Cache Hit Rate | N/A | ~85% | LRU caching |
| Memory/Chunk | ~2MB | ~5MB | Rich data + masks |
| Query Speed | ~0.01ms | ~0.01ms | Same performance |
| Visual Quality | Basic | Rich | Masks + seasonal |

## Parameter Tuning Guide

### For Flatter Terrain (More Buildable Areas)

```csharp
FlatAreaThreshold = 0.4f;        // Lower = more flat areas
FlatAreaBlendStrength = 0.8f;    // Higher = smoother flats
HeightScale = 25f;               // Lower = less elevation
```

### For More Dramatic Terrain

```csharp
HeightScale = 60f;
RidgeIntensity = 0.8f;
ValleyInfluence = 0.6f;
ErosionStrength = 0.7f;
Rockiness = 0.5f;
```

### For More Water Features

```csharp
WaterholeCount = 25;
DrainageDensity = 0.7f;
RiverCarvingDepth = 0.5f;
MoistureSpread = 0.6f;
```

### For Drier/Sahel-like Terrain

```csharp
WaterholeCount = 8;
DrainageDensity = 0.2f;
WetnessResponse = 0.3f;
DrySeasonBleaching = 0.6f;
Rockiness = 0.5f;
```

## Preset Configurations

### Bushveld (Default)

```csharp
var config = TerrainConfig.CreateDefaultBushveldConfig();
```

### Dramatic/Mountainous

```csharp
var config = TerrainConfig.CreateDramaticConfig();
```

### Custom Savannah

```csharp
var config = new TerrainConfig
{
    WorldSeed = 54321,
    HeightScale = 30f,
    FlatAreaThreshold = 0.5f,
    WaterholeCount = 20,
    DrainageDensity = 0.4f,
    RoadDensity = 0.15f,
    Rockiness = 0.25f
};
```

## Verification Steps

After migration, verify:

1. **Terrain generates** - No errors in console
2. **Player can walk** - No falling through terrain
3. **Animals work** - Herds spawn and move correctly
4. **Buildings place** - Building placement works
5. **Waterholes exist** - Can find water sources
6. **Seasons work** - Visual changes with season
7. **Performance OK** - Stable frame rate

## Rollback Plan

If issues occur:

1. Stop the game
2. Restore backed-up files:
   - TerrainGenerator.cs
   - TerrainQuery.cs
   - WorldChunk.cs
   - WorldChunkStreamer.cs
3. Uncheck "Use Terrain System" in WorldChunkStreamer
4. Test with old system

## Support

For issues or questions:
1. Check the README.md
2. Review debug output: `TerrainSystem.Instance.GetDebugInfo()`
3. Check performance stats: `WorldChunkStreamer.Instance.GetPerformanceStats()`
