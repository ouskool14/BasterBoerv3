# BasterBoer Flora System Integration Guide

This guide provides step-by-step instructions for integrating the new flora system into your BasterBoer project.

---

## Overview

The new flora system replaces the old `FloraType` enum-based approach with a more flexible archetype system. Key changes:

| Old System | New System |
|------------|------------|
| `FloraType` enum (16 values) | `byte ArchetypeId` (6 archetypes) |
| `FloraEntry.IsTree()`, `GetVisualRadius()` | Render logic moved to `FloraPopulator` |
| `FloraEntry.IsInvasive` | Moved to `ChunkEcologyState.InvasivePressure` |
| Uniform random scatter | Poisson-disc sampling |
| Per-instance storage for shrubs | `FloraPatch` descriptor system |
| Static mesh cache | Instance-based cache (no stale references) |

---

## Step 1: Replace Flora Files

### Delete Old Files

Delete these files from your `Scripts/` folder:
- `FloraEntry.cs` (old version)
- `FloraSystem.cs` (old version)
- `FloraPopulator.cs` (old version)
- `FloraEntry.cs.uid`
- `FloraSystem.cs.uid`
- `FloraPopulator.cs.uid`

### Add New Files

Copy these new files to your `Scripts/` folder:
- `ChunkEcologyState.cs`
- `FloraEntry.cs` (new version)
- `FloraPatch.cs`
- `BushveldBiomeRecipe.cs`
- `FloraGenerator.cs`
- `FloraSystem.cs` (new version)
- `FloraPopulator.cs` (new version)

---

## Step 2: Update WorldChunk.cs

The `WorldChunk.cs` file needs significant updates to work with the new system.

### Replace the `using` statements (around line 1-5):

```csharp
// OLD:
using WorldStreaming.Flora;
using WorldStreaming.Terrain;

// NEW (keep the same - no change needed):
using WorldStreaming.Flora;
using WorldStreaming.Terrain;
```

### Replace the `BuildChunkContent()` method (around line 60-80):

```csharp
// OLD:
public ChunkBuildResult BuildChunkContent()
{
    // Get rich terrain data from TerrainSystem
    TerrainData = TerrainSystem.Instance?.BuildChunk(Coordinate);
    
    if (TerrainData == null)
    {
        GD.PushError($"[WorldChunk] Failed to build terrain data for chunk {Coordinate}");
        TerrainData = TerrainChunkData.Create(Coordinate, ChunkSize, 64);
    }

    // Get flora data from FloraSystem
    List<FloraEntry> floraEntries = FloraSystem.Instance?.GetFloraForChunk(Coordinate) 
        ?? new List<FloraEntry>();

    // Create MultiMesh instances for flora
    Dictionary<FloraType, MultiMesh> floraMultiMeshes = 
        FloraPopulator.CreateFloraMultiMeshes(floraEntries, Coordinate, ChunkSize);

    return new ChunkBuildResult
    {
        Coordinate = Coordinate,
        TerrainData = TerrainData,
        FloraMultiMeshes = floraMultiMeshes
    };
}

// NEW:
public ChunkBuildResult BuildChunkContent()
{
    // Get rich terrain data from TerrainSystem
    TerrainData = TerrainSystem.Instance?.BuildChunk(Coordinate);
    
    if (TerrainData == null)
    {
        GD.PushError($"[WorldChunk] Failed to build terrain data for chunk {Coordinate}");
        TerrainData = TerrainChunkData.Create(Coordinate, ChunkSize, 64);
    }

    // Get flora data from FloraSystem (NEW API)
    List<FloraEntry> structuralEntries = FloraSystem.Instance?.GetStructuralForChunk(Coordinate) 
        ?? new List<FloraEntry>();
    List<FloraPatch> patches = FloraSystem.Instance?.GetPatchesForChunk(Coordinate)
        ?? new List<FloraPatch>();
    ChunkEcologyState ecology = FloraSystem.Instance?.GetChunkEcology(Coordinate)
        ?? ChunkEcologyState.CreateNeutral();

    // Create MultiMesh instances for flora (NEW API)
    Dictionary<byte, MultiMesh> floraMultiMeshes = 
        FloraPopulator.CreateFloraMultiMeshes(structuralEntries, patches, Coordinate, ChunkSize, ecology);

    return new ChunkBuildResult
    {
        Coordinate = Coordinate,
        TerrainData = TerrainData,
        FloraMultiMeshes = floraMultiMeshes
    };
}
```

### Replace the `ChunkBuildResult` struct (around line 280-290):

```csharp
// OLD:
public struct ChunkBuildResult
{
    public ChunkCoord Coordinate;
    public TerrainChunkData TerrainData;
    public Dictionary<FloraType, MultiMesh> FloraMultiMeshes;
}

// NEW:
public struct ChunkBuildResult
{
    public ChunkCoord Coordinate;
    public TerrainChunkData TerrainData;
    public Dictionary<byte, MultiMesh> FloraMultiMeshes;
}
```

### Replace the `ApplyBuildResult()` flora section (around line 110-140):

```csharp
// OLD:
foreach (var kvp in buildResult.FloraMultiMeshes)
{
    FloraType type = kvp.Key;
    MultiMesh multiMesh = kvp.Value;

    var mmi = new MultiMeshInstance3D
    {
        Multimesh = multiMesh,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Name = $"Flora_{type}"
    };

    float visibilityRange = GetFloraVisibilityRange(type);
    mmi.VisibilityRangeBegin = 0f;
    mmi.VisibilityRangeEnd = visibilityRange;
    mmi.VisibilityRangeEndMargin = 10f;

    AddChild(mmi);
    _floraMultiMeshes[type] = mmi;
}

// NEW:
foreach (var kvp in buildResult.FloraMultiMeshes)
{
    byte archetypeId = kvp.Key;
    MultiMesh multiMesh = kvp.Value;

    // Handle patch archetypes (key > 127 means patch instance)
    bool isPatch = archetypeId >= 128;
    byte actualArchetypeId = isPatch ? (byte)(archetypeId - 128) : archetypeId;

    string namePrefix = isPatch ? "Patch_" : "Flora_";
    var mmi = new MultiMeshInstance3D
    {
        Multimesh = multiMesh,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Name = $"{namePrefix}{FloraArchetypeIds.GetDisplayName(actualArchetypeId)}"
    };

    float visibilityRange = FloraPopulator.GetVisibilityRange(actualArchetypeId);
    mmi.VisibilityRangeBegin = 0f;
    mmi.VisibilityRangeEnd = visibilityRange;
    mmi.VisibilityRangeEndMargin = 10f;

    AddChild(mmi);
    _floraMultiMeshes[archetypeId] = mmi;
}
```

### Replace the `_floraMultiMeshes` dictionary declaration (around line 45):

```csharp
// OLD:
private readonly Dictionary<FloraType, MultiMeshInstance3D> _floraMultiMeshes = 
    new Dictionary<FloraType, MultiMeshInstance3D>();

// NEW:
private readonly Dictionary<byte, MultiMeshInstance3D> _floraMultiMeshes = 
    new Dictionary<byte, MultiMeshInstance3D>();
```

### Delete the `GetFloraVisibilityRange()` method (around line 260-275):

```csharp
// DELETE THIS ENTIRE METHOD:
private static float GetFloraVisibilityRange(FloraType type)
{
    return type switch
    {
        FloraType.RedGrass or FloraType.PanicGrass => 80f,
        FloraType.MagicGuarana or FloraType.SicklebushDichrostachys or 
        FloraType.InvasiveLantana or FloraType.InvasiveBugweed => 150f,
        _ => 300f
    };
}
```

---

## Step 3: Update WorldChunkStreamer.cs

### Add the `RefreshAllFloraVisuals()` implementation (around line 200):

```csharp
// OLD (empty method):
public void RefreshAllFloraVisuals()
{
    foreach (var chunk in _activeChunks.Values)
    {
        // TODO: Implement flora refresh if needed for dynamic health changes
    }
    GD.Print("[WorldChunkStreamer] Flora visuals refreshed for all active chunks");
}

// NEW:
public void RefreshAllFloraVisuals()
{
    foreach (var chunk in _activeChunks.Values)
    {
        // Unload and reload flora visuals
        chunk.ReloadFloraVisuals();
    }
    GD.Print("[WorldChunkStreamer] Flora visuals refreshed for all active chunks");
}
```

### Add a new method to WorldChunk.cs for reloading flora:

Add this new method to `WorldChunk.cs` (after `UnloadChunk()`):

```csharp
/// <summary>
/// Reloads only the flora visuals for this chunk.
/// Called when ecology state changes significantly.
/// </summary>
public void ReloadFloraVisuals()
{
    if (!IsLoaded) return;

    // Remove existing flora MultiMeshes
    foreach (var mmi in _floraMultiMeshes.Values)
    {
        if (mmi != null && IsInstanceValid(mmi))
        {
            mmi.QueueFree();
        }
    }
    _floraMultiMeshes.Clear();

    // Get fresh flora data
    List<FloraEntry> structuralEntries = FloraSystem.Instance?.GetStructuralForChunk(Coordinate) 
        ?? new List<FloraEntry>();
    List<FloraPatch> patches = FloraSystem.Instance?.GetPatchesForChunk(Coordinate)
        ?? new List<FloraPatch>();
    ChunkEcologyState ecology = FloraSystem.Instance?.GetChunkEcology(Coordinate)
        ?? ChunkEcologyState.CreateNeutral();

    // Create new MultiMeshes
    Dictionary<byte, MultiMesh> floraMultiMeshes = 
        FloraPopulator.CreateFloraMultiMeshes(structuralEntries, patches, Coordinate, ChunkSize, ecology);

    // Add to scene (same code as ApplyBuildResult)
    foreach (var kvp in floraMultiMeshes)
    {
        byte archetypeId = kvp.Key;
        MultiMesh multiMesh = kvp.Value;

        bool isPatch = archetypeId >= 128;
        byte actualArchetypeId = isPatch ? (byte)(archetypeId - 128) : archetypeId;

        string namePrefix = isPatch ? "Patch_" : "Flora_";
        var mmi = new MultiMeshInstance3D
        {
            Multimesh = multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Name = $"{namePrefix}{FloraArchetypeIds.GetDisplayName(actualArchetypeId)}"
        };

        float visibilityRange = FloraPopulator.GetVisibilityRange(actualArchetypeId);
        mmi.VisibilityRangeBegin = 0f;
        mmi.VisibilityRangeEnd = visibilityRange;
        mmi.VisibilityRangeEndMargin = 10f;

        AddChild(mmi);
        _floraMultiMeshes[archetypeId] = mmi;
    }

    GD.Print($"[WorldChunk] Reloaded flora visuals for {Coordinate}");
}
```

---

## Step 4: Update TimeSystem.cs

The `TimeSystem` needs to call the new flora system tick methods.

### Add field reference (around line 30):

```csharp
private FloraSystem _floraSystem;
```

### Initialize in `_Ready()` (around line 50):

```csharp
_floraSystem = FloraSystem.Instance;
```

### Call flora ticks in your update method:

```csharp
// In your daily tick handler:
private void OnDailyTick()
{
    _floraSystem?.OnDailyTick();
    
    // ... rest of your daily tick code
}

// In your monthly tick handler:
private void OnMonthlyTick()
{
    float rainfall = WeatherSystem.Instance?.GetMonthlyRainfall() ?? 0f;
    _floraSystem?.OnMonthlyTick(rainfall);
    
    // ... rest of your monthly tick code
}

// In your season change handler:
private void OnSeasonChanged(Season newSeason)
{
    _floraSystem?.OnSeasonChanged(newSeason);
    
    // ... rest of your season change code
}
```

---

## Step 5: Update GrazingSystem.cs (Optional)

If you want grazing to affect flora patches, add this integration:

```csharp
// In your grazing update method, after calculating grazing pressure:
private void UpdateGrazingPressureForChunk(ChunkCoord coord, float pressure)
{
    FloraSystem.Instance?.SetGrazingPressure(coord, pressure);
}
```

---

## Step 6: Create Shader (Optional)

Create a new shader at `shaders/flora_shader.gdshader` to use the custom data:

```glsl
shader_type spatial;
render_mode diffuse_lambert, cull_disabled;

uniform vec4 healthy_hue : source_color = vec4(0.545, 0.627, 0.353, 1.0);
uniform vec4 dry_season_hue : source_color = vec4(0.722, 0.627, 0.353, 1.0);
uniform vec4 drought_hue : source_color = vec4(0.769, 0.659, 0.333, 1.0);
uniform vec4 burn_recovery_hue : source_color = vec4(0.490, 0.769, 0.333, 1.0);

void vertex() {
    // Custom data is passed in INSTANCE_CUSTOM
    float hue_offset = INSTANCE_CUSTOM.x;
    float dryness = INSTANCE_CUSTOM.y;
    float burn_tint = INSTANCE_CUSTOM.z;
    float canopy_fill = INSTANCE_CUSTOM.w;
    
    // Apply canopy fill to scale
    VERTEX *= canopy_fill;
}

void fragment() {
    float hue_offset = INSTANCE_CUSTOM.x;
    float dryness = INSTANCE_CUSTOM.y;
    float burn_tint = INSTANCE_CUSTOM.z;
    float canopy_fill = INSTANCE_CUSTOM.w;
    
    // Blend between healthy and dry based on dryness
    vec4 base_color = mix(healthy_hue, dry_season_hue, dryness);
    
    // Apply drought stress
    base_color = mix(base_color, drought_hue, dryness * 0.5);
    
    // Apply burn tint (blackened)
    vec4 burn_color = vec4(0.1, 0.1, 0.1, 1.0);
    base_color = mix(base_color, burn_color, burn_tint * 0.7);
    
    // Apply recovery flush
    base_color = mix(base_color, burn_recovery_hue, (1.0 - burn_tint) * dryness * 0.3);
    
    ALBEDO = base_color.rgb;
}
```

---

## Step 7: Test Checklist

Before considering the integration complete, verify:

- [ ] Game compiles without errors
- [ ] Chunks load with flora visible
- [ ] Structural trees (FlatThorn, UprightDryland, RoundLandmark) are visible
- [ ] Patch shrubs (DenseThornShrub, LowDryBush) are visible
- [ ] DeadSnags appear occasionally
- [ ] Flora placement uses Poisson-disc (no clumping/voids)
- [ ] Different chunks have different flora distributions
- [ ] Same chunk produces same flora on reload (deterministic)
- [ ] No stale mesh references after scene reload

---

## Troubleshooting

### "FloraSystem.Instance is null"
- Ensure FloraSystem node is in your scene tree
- Check that FloraSystem._EnterTree() is being called

### "No flora visible"
- Check that WorldChunk.BuildChunkContent() is being called
- Verify FloraPopulator.CreateFloraMultiMeshes() returns non-empty dictionary
- Check visibility range settings on MultiMeshInstance3D

### "Flora looks wrong (all same type)"
- Verify archetype weights in BushveldBiomeRecipe
- Check that SelectStructuralArchetype() is using RNG correctly

### "Memory leaks / stale references"
- Ensure ClearVisualContent() is being called in WorldChunk.UnloadChunk()
- Verify FloraPopulator.ClearMeshCache() is called on scene changes

---

## Migration Notes

### Save Game Compatibility

The new system uses `ChunkEcologyState` for persistence. Old save files won't have this data.

**Option 1: Reset flora on load**
- Just ignore old saves - chunks will regenerate with neutral ecology

**Option 2: Migrate old saves**
- Add migration code to convert old flora data to new format
- Create neutral ecology states for chunks with existing flora

### Custom Flora Types

If you added custom `FloraType` values, map them to archetypes:

| Old FloraType | New ArchetypeId |
|---------------|-----------------|
| AcaciaThorn | 0 (FlatThorn) |
| MarulaMpopona | 2 (RoundLandmark) |
| BuffaloThorn | 1 (UprightDryland) |
| MagicGuarana | 3 (DenseThornShrub) |
| SicklebushDichrostachys | 3 (DenseThornShrub) |
| RedGrass | 4 (LowDryBush) |
| InvasiveLantana | 3 (DenseThornShrub) + high invasive pressure |

---

## File Summary

| File | Purpose | Lines |
|------|---------|-------|
| `ChunkEcologyState.cs` | Per-chunk ecology (7 floats + season) | ~180 |
| `FloraEntry.cs` | Slim simulation struct | ~150 |
| `FloraPatch.cs` | Dense shrub descriptor + expansion | ~200 |
| `BushveldBiomeRecipe.cs` | Data-defined biome configuration | ~200 |
| `FloraGenerator.cs` | Poisson-disc placement logic | ~350 |
| `FloraSystem.cs` | Singleton, API, simulation ticks | ~450 |
| `FloraPopulator.cs` | Render-only MultiMesh construction | ~400 |

---

## Support

For issues or questions about the flora system:
1. Check the visual test checklist above
2. Review the implementation plan document
3. Check Godot console for error messages
