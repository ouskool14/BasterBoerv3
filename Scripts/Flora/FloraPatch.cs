using System;
using System.Collections.Generic;
using Godot;

namespace WorldStreaming.Flora
{
    /// <summary>
    /// Dense shrub placement descriptor. Rather than storing hundreds of individual 
    /// shrub entries, patches store a descriptor that expands deterministically at 
    /// chunk-build time.
    /// 
    /// A 256m chunk typically carries 8-20 patches. At expand time each patch produces 
    /// 15-60 instance transforms. These are never individually stored - regenerated from 
    /// seed on every chunk load.
    /// </summary>
    [Serializable]
    public struct FloraPatch
    {
        /// <summary>
        /// Center position in world XZ coordinates.
        /// </summary>
        public Vector2 Center;

        /// <summary>
        /// Radius in meters. Typical: 8-25m.
        /// </summary>
        public float Radius;

        /// <summary>
        /// Archetype ID for most instances in this patch.
        /// </summary>
        public byte PrimaryArchetype;

        /// <summary>
        /// Archetype ID for ~30% mix. 255 = none (pure primary).
        /// </summary>
        public byte SecondaryArchetype;

        /// <summary>
        /// Density from 0-1. Scaled by ChunkEcologyState at expand time.
        /// </summary>
        public float Density;

        /// <summary>
        /// Seed for deterministic expansion. Same seed = same instance positions.
        /// </summary>
        public uint Seed;

        /// <summary>
        /// Creates a new flora patch.
        /// </summary>
        public FloraPatch(Vector2 center, float radius, byte primaryArchetype, float density, uint seed, byte secondaryArchetype = 255)
        {
            Center = center;
            Radius = Mathf.Clamp(radius, 3f, 40f);
            PrimaryArchetype = primaryArchetype;
            SecondaryArchetype = secondaryArchetype;
            Density = Mathf.Clamp(density, 0f, 1f);
            Seed = seed;
        }

        /// <summary>
        /// Returns true if this patch has a secondary archetype mix.
        /// </summary>
        public bool HasSecondaryArchetype => SecondaryArchetype != 255 && SecondaryArchetype < FloraArchetypeIds.Count;

        /// <summary>
        /// Gets the secondary mix ratio (~30% if secondary present).
        /// </summary>
        public float SecondaryRatio => HasSecondaryArchetype ? 0.3f : 0f;

        /// <summary>
        /// Expands this patch into individual instance transforms.
        /// Called by FloraPopulator at chunk load time.
        /// </summary>
        /// <param name="ecologyState">Current chunk ecology for density scaling</param>
        /// <param name="chunkOrigin">World position of chunk origin</param>
        /// <returns>List of expanded entries with local positions</returns>
        public List<ExpandedPatchInstance> Expand(ChunkEcologyState ecologyState, Vector3 chunkOrigin)
        {
            var instances = new List<ExpandedPatchInstance>();

            // Scale density by ecology state
            float effectiveDensity = Density * ecologyState.GetDensityMultiplier();

            // Determine instance count based on patch area and density
            float area = Mathf.Pi * Radius * Radius;
            int baseInstanceCount = Mathf.FloorToInt(area * 0.15f); // ~1 instance per 20m² at full density
            int instanceCount = Mathf.FloorToInt(baseInstanceCount * effectiveDensity);
            instanceCount = Mathf.Clamp(instanceCount, 3, 60); // Clamp to reasonable range

            // Deterministic RNG from patch seed
            var rng = new RandomNumberGenerator();
            rng.Seed = Seed;

            for (int i = 0; i < instanceCount; i++)
            {
                // Random position within patch radius (using rejection sampling for uniform distribution)
                Vector2 localPos;
                int attempts = 0;
                do
                {
                    float angle = rng.Randf() * Mathf.Pi * 2f;
                    float dist = rng.Randf() * Radius;
                    localPos = new Vector2(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist);
                    attempts++;
                } while (localPos.Length() > Radius && attempts < 10);

                Vector2 worldPos = Center + localPos;

                // Determine archetype (primary or secondary)
                byte archetypeId = PrimaryArchetype;
                if (HasSecondaryArchetype && rng.Randf() < SecondaryRatio)
                {
                    archetypeId = SecondaryArchetype;
                }

                // Generate deterministic variation for this instance
                uint instanceSeed = Seed ^ (uint)(i * 2654435761u);

                // Random rotation
                float rotationY = (instanceSeed % 360);

                // Size variation
                float sizeVariation = 0.7f + rng.Randf() * 0.6f; // 0.7 to 1.3

                // Health variation (most plants healthy)
                float health = 0.6f + rng.Randf() * 0.4f;

                // Age variation
                float age = 2f + rng.Randf() * 8f;

                instances.Add(new ExpandedPatchInstance
                {
                    WorldPosition2D = worldPos,
                    LocalPosition = new Vector3(worldPos.X - chunkOrigin.X, 0, worldPos.Y - chunkOrigin.Z),
                    ArchetypeId = archetypeId,
                    RotationY = rotationY,
                    ScaleMultiplier = sizeVariation,
                    Health = health,
                    Age = age,
                    VariationSeed = instanceSeed % 1000f
                });
            }

            return instances;
        }

        /// <summary>
        /// Gets the minimum separation distance required for Poisson-disc placement.
        /// Patches need larger separation than individual trees.
        /// </summary>
        public static float GetMinSeparationDistance()
        {
            return 20f; // 20m minimum between patch centers
        }
    }

    /// <summary>
    /// An expanded instance from a FloraPatch. Created at chunk load time, not persisted.
    /// </summary>
    public struct ExpandedPatchInstance
    {
        /// <summary>
        /// World XZ position.
        /// </summary>
        public Vector2 WorldPosition2D;

        /// <summary>
        /// Local position relative to chunk origin.
        /// </summary>
        public Vector3 LocalPosition;

        /// <summary>
        /// Archetype ID for this instance.
        /// </summary>
        public byte ArchetypeId;

        /// <summary>
        /// Rotation around Y axis.
        /// </summary>
        public float RotationY;

        /// <summary>
        /// Scale multiplier.
        /// </summary>
        public float ScaleMultiplier;

        /// <summary>
        /// Health of this instance.
        /// </summary>
        public float Health;

        /// <summary>
        /// Age of this instance.
        /// </summary>
        public float Age;

        /// <summary>
        /// Variation seed for shader.
        /// </summary>
        public float VariationSeed;

        /// <summary>
        /// Converts this expanded instance to a FloraEntry.
        /// </summary>
        public FloraEntry ToFloraEntry()
        {
            return new FloraEntry(
                WorldPosition2D,
                ArchetypeId,
                Health,
                Age,
                (uint)VariationSeed
            )
            {
                RotationY = RotationY
            };
        }
    }
}
