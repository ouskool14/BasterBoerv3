# BasterBoer — Flora System Replacement Plan
**Supersedes:** FloraSystem.cs, FloraEntry.cs, FloraPopulator.cs, FloraPopulator.cs (legacy)  
**Target:** Godot 4.6 · C# · Bushveld v1 · Dense but performant · Low-poly stylized  
**Biome scope:** Bushveld only (v1). Other biomes stubbed as `// TODO: biome — Karoo`, etc.

---

### Problems with the existing code

**`FloraEntry.IsTree()` and `FloraEntry.GetVisualRadius()` are render logic inside simulation data.**  
`FloraEntry` is a pure simulation struct. Whether something "is a tree" for rendering purposes is a concern of `FloraPopulator` and the archetype registry, not the data record. This is a direct violation of the simulate-vs-render boundary your own architecture doc mandates.

**`FloraType` enum mixes naming conventions.**  
`MarulaMpopona` (genus+vernacular), `AcaciaThorn` (function+type), `RedGrass` (colour+type), `SicklebushDichrostachys` (common name+genus). Pick one convention. The replacement uses archetype IDs that map to display names in a registry — the enum stays lean and consistent.

**`FloraPopulator` contains procedural mesh generation.**  
The `CreateSimpleTree()`, `CreateSimpleBox()`, `CreateCrossedPlanes()` methods exist as "placeholders." They should never have been in `FloraPopulator`. The populator's job is to take simulation data and build `MultiMesh` instances from pre-loaded GLB meshes. All placeholder geometry belongs in a separate `FloraDebugMeshes.cs` file that gets deleted when real assets land.

**The existing `SelectFloraType` does not read terrain at all.**  
There is a `// TODO: Integrate with moisture and elevation maps` comment inside the method. This means every chunk currently gets the same weighted-random flora distribution regardless of whether it's a rocky ridge, a drainage line, or a flat open plain. The entire point of procedural generation is broken.

**`_floraMeshCache` is a static Dictionary.**  
Static state in Godot 4 survives scene reloads. This cache will silently hold stale mesh references across scene changes and cause subtle visual bugs.

**No `ChunkEcologyState` exists.**  
The current system has no concept of chunk-level ecological state. Every piece of data that would live there — moisture, drought stress, burn history, grazing pressure — is simply absent. Without it, the land cannot tell the player anything about its condition.

**`GetFloraInstanceColor` ignores season, moisture, and burn state entirely.**  
It only reads `entry.Health` and `entry.IsInvasive`. The colour pipeline is the primary way a low-poly game communicates ecological state. It must read from the chunk state, not just individual plant health.

---

## Part 2 — The Replacement Plan

### North Star (One Sentence)

> The player should be able to read the ecological health of any visible block of land from the driver's seat of the bakkie, without opening any UI.

That means colour, density, and silhouette do the work.

---

### Design Decisions (Fixed, Not Negotiable for v1)

| Decision | Value |
|---|---|
| Biome scope | Bushveld only. Other biomes stubbed. |
| Flora archetypes | 6 (see archetype table) |
| Render layers | 2: Structural + Patch |
| Ground cover | Shader-driven field, not instanced entries |
| Invasives | Ecological state on ChunkEcologyState, not a separate archetype |
| Biome recipe format | Single struct, data-defined (not subclassed) |
| Chunk state fields | 7 floats + 1 season enum |
| Max draw calls from flora | 8 per loaded chunk · 72 for 3×3 grid |
| Structural tree tris | 80–180 |
| Shrub/patch tris | 30–80 |
| Billboard sheet | 2–4 tris |
| Custom instance data | 4 packed floats per instance |
| Simulation tick | Daily (cheap) + Monthly (heavier) via TimeSystem |
| Serialisation | ChunkEcologyState only — static flora rebuilt from seed |

---

### The 6 Archetypes

These are visual archetypes, not botanical species. Species names are display metadata only.

| ID | ArchetypeId | Visual Identity | Tris | Layer | Example Species Names |
|---|---|---|---|---|---|
| 0 | `FlatThorn` | Wide flat canopy, sparse, iconic silhouette | 120–180 | Structural | Knob-thorn, Camel-thorn |
| 1 | `UprightDryland` | Narrow tall form, thin canopy, open | 80–140 | Structural | Shepherd's tree, Buffalo-thorn |
| 2 | `RoundLandmark` | Full round canopy, landmark presence | 140–180 | Structural | Marula, Apple-leaf |
| 3 | `DenseThornShrub` | Compact armed shrub, massing form | 40–70 | Patch | Sickle-bush, Magic guarri |
| 4 | `LowDryBush` | Sprawling low bush, dry palette | 30–60 | Patch | Raisin bush, Wild rosemary |
| 5 | `DeadSnag` | Dry skeleton, accent/death marker | 60–100 | Structural | Any standing deadwood |

> **TODO — future biomes:** Add `RiverineCanopy`, `SedgeReed`, `SucculentClump`, `FynbosShrub` when non-Bushveld biomes are implemented.

---

### ChunkEcologyState (7 floats + season)

Stored per chunk in `FloraSystem`. This is the only thing persisted to save data if the chunk is player-modified. Deterministic chunks are rebuilt from seed.

```csharp
public struct ChunkEcologyState
{
    public float Moisture;          // 0–1. Driven by rainfall + water proximity.
    public float DroughtStress;     // 0–1. Accumulates in dry months. Reduces moisture.
    public float GrazingPressure;   // 0–1. Set by GrazingSystem. Suppresses patch density.
    public float BurnAge;           // 0 = freshly burned, 1 = fully recovered. -1 = never burned.
    public float InvasivePressure;  // 0–1. Grows on disturbed/grazed ground. Shifts shrub hue.
    public float ShrubEncroachment;// 0–1. Bush thickening when grazing pressure is low.
    public float RecoveryFactor;    // 0–1. Post-rain/post-burn green flush multiplier.
    public Season CurrentSeason;    // Propagated from TimeSystem on season change.
}
```

---

### FloraEntry (Slim Simulation Struct)

Remove `IsTree()`, `GetVisualRadius()`, `IsInvasive` (it's now chunk-level). Keep it pure data.

```csharp
public struct FloraEntry
{
    public Vector2 WorldPosition2D; // X/Z. Y resolved from terrain at render time.
    public byte ArchetypeId;        // Index into FloraArchetypeRegistry.
    public float Health;            // 0–1.
    public float Age;               // Simulation years.
    public float VariationSeed;     // Deterministic per-instance. Drives shader variation.
    public float RotationY;         // 0–360. Stored for determinism across loads.
}
```

All per-instance visual variation (hue offset, canopy fullness, lean) is derived from `VariationSeed` at render time — not stored.

---

### FloraPatch (Dense Shrub Placement Without Per-Instance Storage)

Rather than storing hundreds of individual shrub entries, patches store a descriptor that expands deterministically at chunk-build time.

```csharp
public struct FloraPatch
{
    public Vector2 Center;          // World XZ.
    public float Radius;            // Metres. Typical: 8–25m.
    public byte PrimaryArchetype;   // ArchetypeId for most instances.
    public byte SecondaryArchetype; // ArchetypeId for ~30% mix. 255 = none.
    public float Density;           // 0–1. Scaled by ChunkEcologyState at expand time.
    public uint Seed;               // Expansion is deterministic from this.
}
```

A 256m chunk typically carries 8–20 patches. At expand time each patch produces 15–60 instance transforms. These are never individually stored — regenerated from seed on every chunk load.

---

### BushveldBiomeRecipe (Data-Defined, Not Subclassed)

```csharp
public struct BushveldBiomeRecipe
{
    // Archetype probability weights [6 values, index = ArchetypeId]
    public float[] StructuralWeights;   // Probabilities for structural tree selection.
    public float[] PatchWeights;        // Probabilities for patch archetype selection.

    public float StructuralDensityBase; // Trees per hectare at neutral ecology.
    public float PatchCountBase;        // Patches per 256m chunk at neutral ecology.
    public float PatchRadiusMin;
    public float PatchRadiusMax;

    // Ecology response multipliers
    public float DroughtDensityScale;   // Multiplied against density when drought > 0.5.
    public float GrazingPatchSuppression; // How much grazing pressure shrinks patch density.
    public float EncroachmentBoost;     // Patch density multiplier when encroachment is high.

    // Palette (passed to shader via instance custom data)
    public Color HealthyHue;            // Base foliage hue, full moisture.
    public Color DrySeasonHue;          // Dry season shift.
    public Color DroughtHue;            // Severe drought.
    public Color BurnRecoveryHue;       // Fresh regrowth flush.
}
```

> **TODO — future biomes:** Karoo recipe, Highveld recipe, Riverine recipe. Each is a new `BushveldBiomeRecipe` instance — no new classes required.

The v1 Bushveld recipe target palette:
- **Wet season / healthy:** Warm olive-green (#8BA05A), deep earth trunks (#5C3A1E)
- **Dry season:** Dusty khaki (#B8A05A), bleached buff grass (#D4C27A)
- **Drought stress:** Ashy yellow-brown (#C4A855), thinned canopies
- **Post-burn recovery:** Sharp acid green flush (#7DC455) on black stems

---

### Shader Custom Data (4 Floats Per Instance)

Pack into `MultiMesh` custom data channel. One value per float:

| Channel | Name | Range | Meaning |
|---|---|---|---|
| 0 | `HueOffset` | −0.08 to +0.08 | Per-instance hue variation |
| 1 | `Dryness` | 0–1 | Blend toward dry-season palette |
| 2 | `BurnTint` | 0–1 | Blend toward blackened/recovering state |
| 3 | `CanopyFill` | 0.3–1 | Foliage density (less in drought/stress) |

This gives full visual range — drought, burn aftermath, recovery flush, invasive hue shift — from a single shader and no unique materials.

---

### Generation Pipeline (Per Chunk)

Replace the current `rng.RandiRange(80, 250)` scatter with a structured pipeline:

**Step 1 — Sample environment inputs**  
Read: terrain height distribution, slope variance, water proximity float, disturbance factor, world seed + chunk seed.

**Step 2 — Initialize ChunkEcologyState**  
Start at neutral values. Apply moisture from water proximity. Apply disturbance. For new games these are seeded values; for loaded games, use persisted state.

**Step 3 — Place structural anchors (Structural layer)**  
Use Poisson-disc sampling — not uniform random scatter. Min separation varies by archetype (FlatThorn needs 12m clearance, UprightDryland needs 6m). Count driven by `BushveldBiomeRecipe.StructuralDensityBase` × chunk ecology modifiers.

Poisson-disc is essential for the visual goal. Uniform random scatter produces the characteristic "AI slop" look — clumps and voids with no ecological logic. Bushveld trees are spatially regulated by root competition.

**Step 4 — Generate shrub patches (Patch layer)**  
Place patch centers using a second Poisson-disc pass with larger min separation (20m). Patches cluster more in drainage areas (moisture > 0.5) and open areas (no structural anchor within 8m).

**Step 5 — Suppression resolve**  
- Patches within 6m of a structural FlatThorn: reduce density by 0.4 (shade suppression)  
- GrazingPressure > 0.6: patches suppressed, structural trees unchanged  
- BurnAge < 0.2: all density × 0.2, recovery flush colour enabled  

**Step 6 — Cache result**  
Store structural `List<FloraEntry>` and `List<FloraPatch>`. Patches are not expanded yet — that happens in `FloraPopulator` on chunk load.

---

### FloraSystem — Public API Contract

Keep these signatures stable (callers depend on them):

```csharp
public static FloraSystem Instance { get; }
public List<FloraEntry> GetFloraForChunk(ChunkCoord coord);       // Existing callers safe.
public bool HasFloraForChunk(ChunkCoord coord);
public List<FloraEntry> GetFloraInRadius(Vector3 center, float radius);
```

Add these without breaking anything:

```csharp
public ChunkEcologyState GetChunkEcology(ChunkCoord coord);
public void SetChunkEcology(ChunkCoord coord, ChunkEcologyState state); // From GrazingSystem etc.
public List<FloraPatch> GetPatchesForChunk(ChunkCoord coord);
public void OnDailyTick();                                          // Called by TimeSystem.
public void OnMonthlyTick();                                        // Called by TimeSystem.
public void OnSeasonChanged(Season season);                         // Called by TimeSystem.
public void ApplyBurnEvent(Vector3 center, float radius);           // Called by fire system.
```

---

### FloraPopulator — Render Side

**Remove all placeholder mesh generation.** Reference GLBs by archetype ID:

```csharp
// Mesh paths by archetype ID — update when assets land.
private static readonly string[] MeshPaths = {
    "res://Assets/Flora/flat_thorn.glb",        // 0 FlatThorn
    "res://Assets/Flora/upright_dryland.glb",   // 1 UprightDryland
    "res://Assets/Flora/round_landmark.glb",    // 2 RoundLandmark
    "res://Assets/Flora/dense_thorn_shrub.glb", // 3 DenseThornShrub
    "res://Assets/Flora/low_dry_bush.glb",      // 4 LowDryBush
    "res://Assets/Flora/dead_snag.glb",         // 5 DeadSnag
};
```

Until GLBs exist, use a single shared low-poly placeholder per archetype category (structural vs patch). Not species-accurate placeholders — one box, one cross-plane. Coded once, used for all archetypes in that category.

**Build two MultiMesh sets per chunk:**

```
Structural pass:
  → 1 MultiMesh per archetype present (max 4 in Bushveld v1)
  → Instances from expanded FloraEntry list

Patch pass:
  → 1 MultiMesh per archetype present (max 2 in Bushveld v1)
  → Instances from FloraPatch expansion (deterministic from patch seed)

Total per chunk: max 6 MultiMesh nodes.
Total for 3×3 loaded grid: max 54 draw calls from flora. Well under budget.
```

**Custom data per instance** (pack using ChunkEcologyState + FloraEntry.VariationSeed):

```csharp
float hueOffset   = (entry.VariationSeed % 100f / 100f - 0.5f) * 0.16f;
float dryness     = Mathf.Clamp(ecology.DroughtStress * 0.7f + (season == Season.Winter ? 0.3f : 0f), 0f, 1f);
float burnTint    = ecology.BurnAge >= 0f ? Mathf.Clamp(1f - ecology.BurnAge, 0f, 1f) : 0f;
float canopyFill  = Mathf.Clamp(1f - ecology.DroughtStress * 0.6f, 0.3f, 1f);
multiMesh.SetInstanceCustomData(i, new Color(hueOffset, dryness, burnTint, canopyFill));
```

**LOD policy (relative to chunk size, not invented):**

| LOD | Distance | Form |
|---|---|---|
| Hero | 0–35m | Full mesh, wind shader active |
| Near | 35–90m | Full mesh, simplified wind |
| Mid | 90–220m | Same mesh, no wind, no alpha detail |
| Billboard | 220–450m | Impostor billboard (1 per structural instance, cluster card for patches) |
| Cull | > 450m | Not rendered. Chunk colour blend handles visual continuity. |

---

### File Structure (7 files, not 15)

```
FloraSystem.cs          — Singleton, data storage, public API, simulation ticks.
ChunkEcologyState.cs    — Struct only. Serializable.
FloraEntry.cs           — Struct only. Slim (no render logic).
FloraPatch.cs           — Struct only.
BushveldBiomeRecipe.cs  — Recipe struct + static Bushveld() factory method.
FloraGenerator.cs       — Poisson-disc placement, patch generation, suppression logic.
FloraPopulator.cs       — Render only. MultiMesh construction. Patch expansion. LOD.
```

Season/weather/grass integration lives as methods in `FloraSystem.cs` called by the existing systems — not separate bridge files.

---

### What Gets Deleted

| File | Fate |
|---|---|
| `FloraSystem.cs` | Full rewrite |
| `FloraEntry.cs` | Full rewrite (remove IsTree, GetVisualRadius, IsInvasive) |
| `FloraPopulator.cs` | Full rewrite (remove all placeholder mesh generation) |
| `FloraPopulator.cs` (root-level duplicate if present) | Delete |
| `FloraType` enum | Replaced by `byte ArchetypeId` + `FloraArchetypeRegistry` |
| All `CreateSimpleMesh*` methods | Delete entirely |

---

### Simulation Rules (Cheap, Monthly Tick)

These run in `OnMonthlyTick()` — no `_Process()`, no per-plant loops:

```
Moisture:
  moisture += rainfall_this_month * 0.3f
  moisture -= base_evaporation (0.08f/month)
  moisture = clamp(moisture + waterProximityBonus, 0, 1)

DroughtStress:
  if moisture < 0.2f → droughtStress += 0.12f
  if moisture > 0.5f → droughtStress -= 0.08f
  droughtStress = clamp(droughtStress, 0, 1)

GrazingPressure:
  Set by GrazingSystem.SetGrazingLoad(ChunkCoord, float)
  Decays naturally: grazingPressure -= 0.05f/month if no animals present

BurnAge:
  if burnAge >= 0 → burnAge += 0.04f/month (full recovery ~25 months)
  if burnAge >= 1 → burnAge = 1

InvasivePressure:
  if grazingPressure > 0.5f && moisture > 0.3f → invasivePressure += 0.03f/month
  if managed (future: herbicide event) → invasivePressure -= 0.15f
  invasivePressure = clamp(invasivePressure, 0, 1)

ShrubEncroachment:
  if grazingPressure < 0.2f && moisture > 0.4f → encroachment += 0.02f/month
  if grazingPressure > 0.6f → encroachment -= 0.04f/month
  encroachment = clamp(encroachment, 0, 1)
```

On `OnSeasonChanged()`: broadcast new season to all cached `ChunkEcologyState` entries. `RecoveryFactor` spikes to 0.8 at first summer rain, decays to 0 over 2 months.

---

### Visual Test Checklist (Ship Criteria)

Before the new flora system is considered done, every item below must pass:

- [ ] Standing at the bakkie's door, can you distinguish open veld from dense thornveld by silhouette alone?
- [ ] After a burn event, does the affected area look visually distinct (blackened, sparse) for at least 2 in-game seasons?
- [ ] Does a drought year produce visibly drier, thinner, yellower veld compared to a good rain year?
- [ ] Does a heavily grazed camp look different from a rested camp?
- [ ] Does the riverine drainage line look greener and denser than the surrounding hillside?
- [ ] Can you drive the bakkie at full speed through a 3×3 chunk traversal with no hitching?
- [ ] Do all 6 archetypes have readable silhouettes at dusk light?
- [ ] Does `GetFloraForChunk` produce identical results given the same seed + ecology state across two calls?

---

### Implementation Order

1. **Write new structs first** — `ChunkEcologyState`, `FloraEntry` (slim), `FloraPatch`, `BushveldBiomeRecipe`. Compile.
2. **Rewrite `FloraGenerator`** — Poisson-disc placement, patch generation, suppression. No rendering yet.
3. **Rewrite `FloraSystem`** — New API, tick hooks, thread-safe storage. Old callers still compile via wrappers.
4. **Rewrite `FloraPopulator`** — MultiMesh builds from new data. GLB paths as stubs. Custom data packing.
5. **Wire TimeSystem hooks** — `OnDailyTick`, `OnMonthlyTick`, `OnSeasonChanged`.
6. **Shader pass** — Implement the 4-channel custom data reads in the woody flora shader.
7. **Palette tuning** — Get the Bushveld dry vs wet season palette right before adding any other biome.
8. **Visual test pass** — Run checklist above. Fix before moving on.

---

*Last updated: April 2026. Written for BasterBoerv3 / Godot 4.6 / C# (.NET).*  
*This document is the authoritative design spec for the flora rewrite. All implementation decisions not covered here should default to the principle: simulate lean, render cheap, make the land readable.*
