using Godot;

namespace WorldStreaming.Flora
{
	/// <summary>
	/// Data-defined biome recipe for Bushveld flora generation.
	/// No subclassing - each biome is an instance of this struct.
	/// 
	/// TODO: Future biomes (Karoo, Highveld, Riverine) are additional instances,
	/// not new classes.
	/// </summary>
	public struct BushveldBiomeRecipe
	{
		// ── Archetype Probability Weights ─────────────────────────────────

		/// <summary>
		/// Probabilities for structural tree selection [6 values, index = ArchetypeId].
		/// Should sum to 1.0.
		/// </summary>
		public float[] StructuralWeights;

		/// <summary>
		/// Probabilities for patch archetype selection [6 values, index = ArchetypeId].
		/// Should sum to 1.0.
		/// </summary>
		public float[] PatchWeights;

		// ── Density Base Values ───────────────────────────────────────────

		/// <summary>
		/// Trees per hectare at neutral ecology.
		/// </summary>
		public float StructuralDensityBase;

		/// <summary>
		/// Patches per 256m chunk at neutral ecology.
		/// </summary>
		public float PatchCountBase;

		/// <summary>
		/// Minimum patch radius in meters.
		/// </summary>
		public float PatchRadiusMin;

		/// <summary>
		/// Maximum patch radius in meters.
		/// </summary>
		public float PatchRadiusMax;

		// ── Ecology Response Multipliers ──────────────────────────────────

		/// <summary>
		/// Multiplied against density when drought > 0.5.
		/// </summary>
		public float DroughtDensityScale;

		/// <summary>
		/// How much grazing pressure shrinks patch density.
		/// </summary>
		public float GrazingPatchSuppression;

		/// <summary>
		/// Patch density multiplier when encroachment is high.
		/// </summary>
		public float EncroachmentBoost;

		// ── Palette (passed to shader via instance custom data) ────────────

		/// <summary>
		/// Base foliage hue at full moisture.
		/// </summary>
		public Color HealthyHue;

		/// <summary>
		/// Dry season palette shift.
		/// </summary>
		public Color DrySeasonHue;

		/// <summary>
		/// Severe drought palette.
		/// </summary>
		public Color DroughtHue;

		/// <summary>
		/// Fresh regrowth flush palette.
		/// </summary>
		public Color BurnRecoveryHue;

		// ── Poisson-Disc Parameters ───────────────────────────────────────

		/// <summary>
		/// Minimum separation between structural trees (meters).
		/// Varies by archetype - this is the base value.
		/// </summary>
		public float TreeMinSeparationBase;

		/// <summary>
		/// Maximum attempts for Poisson-disc placement.
		/// </summary>
		public int MaxPlacementAttempts;

		/// <summary>
		/// Creates the default Bushveld biome recipe.
		/// Target palette:
		/// - Wet season / healthy: Warm olive-green (#8BA05A), deep earth trunks (#5C3A1E)
		/// - Dry season: Dusty khaki (#B8A05A), bleached buff grass (#D4C27A)
		/// - Drought stress: Ashy yellow-brown (#C4A855), thinned canopies
		/// - Post-burn recovery: Sharp acid green flush (#7DC455) on black stems
		/// </summary>
		public static BushveldBiomeRecipe CreateBushveld()
		{
			return new BushveldBiomeRecipe
			{
				// Structural weights: FlatThorn dominant, then UprightDryland, RoundLandmark, DeadSnag
				StructuralWeights = new float[] { 0.40f, 0.25f, 0.25f, 0f, 0f, 0.10f },

				// Patch weights: DenseThornShrub dominant, LowDryBush secondary
				PatchWeights = new float[] { 0f, 0f, 0f, 0.65f, 0.35f, 0f },

				// Density: ~150 trees per hectare in typical Bushveld
				StructuralDensityBase = 0.015f, // ~10 trees per 256m chunk

				// Patches: 12-18 per chunk
				PatchCountBase = 15f,
				PatchRadiusMin = 8f,
				PatchRadiusMax = 25f,

				// Ecology response
				DroughtDensityScale = 0.6f,
				GrazingPatchSuppression = 0.5f,
				EncroachmentBoost = 1.4f,

				// Palette
				HealthyHue = new Color(0.545f, 0.627f, 0.353f),      // #8BA05A - warm olive-green
				DrySeasonHue = new Color(0.722f, 0.627f, 0.353f),    // #B8A05A - dusty khaki
				DroughtHue = new Color(0.769f, 0.659f, 0.333f),      // #C4A855 - ashy yellow-brown
				BurnRecoveryHue = new Color(0.490f, 0.769f, 0.333f), // #7DC455 - acid green flush

				// Poisson-disc
				TreeMinSeparationBase = 8f,
				MaxPlacementAttempts = 30
			};
		}

		/// <summary>
		/// Creates a recipe for a drier, more open Bushveld variant.
		/// TODO: biome - use for rocky ridge areas
		/// </summary>
		public static BushveldBiomeRecipe CreateOpenBushveld()
		{
			var recipe = CreateBushveld();
			recipe.StructuralDensityBase = 0.008f; // Fewer trees
			recipe.PatchCountBase = 10f; // Fewer patches
			recipe.DroughtDensityScale = 0.4f; // More severe drought response
			return recipe;
		}

		/// <summary>
		/// Creates a recipe for denser thornveld.
		/// TODO: biome - use for drainage lines and protected areas
		/// </summary>
		public static BushveldBiomeRecipe CreateDenseThornveld()
		{
			var recipe = CreateBushveld();
			recipe.StructuralDensityBase = 0.025f; // More trees
			recipe.PatchCountBase = 22f; // More patches
			recipe.PatchWeights = new float[] { 0f, 0f, 0f, 0.80f, 0.20f, 0f }; // More DenseThornShrub
			return recipe;
		}

		/// <summary>
		/// Gets the minimum separation distance for a specific archetype.
		/// FlatThorn needs more clearance (wide canopy), UprightDryland needs less.
		/// </summary>
		public float GetMinSeparationForArchetype(byte archetypeId)
		{
			return archetypeId switch
			{
				0 => TreeMinSeparationBase * 1.5f, // FlatThorn: 12m
				1 => TreeMinSeparationBase * 0.75f, // UprightDryland: 6m
				2 => TreeMinSeparationBase * 1.25f, // RoundLandmark: 10m
				5 => TreeMinSeparationBase * 1.0f,  // DeadSnag: 8m
				_ => TreeMinSeparationBase
			};
		}

		/// <summary>
		/// Selects a random structural archetype based on weights.
		/// </summary>
		public byte SelectStructuralArchetype(RandomNumberGenerator rng)
		{
			float rand = rng.Randf();
			float cumulative = 0f;

			for (byte i = 0; i < FloraArchetypeIds.Count; i++)
			{
				cumulative += StructuralWeights[i];
				if (rand < cumulative)
					return i;
			}

			return 0; // Default to FlatThorn
		}

		/// <summary>
		/// Selects a random patch archetype based on weights.
		/// </summary>
		public byte SelectPatchArchetype(RandomNumberGenerator rng)
		{
			float rand = rng.Randf();
			float cumulative = 0f;

			for (byte i = 0; i < FloraArchetypeIds.Count; i++)
			{
				cumulative += PatchWeights[i];
				if (rand < cumulative)
					return i;
			}

			return 3; // Default to DenseThornShrub
		}

		/// <summary>
		/// Gets the effective structural density considering ecology state.
		/// </summary>
		public float GetEffectiveStructuralDensity(ChunkEcologyState ecology)
		{
			float density = StructuralDensityBase;

			// Drought reduces tree density slightly
			if (ecology.DroughtStress > 0.5f)
				density *= DroughtDensityScale;

			// Recent burn reduces density dramatically
			if (ecology.HasBeenBurned && ecology.BurnAge < 0.3f)
				density *= 0.3f;

			return density;
		}

		/// <summary>
		/// Gets the effective patch count considering ecology state.
		/// </summary>
		public float GetEffectivePatchCount(ChunkEcologyState ecology)
		{
			float count = PatchCountBase;

			// Drought reduces patches
			if (ecology.DroughtStress > 0.5f)
				count *= DroughtDensityScale;

			// Grazing suppresses patches
			if (ecology.GrazingPressure > 0.5f)
				count *= (1f - GrazingPatchSuppression * (ecology.GrazingPressure - 0.5f) * 2f);

			// Encroachment boosts patches
			if (ecology.ShrubEncroachment > 0.5f)
				count *= EncroachmentBoost;

			// Recent burn reduces patches
			if (ecology.HasBeenBurned && ecology.BurnAge < 0.2f)
				count *= 0.2f;

			return Mathf.Max(count, 3f); // Minimum 3 patches
		}
	}
}
