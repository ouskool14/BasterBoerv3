using System;
using BasterBoer.Core.Time;
using Godot;

namespace WorldStreaming.Flora
{
    /// <summary>
    /// Per-chunk ecological state. This is the only flora-related data persisted to save files.
    /// Deterministic chunks are rebuilt from seed + this state.
    /// 
    /// Contains 7 floats + 1 season enum as specified in the flora replacement plan.
    /// </summary>
    [Serializable]
    public struct ChunkEcologyState
    {
        /// <summary>
        /// Moisture level from 0-1. Driven by rainfall + water proximity.
        /// </summary>
        public float Moisture;

        /// <summary>
        /// Drought stress from 0-1. Accumulates in dry months, reduces effective moisture.
        /// </summary>
        public float DroughtStress;

        /// <summary>
        /// Grazing pressure from 0-1. Set by GrazingSystem. Suppresses patch density.
        /// </summary>
        public float GrazingPressure;

        /// <summary>
        /// Burn age from 0-1. 0 = freshly burned, 1 = fully recovered. -1 = never burned.
        /// </summary>
        public float BurnAge;

        /// <summary>
        /// Invasive pressure from 0-1. Grows on disturbed/grazed ground. Shifts shrub hue.
        /// </summary>
        public float InvasivePressure;

        /// <summary>
        /// Shrub encroachment from 0-1. Bush thickening when grazing pressure is low.
        /// </summary>
        public float ShrubEncroachment;

        /// <summary>
        /// Recovery factor from 0-1. Post-rain/post-burn green flush multiplier.
        /// </summary>
        public float RecoveryFactor;

        /// <summary>
        /// Current season propagated from TimeSystem on season change.
        /// </summary>
        public Season CurrentSeason;

        /// <summary>
        /// Creates a neutral ecology state for new chunks.
        /// </summary>
        public static ChunkEcologyState CreateNeutral(Season season = Season.Spring)
        {
            return new ChunkEcologyState
            {
                Moisture = 0.5f,
                DroughtStress = 0f,
                GrazingPressure = 0f,
                BurnAge = -1f, // Never burned
                InvasivePressure = 0f,
                ShrubEncroachment = 0f,
                RecoveryFactor = 0f,
                CurrentSeason = season
            };
        }

        /// <summary>
        /// Creates an ecology state for a drainage line (higher moisture).
        /// </summary>
        public static ChunkEcologyState CreateDrainageLine(Season season = Season.Spring)
        {
            return new ChunkEcologyState
            {
                Moisture = 0.75f,
                DroughtStress = 0f,
                GrazingPressure = 0f,
                BurnAge = -1f,
                InvasivePressure = 0.1f,
                ShrubEncroachment = 0.2f,
                RecoveryFactor = 0.3f,
                CurrentSeason = season
            };
        }

        /// <summary>
        /// Creates an ecology state for a recently burned area.
        /// </summary>
        public static ChunkEcologyState CreatePostBurn(Season season = Season.Spring)
        {
            return new ChunkEcologyState
            {
                Moisture = 0.3f,
                DroughtStress = 0.2f,
                GrazingPressure = 0f,
                BurnAge = 0f, // Freshly burned
                InvasivePressure = 0f,
                ShrubEncroachment = 0f,
                RecoveryFactor = 0.8f, // Strong recovery flush
                CurrentSeason = season
            };
        }

        /// <summary>
        /// Creates an ecology state for heavily grazed land.
        /// </summary>
        public static ChunkEcologyState CreateHeavilyGrazed(Season season = Season.Spring)
        {
            return new ChunkEcologyState
            {
                Moisture = 0.4f,
                DroughtStress = 0.3f,
                GrazingPressure = 0.85f,
                BurnAge = -1f,
                InvasivePressure = 0.4f,
                ShrubEncroachment = 0f,
                RecoveryFactor = 0.1f,
                CurrentSeason = season
            };
        }

        /// <summary>
        /// Returns true if this chunk has been burned at least once.
        /// </summary>
        public bool HasBeenBurned => BurnAge >= 0f;

        /// <summary>
        /// Gets effective moisture considering drought stress.
        /// </summary>
        public float GetEffectiveMoisture()
        {
            return Mathf.Clamp(Moisture * (1f - DroughtStress * 0.5f), 0f, 1f);
        }

        /// <summary>
        /// Gets the density multiplier based on ecological factors.
        /// </summary>
        public float GetDensityMultiplier()
        {
            float multiplier = 1f;

            // Drought reduces density
            if (DroughtStress > 0.5f)
                multiplier *= 0.7f;

            // Grazing suppresses patches
            if (GrazingPressure > 0.6f)
                multiplier *= 0.5f;

            // Recent burn dramatically reduces density
            if (HasBeenBurned && BurnAge < 0.2f)
                multiplier *= 0.2f;

            // Recovery flush increases density
            multiplier *= 0.7f + RecoveryFactor * 0.3f;

            return Mathf.Clamp(multiplier, 0.1f, 1.2f);
        }

        /// <summary>
        /// Gets the hue shift based on invasive pressure and drought stress.
        /// </summary>
        public float GetHueShift()
        {
            // Invasive pressure shifts toward reddish hue
            float invasiveShift = InvasivePressure * 0.08f;

            // Drought shifts toward yellow-brown
            float droughtShift = DroughtStress * -0.05f;

            return invasiveShift + droughtShift;
        }

        /// <summary>
        /// Gets the dryness value for shader (0-1, higher = drier).
        /// </summary>
        public float GetDryness()
        {
            float dryness = DroughtStress * 0.7f;

            // Winter is drier in Bushveld
            if (CurrentSeason == Season.Winter)
                dryness += 0.3f;

            return Mathf.Clamp(dryness, 0f, 1f);
        }

        /// <summary>
        /// Gets the burn tint for shader (0-1, higher = more blackened).
        /// </summary>
        public float GetBurnTint()
        {
            if (!HasBeenBurned)
                return 0f;

            return Mathf.Clamp(1f - BurnAge, 0f, 1f);
        }

        /// <summary>
        /// Gets the canopy fill for shader (0.3-1, lower = thinner canopies).
        /// </summary>
        public float GetCanopyFill()
        {
            return Mathf.Clamp(1f - DroughtStress * 0.6f, 0.3f, 1f);
        }
    }
}
