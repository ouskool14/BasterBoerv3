using System;
using System.Collections.Generic;
using Godot;

namespace LandManagementSim.Simulation
{
	/// <summary>
	/// Singleton system managing all herds and animals in the simulation.
	/// Handles main update loop, LOD management, and provides query interface.
	/// </summary>
	public sealed class AnimalSystem
	{
		private static AnimalSystem _instance;
		
		/// <summary>
		/// Singleton instance accessor.
		/// </summary>
		public static AnimalSystem Instance => _instance ??= new AnimalSystem();

		private readonly List<HerdBrain> _herds;
		private float _secondAccumulator;
		private ulong _nextAnimalId;

		private AnimalSystem()
		{
			_herds = new List<HerdBrain>(256); // Pre-allocate for expected herd count
			_secondAccumulator = 0f;
			_nextAnimalId = 1;
		}

		/// <summary>
		/// Read-only access to all herds in the simulation.
		/// </summary>
		public IReadOnlyList<HerdBrain> Herds => _herds;

		/// <summary>
		/// Total count of all animals across all herds.
		/// </summary>
		public int TotalAnimalCount
		{
			get
			{
				int count = 0;
				int herdCount = _herds.Count;
				for (int i = 0; i < herdCount; i++)
				{
					count += _herds[i].Animals.Length;
				}
				return count;
			}
		}

		/// <summary>
		/// Main frame update called from game loop.
		/// Handles per-frame simulation for nearby herds and accumulates time for interval updates.
		/// </summary>
		/// <param name="deltaTime">Frame delta time in seconds</param>
		/// <param name="playerPosition">Current player world position</param>
		public void UpdateFrame(float deltaTime, Vector3 playerPosition)
		{
			// Update all herds with LOD-aware ticking
			int herdCount = _herds.Count;
			for (int i = 0; i < herdCount; i++)
			{
				_herds[i].Tick(deltaTime, playerPosition);
			}

			// Accumulate time for 1-second interval updates
			_secondAccumulator += deltaTime;
			if (_secondAccumulator >= 1f)
			{
				ProcessSecondInterval(_secondAccumulator);
				_secondAccumulator = 0f;
			}
		}

		/// <summary>
		/// Processes updates that should happen approximately once per second.
		/// Used for Medium LOD herds and other interval-based logic.
		/// </summary>
		private void ProcessSecondInterval(float deltaTime)
		{
			// TODO: Add any logic that needs to run on 1-second intervals
			// For example: predator detection, environmental events, etc.
		}

		/// <summary>
		/// Creates and registers a new herd in the simulation.
		/// </summary>
		/// <param name="species">Species for the new herd</param>
		/// <param name="initialPosition">Starting world position</param>
		/// <param name="rngSeed">Optional seed for deterministic generation</param>
		/// <returns>The created herd brain</returns>
		public HerdBrain CreateHerd(Species species, Vector3 initialPosition, int? rngSeed = null)
		{
			HerdBrain herd = HerdFactory.CreateHerd(species, initialPosition, rngSeed);
			herd.HerdId = _herds.Count;
			_herds.Add(herd);

			// TODO: Notify render system to allocate MultiMesh instances
			// RenderSystem.Instance.AllocateHerdMeshes(herd);

			return herd;
		}

		/// <summary>
		/// Removes a herd from the simulation.
		/// </summary>
		/// <param name="herd">Herd to remove</param>
		/// <returns>True if herd was found and removed</returns>
		public bool RemoveHerd(HerdBrain herd)
		{
			bool removed = _herds.Remove(herd);
			
			if (removed)
			{
				// TODO: Notify render system to deallocate MultiMesh instances
				// RenderSystem.Instance.DeallocateHerdMeshes(herd);
			}

			return removed;
		}

		/// <summary>
		/// Gets all herds within specified radius of a position.
		/// Useful for spatial queries by other systems.
		/// </summary>
		/// <param name="center">Center position for search</param>
		/// <param name="radius">Search radius in meters</param>
		/// <returns>List of herds within radius</returns>
		public List<HerdBrain> GetHerdsInRadius(Vector3 center, float radius)
		{
			List<HerdBrain> result = new List<HerdBrain>();
			float radiusSquared = radius * radius;
			
			int herdCount = _herds.Count;
			for (int i = 0; i < herdCount; i++)
			{
				HerdBrain herd = _herds[i];
				float distanceSquared = herd.CenterPosition.DistanceSquaredTo(center);
				
				if (distanceSquared <= radiusSquared)
				{
					result.Add(herd);
				}
			}

			return result;
		}

		/// <summary>
		/// Allocates a unique ID for a new animal.
		/// Thread-safe for future multi-threading support.
		/// </summary>
		/// <returns>Unique animal ID</returns>
		public ulong GetNextAnimalId()
		{
			return _nextAnimalId++;
		}

		/// <summary>
		/// Daily simulation tick called by TimeSystem.
		/// Handles daily reset logic and health updates.
		/// </summary>
		public void OnDailyTick()
		{
			int herdCount = _herds.Count;
			for (int i = 0; i < herdCount; i++)
			{
				_herds[i].OnDailyTick();
			}
		}

		/// <summary>
		/// Monthly simulation tick called by TimeSystem.
		/// Handles long-term population dynamics and aging.
		/// </summary>
		public void OnMonthlyTick()
		{
			int herdCount = _herds.Count;
			for (int i = 0; i < herdCount; i++)
			{
				HerdBrain herd = _herds[i];
				herd.OnMonthlyTick();

				int aliveCount = 0;
				for (int j = 0; j < herd.Animals.Length; j++)
				{
					if (herd.Animals[j].Health > 0f)
					{
						herd.Animals[aliveCount] = herd.Animals[j];
						aliveCount++;
					}
				}

				// Resize array only if needed (avoid constant allocations)
				if (aliveCount < herd.Animals.Length)
				{
					AnimalStruct[] animals = herd.Animals;
					Array.Resize(ref animals, aliveCount);
					herd.Animals = animals;
				}
			}

			// TODO: Trigger breeding events
			// TODO: Handle seasonal migration patterns
		}

		/// <summary>
		/// Alerts all herds within radius to a threat.
		/// Used by predator systems, gunshot detection, etc.
		/// </summary>
		/// <param name="threatPosition">Position of threat</param>
		/// <param name="alertRadius">Radius of alert effect</param>
		/// <param name="threatIntensity">Intensity of threat (0.0 to 1.0)</param>
		public void AlertHerdsToThreat(Vector3 threatPosition, float alertRadius, float threatIntensity)
		{
			List<HerdBrain> affectedHerds = GetHerdsInRadius(threatPosition, alertRadius);
			
			int count = affectedHerds.Count;
			for (int i = 0; i < count; i++)
			{
				// Apply distance-based intensity falloff
				float distance = affectedHerds[i].CenterPosition.DistanceTo(threatPosition);
				float falloffFactor = 1f - (distance / alertRadius);
				float adjustedIntensity = threatIntensity * falloffFactor;
				
				affectedHerds[i].AlertToThreat(threatPosition, adjustedIntensity);
			}
		}

		/// <summary>
		/// Gets statistics about the current simulation state.
		/// Useful for UI display and debugging.
		/// </summary>
		/// <returns>Simulation statistics</returns>
		public SimulationStats GetStats()
		{
			var stats = new SimulationStats
			{
				TotalHerds = _herds.Count,
				TotalAnimals = TotalAnimalCount
			};

			// Count herds by LOD
			int herdCount = _herds.Count;
			for (int i = 0; i < herdCount; i++)
			{
				switch (_herds[i].CurrentLOD)
				{
					case BehaviourLOD.Full:
						stats.FullLODHerds++;
						break;
					case BehaviourLOD.High:
						stats.HighLODHerds++;
						break;
					case BehaviourLOD.Medium:
						stats.MediumLODHerds++;
						break;
					case BehaviourLOD.Background:
						stats.BackgroundLODHerds++;
						break;
				}
			}

			return stats;
		}
	}

	/// <summary>
	/// Statistics about the current simulation state.
	/// </summary>
	public struct SimulationStats
	{
		public int TotalHerds;
		public int TotalAnimals;
		public int FullLODHerds;
		public int HighLODHerds;
		public int MediumLODHerds;
		public int BackgroundLODHerds;
	}
}
