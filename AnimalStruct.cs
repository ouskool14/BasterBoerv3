using Godot;
using System;

namespace LandManagementSim.Simulation
{
	/// <summary>
	/// Lightweight, cache-friendly representation of an individual animal.
	/// Pure data structure with no behavior logic - all decisions made by HerdBrain.
	/// </summary>
	public struct AnimalStruct
	{
		/// <summary>
		/// World position offset from herd center.
		/// Absolute position = HerdBrain.CenterPosition + this.WorldPosition.
		/// </summary>
		public Vector3 WorldPosition;

		/// <summary>
		/// Age in months for realistic lifecycle simulation.
		/// </summary>
		public float Age;

		/// <summary>
		/// Health value from 0.0 (dead) to 1.0 (perfect health).
		/// </summary>
		public float Health;

		/// <summary>
		/// Immutable genetic traits for this animal.
		/// </summary>
		public GeneticProfile Genetics;

		/// <summary>
		/// Reference ID for render layer's MultiMesh instance.
		/// Set to -1 when not currently rendered.
		/// </summary>
		public int MeshInstanceId;

		/// <summary>
		/// Biological sex affecting behavior and trophy scoring.
		/// </summary>
		public AnimalSex Sex;

		/// <summary>
		/// Current animation state for render layer.
		/// </summary>
		public AnimationState CurrentAnimation;

		/// <summary>
		/// Unique identifier for save/load and tracking systems.
		/// </summary>
		public ulong UniqueId;

		/// <summary>
		/// Timer for next individual behavior variation.
		/// Adds personality to herd members.
		/// </summary>
		public float NextVariationTime;

		/// <summary>
		/// Creates a new animal with randomized initial values.
		/// </summary>
		/// <param name="worldPosition">Initial world position offset</param>
		/// <param name="sex">Biological sex</param>
		/// <param name="uniqueId">Unique identifier</param>
		/// <param name="rng">Random number generator</param>
		/// <returns>Fully initialized animal struct</returns>
		public static AnimalStruct Create(Vector3 worldPosition, AnimalSex sex, ulong uniqueId, Random rng)
		{
			return new AnimalStruct
			{
				WorldPosition = worldPosition,
				Age = (float)(rng.NextDouble() * 36.0 + 12.0), // 12-48 months
				Health = (float)(rng.NextDouble() * 0.2 + 0.8), // 0.8-1.0
				Genetics = GeneticProfile.CreateRandom(rng),
				MeshInstanceId = -1,
				Sex = sex,
				CurrentAnimation = AnimationState.Idle,
				UniqueId = uniqueId,
				NextVariationTime = (float)(rng.NextDouble() * 5.0)
			};
		}

		/// <summary>
		/// Returns true if this animal is alive and should be simulated.
		/// </summary>
		public readonly bool IsAlive => Health > 0f;

		/// <summary>
		/// Returns true if this animal is old enough for breeding.
		/// </summary>
		public readonly bool IsBreedingAge => Age >= 18f && Age <= 144f;

		/// <summary>
		/// Calculates movement speed multiplier based on health and age.
		/// </summary>
		public readonly float GetMovementSpeedMultiplier()
		{
			float healthFactor = Health;
			float ageFactor = 1f;

			if (Age < 12f) // Young animals slower
			{
				ageFactor = 0.6f + (Age / 12f) * 0.4f;
			}
			else if (Age > 120f) // Old animals slower
			{
				ageFactor = Math.Max(0.5f, 1f - ((Age - 120f) / 60f) * 0.3f);
			}

			return healthFactor * ageFactor;
		}
	}
}
