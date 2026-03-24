using System;
using Godot;

namespace LandManagementSim.Simulation
{
	/// <summary>
	/// Static factory for creating herds with species-specific configurations and randomized data.
	/// Handles realistic population distribution and genetic variation.
	/// </summary>
	public static class HerdFactory
	{
		/// <summary>
		/// Creates a new herd of specified species with randomized composition.
		/// </summary>
		/// <param name="species">Species to create</param>
		/// <param name="initialCenter">Starting world position</param>
		/// <param name="rngSeed">Optional seed for deterministic generation</param>
		/// <returns>Fully configured herd brain with animals</returns>
		public static HerdBrain CreateHerd(Species species, Vector3 initialCenter, int? rngSeed = null)
		{
			SpeciesConfig config = GetSpeciesConfig(species);
			int seed = rngSeed ?? System.Environment.TickCount ^ initialCenter.GetHashCode() ^ (int)species;
			Random rng = new Random(seed);

			// Determine herd size within species range
			int herdSize = rng.Next(config.MinHerdSize, config.MaxHerdSize + 1);
			AnimalStruct[] animals = new AnimalStruct[herdSize];

			// Create animals with realistic sex distribution
			float femaleRatio = GetFemaleRatio(species);
			int femaleCount = (int)(herdSize * femaleRatio);
			int maleCount = herdSize - femaleCount;

			// Generate animals with spatial distribution
			for (int i = 0; i < herdSize; i++)
			{
				Vector3 position = GeneratePositionInHerd(Vector3.Zero, 20f, rng);
				AnimalSex sex = i < femaleCount ? AnimalSex.Female : AnimalSex.Male;
				ulong id = AnimalSystem.Instance.GetNextAnimalId();
				
				animals[i] = AnimalStruct.Create(position, sex, id, rng);
				
				// Males tend to have better horn development
				if (sex == AnimalSex.Male)
				{
					ref GeneticProfile genetics = ref animals[i].Genetics;
					genetics.HornScore = Math.Min(1f, genetics.HornScore + (float)(rng.NextDouble() * 0.2));
				}
			}

			// Apply realistic age distribution
			ApplyAgeDistribution(animals, rng);

			// Create and configure herd brain
			HerdBrain herd = new HerdBrain(species, config, initialCenter, animals, seed);

			return herd;
		}

		/// <summary>
		/// Gets species-specific configuration parameters.
		/// </summary>
		private static SpeciesConfig GetSpeciesConfig(Species species)
		{
			return species switch
			{
				Species.Impala => new SpeciesConfig
				{
					BaseAwarenessRadius = 120f,
					FlightDistance = 200f,
					MinHerdSize = 10,
					MaxHerdSize = 50,
					DrinkFrequencyHours = 12f, // Twice daily
					GrazeSpeedMPS = 0.4f,
					WalkSpeedMPS = 1.5f,
					RunSpeedMPS = 8f,
					RestIntervalHours = 6f,
					MaxDailyTravelKm = 8f
				},

				Species.Buffalo => new SpeciesConfig
				{
					BaseAwarenessRadius = 80f,
					FlightDistance = 150f,
					MinHerdSize = 20,
					MaxHerdSize = 200,
					DrinkFrequencyHours = 16f, // Daily+
					GrazeSpeedMPS = 0.3f,
					WalkSpeedMPS = 1.2f,
					RunSpeedMPS = 7f,
					RestIntervalHours = 4f,
					MaxDailyTravelKm = 10f
				},

				Species.Kudu => new SpeciesConfig
				{
					BaseAwarenessRadius = 150f,
					FlightDistance = 250f,
					MinHerdSize = 5,
					MaxHerdSize = 15,
					DrinkFrequencyHours = 16f,
					GrazeSpeedMPS = 0.35f,
					WalkSpeedMPS = 1.4f,
					RunSpeedMPS = 9f,
					RestIntervalHours = 6f,
					MaxDailyTravelKm = 12f
				},

				Species.Wildebeest => new SpeciesConfig
				{
					BaseAwarenessRadius = 100f,
					FlightDistance = 300f,
					MinHerdSize = 30,
					MaxHerdSize = 150,
					DrinkFrequencyHours = 12f,
					GrazeSpeedMPS = 0.4f,
					WalkSpeedMPS = 1.6f,
					RunSpeedMPS = 9f,
					RestIntervalHours = 5f,
					MaxDailyTravelKm = 15f
				},

				Species.Zebra => new SpeciesConfig
				{
					BaseAwarenessRadius = 110f,
					FlightDistance = 250f,
					MinHerdSize = 8,
					MaxHerdSize = 30,
					DrinkFrequencyHours = 16f,
					GrazeSpeedMPS = 0.4f,
					WalkSpeedMPS = 1.5f,
					RunSpeedMPS = 8.5f,
					RestIntervalHours = 5f,
					MaxDailyTravelKm = 12f
				},

				Species.Waterbuck => new SpeciesConfig
				{
					BaseAwarenessRadius = 90f,
					FlightDistance = 180f,
					MinHerdSize = 6,
					MaxHerdSize = 20,
					DrinkFrequencyHours = 8f, // Need water frequently
					GrazeSpeedMPS = 0.35f,
					WalkSpeedMPS = 1.3f,
					RunSpeedMPS = 8f,
					RestIntervalHours = 6f,
					MaxDailyTravelKm = 8f
				},

				_ => new SpeciesConfig // Default fallback
				{
					BaseAwarenessRadius = 100f,
					FlightDistance = 200f,
					MinHerdSize = 5,
					MaxHerdSize = 20,
					DrinkFrequencyHours = 12f,
					GrazeSpeedMPS = 0.4f,
					WalkSpeedMPS = 1.5f,
					RunSpeedMPS = 8f,
					RestIntervalHours = 6f,
					MaxDailyTravelKm = 10f
				}
			};
		}

		/// <summary>
		/// Gets typical female ratio for species.
		/// Most African herbivores have female-dominated herds.
		/// </summary>
		private static float GetFemaleRatio(Species species)
		{
			return species switch
			{
				Species.Impala => 0.70f,      // Bachelor groups common
				Species.Buffalo => 0.65f,     // Mixed herds
				Species.Kudu => 0.60f,        // Small family groups
				Species.Wildebeest => 0.68f,  // Large mixed herds
				Species.Zebra => 0.72f,       // Harem structure
				Species.Waterbuck => 0.65f,   // Territorial males
				_ => 0.65f
			};
		}

		/// <summary>
		/// Generates random position within circular herd area.
		/// </summary>
		private static Vector3 GeneratePositionInHerd(Vector3 center, float radius, Random rng)
		{
			float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
			float distance = (float)(rng.NextDouble() * radius);
			
			return center + new Vector3(
				Mathf.Cos(angle) * distance,
				0f,
				Mathf.Sin(angle) * distance
			);
		}

		/// <summary>
		/// Applies realistic age distribution to herd.
		/// Creates population pyramid with more young animals.
		/// </summary>
		private static void ApplyAgeDistribution(AnimalStruct[] animals, Random rng)
		{
			for (int i = 0; i < animals.Length; i++)
			{
				ref AnimalStruct animal = ref animals[i];
				
				double roll = rng.NextDouble();
				
				if (roll < 0.15) // 15% juveniles (0-1 year)
				{
					animal.Age = (float)(rng.NextDouble() * 12.0);
				}
				else if (roll < 0.40) // 25% young adults (1-3 years)
				{
					animal.Age = (float)(rng.NextDouble() * 24.0 + 12.0);
				}
				else if (roll < 0.75) // 35% prime adults (3-8 years)
				{
					animal.Age = (float)(rng.NextDouble() * 60.0 + 36.0);
				}
				else if (roll < 0.92) // 17% mature adults (8-12 years)
				{
					animal.Age = (float)(rng.NextDouble() * 48.0 + 96.0);
				}
				else // 8% elderly (12+ years)
				{
					animal.Age = (float)(rng.NextDouble() * 48.0 + 144.0);
				}

				// Adjust health based on age
				if (animal.Age < 6f) // Very young vulnerable
				{
					animal.Health = (float)(rng.NextDouble() * 0.3 + 0.6);
				}
				else if (animal.Age > 144f) // Elderly declining health
				{
					animal.Health = (float)(rng.NextDouble() * 0.4 + 0.5);
				}
			}
		}

		/// <summary>
		/// Convenience methods for creating specific species herds.
		/// </summary>
		public static HerdBrain CreateImpalaHerd(Vector3 position, int? seed = null) =>
			CreateHerd(Species.Impala, position, seed);

		public static HerdBrain CreateBuffaloHerd(Vector3 position, int? seed = null) =>
			CreateHerd(Species.Buffalo, position, seed);

		public static HerdBrain CreateKuduHerd(Vector3 position, int? seed = null) =>
			CreateHerd(Species.Kudu, position, seed);

		public static HerdBrain CreateWildebeestHerd(Vector3 position, int? seed = null) =>
			CreateHerd(Species.Wildebeest, position, seed);

		public static HerdBrain CreateZebraHerd(Vector3 position, int? seed = null) =>
			CreateHerd(Species.Zebra, position, seed);

		public static HerdBrain CreateWaterbuckHerd(Vector3 position, int? seed = null) =>
			CreateHerd(Species.Waterbuck, position, seed);
	}
}
