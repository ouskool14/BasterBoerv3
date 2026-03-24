namespace LandManagementSim.Simulation
{
	/// <summary>
	/// Level of Detail for animal behavior simulation based on distance from player.
	/// Determines update frequency and complexity of simulation logic.
	/// </summary>
	public enum BehaviourLOD : byte
	{
		/// <summary>
		/// 0-150m: Full tick every frame with all state checks active.
		/// Maximum visual fidelity and behavioral complexity.
		/// </summary>
		Full = 0,

		/// <summary>
		/// 150-500m: State changes only, no per-frame awareness checks.
		/// Reduced update frequency but maintains behavioral transitions.
		/// </summary>
		High = 1,

		/// <summary>
		/// 500m-2km: Position updated once per second, major events only.
		/// Minimal CPU overhead while maintaining basic simulation.
		/// </summary>
		Medium = 2,

		/// <summary>
		/// 2km+: Monthly simulation tick only, no real-time position tracking.
		/// Background simulation for population dynamics only.
		/// </summary>
		Background = 3
	}

	/// <summary>
	/// Helper methods for LOD distance calculations and behavior.
	/// </summary>
	public static class BehaviourLODHelper
	{
		public const float FULL_LOD_DISTANCE = 150f;
		public const float HIGH_LOD_DISTANCE = 500f;
		public const float MEDIUM_LOD_DISTANCE = 2000f;

		/// <summary>
		/// Determines appropriate LOD tier based on squared distance from player.
		/// Uses squared distance to avoid expensive square root calculations.
		/// </summary>
		/// <param name="distanceSquared">Squared distance from herd center to player</param>
		/// <returns>Appropriate BehaviourLOD tier</returns>
		public static BehaviourLOD GetLODFromDistanceSquared(float distanceSquared)
		{
			if (distanceSquared < FULL_LOD_DISTANCE * FULL_LOD_DISTANCE)
				return BehaviourLOD.Full;
			
			if (distanceSquared < HIGH_LOD_DISTANCE * HIGH_LOD_DISTANCE)
				return BehaviourLOD.High;
			
			if (distanceSquared < MEDIUM_LOD_DISTANCE * MEDIUM_LOD_DISTANCE)
				return BehaviourLOD.Medium;
			
			return BehaviourLOD.Background;
		}

		/// <summary>
		/// Returns the update interval in seconds for a given LOD tier.
		/// </summary>
		/// <param name="lod">The LOD tier</param>
		/// <returns>Update interval in seconds (0 = every frame)</returns>
		public static float GetUpdateInterval(BehaviourLOD lod)
		{
			return lod switch
			{
				BehaviourLOD.Full => 0f,        // Every frame
				BehaviourLOD.High => 0.1f,      // 10 times per second
				BehaviourLOD.Medium => 1.0f,    // Once per second
				BehaviourLOD.Background => 0f,  // Only on TimeSystem ticks
				_ => 1.0f
			};
		}
	}

	/// <summary>
	/// Supported animal species in the simulation.
	/// </summary>
	public enum Species : byte
	{
		Impala,
		Buffalo,
		Kudu,
		Wildebeest,
		Zebra,
		Waterbuck
	}

	/// <summary>
	/// Biological sex of an animal.
	/// </summary>
	public enum AnimalSex : byte
	{
		Male,
		Female
	}

	/// <summary>
	/// Animation states for individual animals.
	/// </summary>
	public enum AnimationState : byte
	{
		Idle,
		Grazing,
		Walking,
		Running,
		Drinking,
		Resting,
		Alert,
		Fleeing
	}

	/// <summary>
	/// Behavioral states for herd-level decision making.
	/// </summary>
	public enum HerdState : byte
	{
		Grazing,
		Moving,
		Drinking,
		Resting,
		Fleeing,
		Alerting
	}
}
