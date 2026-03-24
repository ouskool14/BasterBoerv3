using System;

namespace LandManagementSim.Simulation
{
	/// <summary>
	/// Compact genetic profile representing an animal's inherited traits.
	/// All values normalized to 0.0-1.0 range for easy interpolation and comparison.
	/// </summary>
	public struct GeneticProfile
	{
		/// <summary>
		/// Body size multiplier relative to species average.
		/// 0.5 = species average, 0.0 = smallest, 1.0 = largest possible.
		/// </summary>
		public float BodySize;

		/// <summary>
		/// Coat quality affecting visual appearance and trophy scoring.
		/// Species-specific interpretation (color variation, pattern quality, etc.).
		/// </summary>
		public float CoatQuality;

		/// <summary>
		/// Horn/antler development score for applicable species.
		/// Ignored for naturally hornless species.
		/// </summary>
		public float HornScore;

		/// <summary>
		/// Overall genetic fitness affecting health regeneration and disease resistance.
		/// </summary>
		public float Fitness;

		/// <summary>
		/// Creates a randomized genetic profile with realistic distribution.
		/// Most animals cluster around average with rare exceptional specimens.
		/// </summary>
		/// <param name="rng">Random number generator for deterministic results</param>
		/// <returns>New genetic profile with bell-curve distributed traits</returns>
		public static GeneticProfile CreateRandom(Random rng)
		{
			return new GeneticProfile
			{
				BodySize = GenerateBellCurveValue(rng),
				CoatQuality = GenerateBellCurveValue(rng),
				HornScore = GenerateBellCurveValue(rng),
				Fitness = GenerateBellCurveValue(rng)
			};
		}

		/// <summary>
		/// Calculates trophy score for this animal based on genetics, sex, and age.
		/// </summary>
		/// <param name="sex">Animal's biological sex</param>
		/// <param name="ageMonths">Age in months</param>
		/// <returns>Trophy score from 0-100</returns>
		public readonly float CalculateTrophyScore(AnimalSex sex, float ageMonths)
		{
			float sexMultiplier = sex == AnimalSex.Male ? 1.0f : 0.7f;
			
			// Age factor - prime age (4-8 years) scores highest
			float ageFactor = 1.0f;
			if (ageMonths < 48f) // Under 4 years
			{
				ageFactor = ageMonths / 48f;
			}
			else if (ageMonths > 96f) // Over 8 years
			{
				ageFactor = Math.Max(0.6f, 1.0f - ((ageMonths - 96f) / 60f) * 0.4f);
			}

			float baseScore = (BodySize * 0.3f + HornScore * 0.5f + CoatQuality * 0.2f);
			return baseScore * sexMultiplier * ageFactor * 100f;
		}

		/// <summary>
		/// Generates a value with bell curve distribution centered on 0.5.
		/// </summary>
		private static float GenerateBellCurveValue(Random rng)
		{
			// Simple approximation using average of multiple random values
			float sum = 0f;
			for (int i = 0; i < 3; i++)
			{
				sum += (float)rng.NextDouble();
			}
			return Math.Clamp(sum / 3f, 0f, 1f);
		}
	}
}
