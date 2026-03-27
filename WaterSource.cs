using Godot;

namespace BasterBoer.Core.Water
{
	/// <summary>
	/// Type of water source on the farm.
	/// </summary>
	public enum WaterSourceType
	{
		/// <summary>Natural dam - capacity depends on rainfall, largest storage.</summary>
		Dam,
		/// <summary>River segment - flows seasonally, may dry up in drought.</summary>
		River,
		/// <summary>Borehole - consistent but limited output, requires pump infrastructure.</summary>
		Borehole,
		/// <summary>Water trough - small capacity, must be filled from other sources.</summary>
		Trough,
		/// <summary>Natural spring - small but reliable year-round.</summary>
		Spring
	}

	/// <summary>
	/// Current operational status of a water source.
	/// </summary>
	public enum WaterSourceStatus
	{
		/// <summary>Functioning normally.</summary>
		Operational,
		/// <summary>Completely dry - no water available.</summary>
		Dry,
		/// <summary>Infrastructure damaged - needs repair.</summary>
		Damaged,
		/// <summary>Pump failure (boreholes only).</summary>
		PumpFailure
	}

	/// <summary>
	/// Data structure representing a water source on the farm.
	/// Designed as a struct for cache-friendly iteration in WaterSystem.
	/// </summary>
	public struct WaterSource
	{
		/// <summary>Unique identifier for this water source.</summary>
		public int Id;

		/// <summary>Player-assigned name (e.g., "Main Dam", "North Borehole").</summary>
		public string Name;

		/// <summary>Type of water source.</summary>
		public WaterSourceType Type;

		/// <summary>World position of the water source center.</summary>
		public Vector3 Position;

		/// <summary>Current water level from 0.0 (empty) to 1.0 (full).</summary>
		public float CurrentLevel;

		/// <summary>Maximum capacity in cubic meters.</summary>
		public float MaxCapacityM3;

		/// <summary>Current operational status.</summary>
		public WaterSourceStatus Status;

		/// <summary>Daily evaporation rate (fraction lost per day, affected by season).</summary>
		public float EvaporationRate;

		/// <summary>Daily seepage/leakage rate (fraction lost per day).</summary>
		public float SeepageRate;

		/// <summary>For boreholes: liters per hour output when operational.</summary>
		public float BoreholeOutputLPH;

		/// <summary>For boreholes: depth in meters (affects pump cost and reliability).</summary>
		public float BoreholeDepthM;

		/// <summary>Radius in meters - used for visual scaling and animal drinking area.</summary>
		public float Radius;

		/// <summary>Chunk ID this water source belongs to (for streaming).</summary>
		public int ChunkId;

		/// <summary>
		/// Returns current water volume in cubic meters.
		/// </summary>
		public readonly float CurrentVolumeM3 => CurrentLevel * MaxCapacityM3;

		/// <summary>
		/// Returns whether this water source has usable water.
		/// </summary>
		public readonly bool HasWater => CurrentLevel > 0.05f && Status == WaterSourceStatus.Operational;

		/// <summary>
		/// Returns water availability score (0-1) considering level and status.
		/// Used by herds to evaluate water sources.
		/// </summary>
		public readonly float GetAvailabilityScore()
		{
			if (Status != WaterSourceStatus.Operational) return 0f;
			if (CurrentLevel < 0.05f) return 0f;

			// Prefer fuller water sources
			return CurrentLevel;
		}

		/// <summary>
		/// Creates a new dam water source with typical South African characteristics.
		/// </summary>
		public static WaterSource CreateDam(int id, string name, Vector3 position, float capacityM3, float initialLevel = 0.6f)
		{
			return new WaterSource
			{
				Id = id,
				Name = name,
				Type = WaterSourceType.Dam,
				Position = position,
				CurrentLevel = initialLevel,
				MaxCapacityM3 = capacityM3,
				Status = WaterSourceStatus.Operational,
				EvaporationRate = 0.002f, // ~0.2% per day base rate
				SeepageRate = 0.001f,     // ~0.1% per day
				Radius = Mathf.Sqrt(capacityM3 / 3f), // Rough radius estimate
				BoreholeOutputLPH = 0f,
				BoreholeDepthM = 0f,
				ChunkId = -1
			};
		}

		/// <summary>
		/// Creates a new borehole water source.
		/// </summary>
		public static WaterSource CreateBorehole(int id, string name, Vector3 position, float depthM, float outputLPH)
		{
			return new WaterSource
			{
				Id = id,
				Name = name,
				Type = WaterSourceType.Borehole,
				Position = position,
				CurrentLevel = 1f, // Boreholes are "full" when operational
				MaxCapacityM3 = outputLPH * 24f / 1000f, // Daily output capacity
				Status = WaterSourceStatus.Operational,
				EvaporationRate = 0f,
				SeepageRate = 0f,
				Radius = 2f,
				BoreholeOutputLPH = outputLPH,
				BoreholeDepthM = depthM,
				ChunkId = -1
			};
		}

		/// <summary>
		/// Creates a river segment water source.
		/// </summary>
		public static WaterSource CreateRiver(int id, string name, Vector3 position, float widthM, float initialLevel = 0.7f)
		{
			return new WaterSource
			{
				Id = id,
				Name = name,
				Type = WaterSourceType.River,
				Position = position,
				CurrentLevel = initialLevel,
				MaxCapacityM3 = widthM * 100f * 2f, // Rough estimate: width * length * depth
				Status = WaterSourceStatus.Operational,
				EvaporationRate = 0.003f, // Rivers evaporate faster (more surface area)
				SeepageRate = 0f,         // Rivers don't seep - they flow
				Radius = widthM / 2f,
				BoreholeOutputLPH = 0f,
				BoreholeDepthM = 0f,
				ChunkId = -1
			};
		}

		/// <summary>
		/// Creates a water trough.
		/// </summary>
		public static WaterSource CreateTrough(int id, string name, Vector3 position, float capacityLiters)
		{
			return new WaterSource
			{
				Id = id,
				Name = name,
				Type = WaterSourceType.Trough,
				Position = position,
				CurrentLevel = 1f,
				MaxCapacityM3 = capacityLiters / 1000f,
				Status = WaterSourceStatus.Operational,
				EvaporationRate = 0.005f, // Small surface, high evap rate
				SeepageRate = 0f,
				Radius = 1.5f,
				BoreholeOutputLPH = 0f,
				BoreholeDepthM = 0f,
				ChunkId = -1
			};
		}
	}
}
