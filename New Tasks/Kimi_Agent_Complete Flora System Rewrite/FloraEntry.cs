using System;
using Godot;

namespace WorldStreaming.Flora
{
    /// <summary>
    /// Archetype IDs for the 6 Bushveld flora archetypes.
    /// These are visual archetypes, not botanical species.
    /// </summary>
    public static class FloraArchetypeIds
    {
        public const byte FlatThorn = 0;        // Wide flat canopy, sparse, iconic silhouette
        public const byte UprightDryland = 1;   // Narrow tall form, thin canopy, open
        public const byte RoundLandmark = 2;    // Full round canopy, landmark presence
        public const byte DenseThornShrub = 3;  // Compact armed shrub, massing form
        public const byte LowDryBush = 4;       // Sprawling low bush, dry palette
        public const byte DeadSnag = 5;         // Dry skeleton, accent/death marker

        public const byte Count = 6;

        /// <summary>
        /// Returns true if the archetype is a structural tree (vs patch shrub).
        /// </summary>
        public static bool IsStructural(byte archetypeId)
        {
            return archetypeId <= 2 || archetypeId == 5; // FlatThorn, UprightDryland, RoundLandmark, DeadSnag
        }

        /// <summary>
        /// Returns true if the archetype is a patch shrub.
        /// </summary>
        public static bool IsPatch(byte archetypeId)
        {
            return archetypeId == 3 || archetypeId == 4; // DenseThornShrub, LowDryBush
        }

        /// <summary>
        /// Gets the display name for an archetype.
        /// </summary>
        public static string GetDisplayName(byte archetypeId)
        {
            return archetypeId switch
            {
                0 => "Flat-Top Thorn",
                1 => "Upright Dryland Tree",
                2 => "Round Landmark Tree",
                3 => "Dense Thorn Shrub",
                4 => "Low Dry Bush",
                5 => "Dead Snag",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Gets example species names for an archetype (for UI/display).
        /// </summary>
        public static string GetExampleSpecies(byte archetypeId)
        {
            return archetypeId switch
            {
                0 => "Knob-thorn, Camel-thorn",
                1 => "Shepherd's tree, Buffalo-thorn",
                2 => "Marula, Apple-leaf",
                3 => "Sickle-bush, Magic guarri",
                4 => "Raisin bush, Wild rosemary",
                5 => "Standing deadwood",
                _ => "Unknown"
            };
        }
    }

    /// <summary>
    /// Pure simulation data for a single structural plant in the world.
    /// No render logic - purely C# data structure.
    /// 
    /// Replaces the old FloraEntry which had IsTree(), GetVisualRadius(), and IsInvasive.
    /// All render concerns moved to FloraPopulator.
    /// All invasive state moved to ChunkEcologyState.InvasivePressure.
    /// </summary>
    [Serializable]
    public struct FloraEntry
    {
        /// <summary>
        /// 2D world position (X, Z plane). Y resolved from terrain at render time.
        /// </summary>
        public Vector2 WorldPosition2D;

        /// <summary>
        /// Archetype ID (0-5). Index into FloraArchetypeRegistry.
        /// Replaces the old FloraType enum.
        /// </summary>
        public byte ArchetypeId;

        /// <summary>
        /// Health from 0 (dead) to 1 (thriving).
        /// </summary>
        public float Health;

        /// <summary>
        /// Age in simulation years.
        /// </summary>
        public float Age;

        /// <summary>
        /// Deterministic per-instance variation seed. Drives shader variation.
        /// All per-instance visual variation (hue offset, canopy fullness, lean) 
        /// is derived from this at render time - not stored.
        /// </summary>
        public float VariationSeed;

        /// <summary>
        /// Rotation around Y axis in degrees (0-360). Stored for determinism across loads.
        /// </summary>
        public float RotationY;

        /// <summary>
        /// Creates a new flora entry with deterministic variation.
        /// </summary>
        public FloraEntry(Vector2 worldPosition2D, byte archetypeId, float health = 1f, float age = 5f, uint deterministicSeed = 0)
        {
            WorldPosition2D = worldPosition2D;
            ArchetypeId = archetypeId;
            Health = Mathf.Clamp(health, 0f, 1f);
            Age = Mathf.Max(0f, age);

            // Generate deterministic variation seed from position if not provided
            if (deterministicSeed == 0)
            {
                deterministicSeed = (uint)(
                    (uint)(worldPosition2D.X * 73856093) ^
                    (uint)(worldPosition2D.Y * 19349663)
                );
            }
            VariationSeed = deterministicSeed % 1000f;

            // Deterministic rotation based on position for consistency
            RotationY = (deterministicSeed % 360);
        }

        /// <summary>
        /// Gets the scale multiplier based on age and health.
        /// </summary>
        public float GetScaleMultiplier()
        {
            float ageScale = Mathf.Clamp(Age / 10f, 0.3f, 1.2f);
            float healthScale = Mathf.Lerp(0.6f, 1f, Health);
            float variation = 0.8f + ((VariationSeed % 100f) / 100f * 0.4f); // 0.8 to 1.2
            return ageScale * healthScale * variation;
        }

        /// <summary>
        /// Returns true if this entry is valid (has a recognized archetype).
        /// </summary>
        public bool IsValid => ArchetypeId < FloraArchetypeIds.Count;

        /// <summary>
        /// Returns true if this is a structural tree entry.
        /// </summary>
        public bool IsStructural => FloraArchetypeIds.IsStructural(ArchetypeId);

        /// <summary>
        /// Returns true if this is a patch shrub entry.
        /// </summary>
        public bool IsPatch => FloraArchetypeIds.IsPatch(ArchetypeId);
    }
}
