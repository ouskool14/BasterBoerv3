using System;
using System.Collections.Generic;
using Godot;
using BasterBoer.Core.Water;
using BasterBoer.Core.Time;
namespace BasterBoer.Core.Systems
{
	/// <summary>
	/// Central water management system for the farm.
	/// Simulates water levels, rainfall effects, evaporation, and provides
	/// query interface for animals seeking water.
	///
	/// Following the game vision: "A dam at 12% capacity after three dry years
	/// is one of the most visceral read-outs of how the farm is doing."
	/// </summary>
	public sealed class WaterSystem
	{
		private static WaterSystem _instance;

		/// <summary>Singleton instance accessor.</summary>
		public static WaterSystem Instance => _instance ??= new WaterSystem();

		private readonly List<WaterSource> _waterSources;
		private int _nextWaterSourceId;

		// Rainfall tracking
		private float _monthlyRainfallMM;
		private float _yearlyRainfallMM;
		private float _averageAnnualRainfallMM;
		private bool _isDrought;
		private int _droughtMonths;

		// Seasonal multipliers for evaporation
		private readonly Dictionary<Season, float> _seasonalEvaporationMultiplier = new()
		{
			{ Season.Summer, 1.5f },   // Dec-Feb: Hot, high evap
			{ Season.Autumn, 1.0f },   // Mar-May: Moderate
			{ Season.Winter, 0.6f },   // Jun-Aug: Cool, low evap
			{ Season.Spring, 1.2f }    // Sep-Nov: Warming up
		};

		// Seasonal rainfall probability (South African pattern)
		private readonly Dictionary<Season, float> _seasonalRainfallMultiplier = new()
		{
			{ Season.Summer, 1.8f },   // Summer rainfall region
			{ Season.Autumn, 0.8f },
			{ Season.Winter, 0.2f },   // Dry winters
			{ Season.Spring, 0.6f }
		};

		private WaterSystem()
		{
			_waterSources = new List<WaterSource>(64);
			_nextWaterSourceId = 1;
			_averageAnnualRainfallMM = 550f; // Typical bushveld average
			_monthlyRainfallMM = 0f;
			_yearlyRainfallMM = 0f;
			_isDrought = false;
			_droughtMonths = 0;
		}

		/// <summary>Read-only access to all water sources.</summary>
		public IReadOnlyList<WaterSource> WaterSources => _waterSources;

		/// <summary>Current monthly rainfall in mm.</summary>
		public float MonthlyRainfallMM => _monthlyRainfallMM;

		/// <summary>Year-to-date rainfall in mm.</summary>
		public float YearlyRainfallMM => _yearlyRainfallMM;

		/// <summary>Whether the farm is currently in drought conditions.</summary>
		public bool IsDrought => _isDrought;

		/// <summary>Number of consecutive drought months.</summary>
		public int DroughtMonths => _droughtMonths;

		/// <summary>
		/// Registers a new water source in the system.
		/// </summary>
		/// <param name="source">Water source to register</param>
		/// <returns>The registered water source with assigned ID</returns>
		public WaterSource RegisterWaterSource(WaterSource source)
		{
			source.Id = _nextWaterSourceId++;
			_waterSources.Add(source);
			GD.Print($"[WaterSystem] Registered {source.Type}: {source.Name} at {source.Position}");
			return source;
		}

		/// <summary>
		/// Removes a water source from the system.
		/// </summary>
		public bool RemoveWaterSource(int waterSourceId)
		{
			for (int i = 0; i < _waterSources.Count; i++)
			{
				if (_waterSources[i].Id == waterSourceId)
				{
					_waterSources.RemoveAt(i);
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Gets a water source by ID.
		/// </summary>
		public WaterSource? GetWaterSource(int id)
		{
			for (int i = 0; i < _waterSources.Count; i++)
			{
				if (_waterSources[i].Id == id)
					return _waterSources[i];
			}
			return null;
		}

		/// <summary>
		/// Finds the nearest water source with available water to a given position.
		/// Primary interface for HerdBrain water-seeking behavior.
		/// </summary>
		/// <param name="position">World position to search from</param>
		/// <param name="maxDistance">Maximum search distance in meters</param>
		/// <returns>Nearest available water source, or null if none found</returns>
		public WaterSource? FindNearestWater(Vector3 position, float maxDistance = 5000f)
		{
			float nearestDistSq = maxDistance * maxDistance;
			int nearestIndex = -1;

			int count = _waterSources.Count;
			for (int i = 0; i < count; i++)
			{
				WaterSource source = _waterSources[i];
				if (!source.HasWater) continue;

				float distSq = source.Position.DistanceSquaredTo(position);
				if (distSq < nearestDistSq)
				{
					nearestDistSq = distSq;
					nearestIndex = i;
				}
			}

			return nearestIndex >= 0 ? _waterSources[nearestIndex] : null;
		}

		/// <summary>
		/// Finds the best water source considering distance and availability.
		/// Used for intelligent pathfinding when multiple sources exist.
		/// </summary>
		/// <param name="position">World position to search from</param>
		/// <param name="maxDistance">Maximum search distance</param>
		/// <returns>Best water source based on score, or null</returns>
		public WaterSource? FindBestWater(Vector3 position, float maxDistance = 5000f)
		{
			float bestScore = 0f;
			int bestIndex = -1;
			float maxDistSq = maxDistance * maxDistance;

			int count = _waterSources.Count;
			for (int i = 0; i < count; i++)
			{
				WaterSource source = _waterSources[i];
				if (!source.HasWater) continue;

				float distSq = source.Position.DistanceSquaredTo(position);
				if (distSq > maxDistSq) continue;

				// Score: availability weighted against distance
				// Closer sources with more water score higher
				float distanceFactor = 1f - (distSq / maxDistSq);
				float score = source.GetAvailabilityScore() * (0.5f + 0.5f * distanceFactor);

				if (score > bestScore)
				{
					bestScore = score;
					bestIndex = i;
				}
			}

			return bestIndex >= 0 ? _waterSources[bestIndex] : null;
		}

		/// <summary>
		/// Gets all water sources within a radius.
		/// </summary>
		public List<WaterSource> GetWaterSourcesInRadius(Vector3 center, float radius)
		{
			List<WaterSource> result = new List<WaterSource>();
			float radiusSq = radius * radius;

			int count = _waterSources.Count;
			for (int i = 0; i < count; i++)
			{
				if (_waterSources[i].Position.DistanceSquaredTo(center) <= radiusSq)
				{
					result.Add(_waterSources[i]);
				}
			}

			return result;
		}

		/// <summary>
		/// Consumes water from a source (animal drinking).
		/// </summary>
		/// <param name="waterSourceId">ID of water source</param>
		/// <param name="liters">Amount to consume in liters</param>
		/// <returns>Actual amount consumed (may be less if source is low)</returns>
		public float ConsumeWater(int waterSourceId, float liters)
		{
			for (int i = 0; i < _waterSources.Count; i++)
			{
				if (_waterSources[i].Id == waterSourceId)
				{
					WaterSource source = _waterSources[i];
					if (!source.HasWater) return 0f;

					float availableLiters = source.CurrentVolumeM3 * 1000f;
					float consumed = Math.Min(liters, availableLiters);

					source.CurrentLevel -= consumed / 1000f / source.MaxCapacityM3;
					source.CurrentLevel = Math.Max(0f, source.CurrentLevel);

					_waterSources[i] = source;
					return consumed;
				}
			}
			return 0f;
		}

		/// <summary>
		/// Daily simulation tick. Handles evaporation and status updates.
		/// Should be connected to TimeSystem.OnDayPassed.
		/// </summary>
		/// <param name="currentSeason">Current season for evaporation calculation</param>
		public void OnDailyTick(Season currentSeason)
		{
			float evapMultiplier = _seasonalEvaporationMultiplier.GetValueOrDefault(currentSeason, 1f);

			// Drought increases evaporation
			if (_isDrought)
			{
				evapMultiplier *= 1.3f;
			}

			int count = _waterSources.Count;
			for (int i = 0; i < count; i++)
			{
				WaterSource source = _waterSources[i];

				// Skip non-operational sources
				if (source.Status != WaterSourceStatus.Operational) continue;

				// Boreholes don't evaporate - they're underground
				if (source.Type == WaterSourceType.Borehole) continue;

				// Calculate daily loss
				float dailyLoss = (source.EvaporationRate * evapMultiplier) + source.SeepageRate;
				source.CurrentLevel -= dailyLoss;
				source.CurrentLevel = Math.Max(0f, source.CurrentLevel);

				// Update status if dry
				if (source.CurrentLevel < 0.01f)
				{
					source.Status = WaterSourceStatus.Dry;
					GD.Print($"[WaterSystem] {source.Name} has dried up!");
				}

				_waterSources[i] = source;
			}
		}

		/// <summary>
		/// Monthly simulation tick. Handles rainfall and long-term water dynamics.
		/// Should be connected to TimeSystem.OnMonthPassed.
		/// </summary>
		/// <param name="currentSeason">Current season</param>
		/// <param name="month">Current month (1-12)</param>
		public void OnMonthlyTick(Season currentSeason, int month)
		{
			// Calculate this month's rainfall
			float rainfallMultiplier = _seasonalRainfallMultiplier.GetValueOrDefault(currentSeason, 1f);

			// Random variation: 50% to 150% of expected
			Random rng = new Random();
			float variation = 0.5f + (float)rng.NextDouble();

			// Base monthly rainfall (annual / 12, adjusted for season)
			float baseMonthly = _averageAnnualRainfallMM / 12f;
			_monthlyRainfallMM = baseMonthly * rainfallMultiplier * variation;

			// Reset yearly total in January
			if (month == 1)
			{
				_yearlyRainfallMM = 0f;
			}
			_yearlyRainfallMM += _monthlyRainfallMM;

			// Update drought status
			UpdateDroughtStatus();

			// Apply rainfall to water sources
			ApplyRainfall(_monthlyRainfallMM, currentSeason);

			GD.Print($"[WaterSystem] Month {month}: Rainfall {_monthlyRainfallMM:F1}mm, YTD: {_yearlyRainfallMM:F1}mm, Drought: {_isDrought}");
		}

		/// <summary>
		/// Updates drought status based on recent rainfall patterns.
		/// </summary>
		private void UpdateDroughtStatus()
		{
			// Drought threshold: less than 60% of expected monthly rainfall
			float expectedMonthly = _averageAnnualRainfallMM / 12f;
			bool dryMonth = _monthlyRainfallMM < expectedMonthly * 0.6f;

			if (dryMonth)
			{
				_droughtMonths++;
				// Drought declared after 3 consecutive dry months
				if (_droughtMonths >= 3 && !_isDrought)
				{
					_isDrought = true;
					GD.Print("[WaterSystem] DROUGHT CONDITIONS DECLARED");
				}
			}
			else
			{
				// Good rain breaks the drought
				if (_monthlyRainfallMM > expectedMonthly * 1.2f)
				{
					_droughtMonths = 0;
					if (_isDrought)
					{
						_isDrought = false;
						GD.Print("[WaterSystem] Drought conditions have ended");
					}
				}
				else
				{
					_droughtMonths = Math.Max(0, _droughtMonths - 1);
				}
			}
		}

		/// <summary>
		/// Applies rainfall to all water sources based on catchment.
		/// </summary>
		private void ApplyRainfall(float rainfallMM, Season season)
		{
			int count = _waterSources.Count;
			for (int i = 0; i < count; i++)
			{
				WaterSource source = _waterSources[i];

				// Different source types respond differently to rainfall
				float fillRate = source.Type switch
				{
					WaterSourceType.Dam => CalculateDamFillRate(rainfallMM, source.MaxCapacityM3),
					WaterSourceType.River => CalculateRiverFillRate(rainfallMM, season),
					WaterSourceType.Borehole => 0f, // Boreholes don't fill from surface rain
					WaterSourceType.Trough => 0f,   // Troughs must be filled manually
					WaterSourceType.Spring => 0.01f, // Springs slightly recharge
					_ => 0f
				};

				source.CurrentLevel = Math.Min(1f, source.CurrentLevel + fillRate);

				// Restore status if water returns
				if (source.CurrentLevel > 0.1f && source.Status == WaterSourceStatus.Dry)
				{
					source.Status = WaterSourceStatus.Operational;
					GD.Print($"[WaterSystem] {source.Name} has water again!");
				}

				_waterSources[i] = source;
			}
		}

		/// <summary>
		/// Calculates how much a dam fills from rainfall.
		/// Based on catchment area estimation.
		/// </summary>
		private float CalculateDamFillRate(float rainfallMM, float damCapacityM3)
		{
			// Assume catchment area is roughly 100x dam surface area
			// Very simplified model - real hydrology is more complex
			float catchmentMultiplier = 50f;
			float rainfallM = rainfallMM / 1000f;
			float damSurfaceM2 = damCapacityM3 / 3f; // Rough estimate

			float inflowM3 = damSurfaceM2 * catchmentMultiplier * rainfallM * 0.3f; // 30% runoff coefficient
			float fillFraction = inflowM3 / damCapacityM3;

			return Math.Min(fillFraction, 0.15f); // Cap at 15% per month to avoid instant fills
		}

		/// <summary>
		/// Calculates river level changes from rainfall.
		/// </summary>
		private float CalculateRiverFillRate(float rainfallMM, Season season)
		{
			// Rivers respond quickly to rain
			float baseFill = rainfallMM / 100f * 0.1f;

			// Rivers run fuller in summer rainfall season
			if (season == Season.Summer)
				baseFill *= 1.5f;

			return Math.Min(baseFill, 0.2f);
		}

		/// <summary>
		/// Fills a trough from another water source (manual operation).
		/// </summary>
		/// <param name="troughId">ID of trough to fill</param>
		/// <param name="sourceId">ID of source to pump from</param>
		/// <param name="liters">Amount to transfer</param>
		/// <returns>Actual amount transferred</returns>
		public float FillTroughFromSource(int troughId, int sourceId, float liters)
		{
			WaterSource? troughOpt = GetWaterSource(troughId);
			WaterSource? sourceOpt = GetWaterSource(sourceId);

			if (!troughOpt.HasValue || !sourceOpt.HasValue) return 0f;

			WaterSource trough = troughOpt.Value;
			WaterSource source = sourceOpt.Value;

			if (trough.Type != WaterSourceType.Trough) return 0f;
			if (!source.HasWater) return 0f;

			// Calculate how much the trough can accept
			float troughSpaceM3 = (1f - trough.CurrentLevel) * trough.MaxCapacityM3;
			float troughSpaceLiters = troughSpaceM3 * 1000f;
			float toTransfer = Math.Min(liters, troughSpaceLiters);

			// Consume from source
			float consumed = ConsumeWater(sourceId, toTransfer);

			// Add to trough
			for (int i = 0; i < _waterSources.Count; i++)
			{
				if (_waterSources[i].Id == troughId)
				{
					WaterSource t = _waterSources[i];
					t.CurrentLevel += consumed / 1000f / t.MaxCapacityM3;
					t.CurrentLevel = Math.Min(1f, t.CurrentLevel);
					_waterSources[i] = t;
					break;
				}
			}

			return consumed;
		}

		/// <summary>
		/// Sets the average annual rainfall for the farm location.
		/// Should be set based on the farm's geographic location.
		/// </summary>
		public void SetAverageAnnualRainfall(float mm)
		{
			_averageAnnualRainfallMM = mm;
		}

		/// <summary>
		/// Gets statistics about the current water situation.
		/// </summary>
		public WaterStats GetStats()
		{
			var stats = new WaterStats
			{
				TotalSources = _waterSources.Count,
				MonthlyRainfallMM = _monthlyRainfallMM,
				YearlyRainfallMM = _yearlyRainfallMM,
				IsDrought = _isDrought,
				DroughtMonths = _droughtMonths
			};

			float totalCapacity = 0f;
			float totalCurrent = 0f;

			int count = _waterSources.Count;
			for (int i = 0; i < count; i++)
			{
				WaterSource source = _waterSources[i];
				totalCapacity += source.MaxCapacityM3;
				totalCurrent += source.CurrentVolumeM3;

				if (source.HasWater)
					stats.OperationalSources++;
				else if (source.Status == WaterSourceStatus.Dry)
					stats.DrySources++;
				else
					stats.DamagedSources++;
			}

			stats.TotalCapacityM3 = totalCapacity;
			stats.TotalCurrentM3 = totalCurrent;
			stats.OverallLevel = totalCapacity > 0 ? totalCurrent / totalCapacity : 0f;

			return stats;
		}

		/// <summary>
		/// Triggers a random infrastructure failure (for events system).
		/// </summary>
		public void TriggerRandomFailure()
		{
			if (_waterSources.Count == 0) return;

			Random rng = new Random();
			int index = rng.Next(_waterSources.Count);
			WaterSource source = _waterSources[index];

			if (source.Type == WaterSourceType.Borehole)
			{
				source.Status = WaterSourceStatus.PumpFailure;
				GD.Print($"[WaterSystem] PUMP FAILURE: {source.Name} - requires repair!");
			}
			else if (source.Type == WaterSourceType.Trough)
			{
				source.Status = WaterSourceStatus.Damaged;
				GD.Print($"[WaterSystem] DAMAGE: {source.Name} is leaking - requires repair!");
			}

			_waterSources[index] = source;
		}

		/// <summary>
		/// Repairs a damaged water source.
		/// </summary>
		/// <param name="waterSourceId">ID of source to repair</param>
		/// <returns>True if repair was successful</returns>
		public bool RepairWaterSource(int waterSourceId)
		{
			for (int i = 0; i < _waterSources.Count; i++)
			{
				if (_waterSources[i].Id == waterSourceId)
				{
					WaterSource source = _waterSources[i];
					if (source.Status == WaterSourceStatus.Damaged ||
						source.Status == WaterSourceStatus.PumpFailure)
					{
						source.Status = WaterSourceStatus.Operational;
						_waterSources[i] = source;
						GD.Print($"[WaterSystem] Repaired: {source.Name}");
						return true;
					}
				}
			}
			return false;
		}
	}

	/// <summary>
	/// Statistics about the farm's water situation.
	/// </summary>
	public struct WaterStats
	{
		public int TotalSources;
		public int OperationalSources;
		public int DrySources;
		public int DamagedSources;
		public float TotalCapacityM3;
		public float TotalCurrentM3;
		public float OverallLevel;
		public float MonthlyRainfallMM;
		public float YearlyRainfallMM;
		public bool IsDrought;
		public int DroughtMonths;
	}
}
