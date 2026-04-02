using System.Collections.Generic;
using Godot;
using LandManagementSim.Terrain;

namespace WorldStreaming.Flora
{
    /// <summary>
    /// Generates flora placement for chunks using Poisson-disc sampling.
    /// Separated from FloraSystem for testability and clarity.
    /// 
    /// Generation pipeline (per chunk):
    /// 1. Sample environment inputs (terrain, moisture, disturbance)
    /// 2. Initialize ChunkEcologyState
    /// 3. Place structural anchors (trees) using Poisson-disc
    /// 4. Generate shrub patches using second Poisson-disc pass
    /// 5. Apply suppression resolve
    /// 6. Cache structural entries and patches
    /// </summary>
    public static class FloraGenerator
    {
        /// <summary>
        /// Generates flora data for a chunk.
        /// </summary>
        public static FloraGenerationResult GenerateChunkFlora(
            ChunkCoord coord,
            float chunkSize,
            BushveldBiomeRecipe recipe,
            ChunkEcologyState ecologyState,
            ulong worldSeed)
        {
            var result = new FloraGenerationResult
            {
                Coordinate = coord,
                EcologyState = ecologyState
            };

            // Create deterministic RNG for this chunk
            var rng = new RandomNumberGenerator();
            rng.Seed = (ulong)coord.GetHashCode() ^ worldSeed;

            Vector3 chunkOrigin = coord.GetWorldOrigin(chunkSize);

            // Step 1: Sample environment inputs and adjust ecology
            ecologyState = SampleEnvironmentAndAdjustEcology(coord, chunkSize, ecologyState, rng);
            result.EcologyState = ecologyState;

            // Step 2: Place structural anchors (trees)
            result.StructuralEntries = PlaceStructuralFlora(
                coord, chunkSize, recipe, ecologyState, rng, chunkOrigin);

            // Step 3: Generate shrub patches
            result.Patches = GeneratePatches(
                coord, chunkSize, recipe, ecologyState, rng, chunkOrigin);

            // Step 4: Suppression resolve
            ApplySuppressionResolve(result.StructuralEntries, result.Patches, ecologyState);

            return result;
        }

        /// <summary>
        /// Samples terrain and adjusts ecology state based on local conditions.
        /// </summary>
        private static ChunkEcologyState SampleEnvironmentAndAdjustEcology(
            ChunkCoord coord,
            float chunkSize,
            ChunkEcologyState baseEcology,
            RandomNumberGenerator rng)
        {
            Vector3 chunkCenter = coord.GetWorldCenter(chunkSize);

            // Sample terrain at chunk center
            float wetness = TerrainQuery.GetWetness(chunkCenter);
            float slope = TerrainQuery.GetSlope(chunkCenter);
            float rockiness = TerrainQuery.GetRockiness(chunkCenter);

            // Adjust moisture based on terrain wetness
            baseEcology.Moisture = Mathf.Lerp(baseEcology.Moisture, wetness, 0.5f);

            // Rocky areas have less vegetation
            if (rockiness > 0.6f)
            {
                baseEcology.Moisture *= 0.7f;
                baseEcology.DroughtStress += 0.1f;
            }

            // Steep slopes have different vegetation patterns
            if (slope > 25f)
            {
                // Steep slopes = more open, fewer shrubs
                baseEcology.ShrubEncroachment *= 0.5f;
            }

            // Clamp all values
            baseEcology.Moisture = Mathf.Clamp(baseEcology.Moisture, 0f, 1f);
            baseEcology.DroughtStress = Mathf.Clamp(baseEcology.DroughtStress, 0f, 1f);
            baseEcology.ShrubEncroachment = Mathf.Clamp(baseEcology.ShrubEncroachment, 0f, 1f);

            return baseEcology;
        }

        /// <summary>
        /// Places structural flora (trees) using Poisson-disc sampling.
        /// </summary>
        private static List<FloraEntry> PlaceStructuralFlora(
            ChunkCoord coord,
            float chunkSize,
            BushveldBiomeRecipe recipe,
            ChunkEcologyState ecology,
            RandomNumberGenerator rng,
            Vector3 chunkOrigin)
        {
            var entries = new List<FloraEntry>();

            // Calculate target count based on recipe and ecology
            float densityPerChunk = recipe.GetEffectiveStructuralDensity(ecology) * chunkSize * chunkSize;
            int targetCount = Mathf.FloorToInt(densityPerChunk);
            targetCount = Mathf.Clamp(targetCount, 3, 25); // Reasonable range for 256m chunk

            // Poisson-disc sampling
            var points = new List<Vector2>();
            var activeList = new List<Vector2>();

            // Start with a random point
            Vector2 firstPoint = new Vector2(
                rng.Randf() * chunkSize,
                rng.Randf() * chunkSize
            );
            points.Add(firstPoint);
            activeList.Add(firstPoint);

            while (activeList.Count > 0 && points.Count < targetCount * 2)
            {
                // Pick random point from active list
                int activeIndex = rng.RandiRange(0, activeList.Count - 1);
                Vector2 currentPoint = activeList[activeIndex];

                bool foundNewPoint = false;

                // Try to find a new point around current point
                for (int attempt = 0; attempt < recipe.MaxPlacementAttempts; attempt++)
                {
                    // Select archetype first to determine min separation
                    byte archetypeId = recipe.SelectStructuralArchetype(rng);
                    float minDist = recipe.GetMinSeparationForArchetype(archetypeId);

                    // Random angle and distance
                    float angle = rng.Randf() * Mathf.Pi * 2f;
                    float distance = minDist * (1f + rng.Randf()); // 1-2x min distance

                    Vector2 newPoint = currentPoint + new Vector2(
                        Mathf.Cos(angle) * distance,
                        Mathf.Sin(angle) * distance
                    );

                    // Check bounds
                    if (newPoint.X < 0 || newPoint.X >= chunkSize ||
                        newPoint.Y < 0 || newPoint.Y >= chunkSize)
                    {
                        continue;
                    }

                    // Check distance to all existing points
                    bool tooClose = false;
                    foreach (var existingPoint in points)
                    {
                        if (newPoint.DistanceTo(existingPoint) < minDist)
                        {
                            tooClose = true;
                            break;
                        }
                    }

                    if (!tooClose)
                    {
                        points.Add(newPoint);
                        activeList.Add(newPoint);
                        foundNewPoint = true;

                        // Create the entry
                        Vector2 worldPos = new Vector2(
                            chunkOrigin.X + newPoint.X,
                            chunkOrigin.Z + newPoint.Y
                        );

                        float health = 0.6f + rng.Randf() * 0.4f;
                        float age = 3f + rng.Randf() * 12f;

                        entries.Add(new FloraEntry(worldPos, archetypeId, health, age));

                        if (points.Count >= targetCount)
                            break;
                    }
                }

                if (!foundNewPoint)
                {
                    activeList.RemoveAt(activeIndex);
                }

                if (points.Count >= targetCount)
                    break;
            }

            return entries;
        }

        /// <summary>
        /// Generates shrub patches using Poisson-disc sampling with larger separation.
        /// </summary>
        private static List<FloraPatch> GeneratePatches(
            ChunkCoord coord,
            float chunkSize,
            BushveldBiomeRecipe recipe,
            ChunkEcologyState ecology,
            RandomNumberGenerator rng,
            Vector3 chunkOrigin)
        {
            var patches = new List<FloraPatch>();

            // Calculate target patch count
            float targetCount = recipe.GetEffectivePatchCount(ecology);
            int patchCount = Mathf.FloorToInt(targetCount);
            patchCount = Mathf.Clamp(patchCount, 5, 25);

            float minSeparation = FloraPatch.GetMinSeparationDistance();

            // Poisson-disc sampling for patch centers
            var points = new List<Vector2>();
            var activeList = new List<Vector2>();

            // Start with a random point
            Vector2 firstPoint = new Vector2(
                rng.Randf() * chunkSize,
                rng.Randf() * chunkSize
            );
            points.Add(firstPoint);
            activeList.Add(firstPoint);

            // Create first patch
            patches.Add(CreatePatchAtPoint(firstPoint, chunkOrigin, recipe, ecology, rng));

            while (activeList.Count > 0 && patches.Count < patchCount)
            {
                int activeIndex = rng.RandiRange(0, activeList.Count - 1);
                Vector2 currentPoint = activeList[activeIndex];

                bool foundNewPoint = false;

                for (int attempt = 0; attempt < recipe.MaxPlacementAttempts; attempt++)
                {
                    float angle = rng.Randf() * Mathf.Pi * 2f;
                    float distance = minSeparation * (1f + rng.Randf() * 0.5f);

                    Vector2 newPoint = currentPoint + new Vector2(
                        Mathf.Cos(angle) * distance,
                        Mathf.Sin(angle) * distance
                    );

                    // Check bounds (with margin for patch radius)
                    float margin = recipe.PatchRadiusMax;
                    if (newPoint.X < margin || newPoint.X >= chunkSize - margin ||
                        newPoint.Y < margin || newPoint.Y >= chunkSize - margin)
                    {
                        continue;
                    }

                    // Check distance to all existing points
                    bool tooClose = false;
                    foreach (var existingPoint in points)
                    {
                        if (newPoint.DistanceTo(existingPoint) < minSeparation)
                        {
                            tooClose = true;
                            break;
                        }
                    }

                    if (!tooClose)
                    {
                        points.Add(newPoint);
                        activeList.Add(newPoint);
                        foundNewPoint = true;

                        patches.Add(CreatePatchAtPoint(newPoint, chunkOrigin, recipe, ecology, rng));

                        if (patches.Count >= patchCount)
                            break;
                    }
                }

                if (!foundNewPoint)
                {
                    activeList.RemoveAt(activeIndex);
                }
            }

            return patches;
        }

        /// <summary>
        /// Creates a patch at the given local point.
        /// </summary>
        private static FloraPatch CreatePatchAtPoint(
            Vector2 localPoint,
            Vector3 chunkOrigin,
            BushveldBiomeRecipe recipe,
            ChunkEcologyState ecology,
            RandomNumberGenerator rng)
        {
            Vector2 worldPos = new Vector2(
                chunkOrigin.X + localPoint.X,
                chunkOrigin.Z + localPoint.Y
            );

            byte primaryArchetype = recipe.SelectPatchArchetype(rng);

            // 40% chance of secondary archetype mix
            byte secondaryArchetype = 255;
            if (rng.Randf() < 0.4f)
            {
                do
                {
                    secondaryArchetype = recipe.SelectPatchArchetype(rng);
                } while (secondaryArchetype == primaryArchetype);
            }

            float radius = recipe.PatchRadiusMin + rng.Randf() * (recipe.PatchRadiusMax - recipe.PatchRadiusMin);
            float density = 0.5f + rng.Randf() * 0.5f; // 0.5-1.0 base density

            uint seed = (uint)(worldPos.X * 73856093) ^ (uint)(worldPos.Y * 19349663);

            return new FloraPatch(worldPos, radius, primaryArchetype, density, seed, secondaryArchetype);
        }

        /// <summary>
        /// Applies suppression rules between structural trees and patches.
        /// </summary>
        private static void ApplySuppressionResolve(
            List<FloraEntry> structuralEntries,
            List<FloraPatch> patches,
            ChunkEcologyState ecology)
        {
            // Build spatial lookup for structural trees
            var treePositions = new List<(Vector2 pos, byte archetypeId)>();
            foreach (var entry in structuralEntries)
            {
                treePositions.Add((entry.WorldPosition2D, entry.ArchetypeId));
            }

            // Apply suppression to patches
            for (int i = patches.Count - 1; i >= 0; i--)
            {
                var patch = patches[i];
                bool shouldRemove = false;

                // Check distance to all structural trees
                foreach (var (treePos, archetypeId) in treePositions)
                {
                    float distance = patch.Center.DistanceTo(treePos);

                    // FlatThorn (archetype 0) casts wider shade
                    float suppressionRadius = (archetypeId == 0) ? 10f : 6f;

                    if (distance < suppressionRadius)
                    {
                        // Reduce patch density
                        patch.Density *= 0.6f;

                        // If density too low, mark for removal
                        if (patch.Density < 0.2f)
                        {
                            shouldRemove = true;
                            break;
                        }
                    }
                }

                // Grazing pressure suppresses patches
                if (ecology.GrazingPressure > 0.6f)
                {
                    patch.Density *= 0.5f;
                    if (patch.Density < 0.2f)
                    {
                        shouldRemove = true;
                    }
                }

                // Recent burn dramatically reduces patches
                if (ecology.HasBeenBurned && ecology.BurnAge < 0.2f)
                {
                    patch.Density *= 0.2f;
                    if (patch.Density < 0.15f)
                    {
                        shouldRemove = true;
                    }
                }

                if (shouldRemove)
                {
                    patches.RemoveAt(i);
                }
                else
                {
                    patches[i] = patch;
                }
            }
        }

        /// <summary>
        /// Regenerates flora for a chunk using existing ecology state.
        /// Used when reloading chunks (deterministic from seed + ecology).
        /// </summary>
        public static FloraGenerationResult RegenerateChunkFlora(
            ChunkCoord coord,
            float chunkSize,
            BushveldBiomeRecipe recipe,
            ChunkEcologyState ecologyState,
            ulong worldSeed)
        {
            // Same as GenerateChunkFlora - deterministic from inputs
            return GenerateChunkFlora(coord, chunkSize, recipe, ecologyState, worldSeed);
        }
    }

    /// <summary>
    /// Result of flora generation for a chunk.
    /// </summary>
    public struct FloraGenerationResult
    {
        public ChunkCoord Coordinate;
        public ChunkEcologyState EcologyState;
        public List<FloraEntry> StructuralEntries;
        public List<FloraPatch> Patches;

        /// <summary>
        /// Gets total instance count (structural + estimated from patches).
        /// </summary>
        public int GetEstimatedInstanceCount()
        {
            int patchInstances = 0;
            foreach (var patch in Patches)
            {
                float area = Mathf.Pi * patch.Radius * patch.Radius;
                patchInstances += Mathf.FloorToInt(area * 0.15f * patch.Density);
            }
            return StructuralEntries.Count + patchInstances;
        }
    }
}
