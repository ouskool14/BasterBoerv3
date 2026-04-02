using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.IO;
using Godot;
using WorldStreaming;
using BasterBoer.Core.Economy;

namespace Basterboer.Buildings
{
	/// <summary>
	/// Singleton system managing all building data with chunk-based organization.
	/// Handles validation, persistence, and integration with economy system.
	/// </summary>
	public partial class BuildingSystem : Node
	{
		private static BuildingSystem _instance;
		public static BuildingSystem Instance => _instance;

		// Building costs in South African Rand (ZAR)
		public static readonly Dictionary<BuildingType, int> BuildingCosts = new()
		{
			{ BuildingType.Wall, 500 },
			{ BuildingType.Roof, 1200 },
			{ BuildingType.Floor, 400 },
			{ BuildingType.Door, 600 },
			{ BuildingType.Window, 450 },
			{ BuildingType.Stoep, 800 },
			{ BuildingType.BraaiArea, 1500 },
			{ BuildingType.WaterTrough, 700 },
			{ BuildingType.FeedingStation, 650 },
			{ BuildingType.StaffQuarters, 8000 },
			{ BuildingType.Boma, 3500 },
			{ BuildingType.Hide, 2200 }
		};

		// Building dimensions for collision detection (width x depth in meters)
		public static readonly Dictionary<BuildingType, Vector2> BuildingDimensions = new()
		{
			{ BuildingType.Wall, new Vector2(4.0f, 0.3f) },
			{ BuildingType.Roof, new Vector2(6.0f, 6.0f) },
			{ BuildingType.Floor, new Vector2(4.0f, 4.0f) },
			{ BuildingType.Door, new Vector2(1.2f, 0.2f) },
			{ BuildingType.Window, new Vector2(1.5f, 0.2f) },
			{ BuildingType.Stoep, new Vector2(8.0f, 4.0f) },
			{ BuildingType.BraaiArea, new Vector2(3.0f, 3.0f) },
			{ BuildingType.WaterTrough, new Vector2(2.5f, 1.2f) },
			{ BuildingType.FeedingStation, new Vector2(2.0f, 2.0f) },
			{ BuildingType.StaffQuarters, new Vector2(10.0f, 8.0f) },
			{ BuildingType.Boma, new Vector2(20.0f, 20.0f) },
			{ BuildingType.Hide, new Vector2(4.0f, 4.0f) }
		};

		// Chunk-organized building storage
		private Dictionary<ChunkCoord, List<BuildingData>> _buildingsByChunk = new();
		private Dictionary<Guid, BuildingData> _buildingsById = new();

		private const string SavePath = "user://buildings.json";
		private const float MinBuildingDistance = 1.5f;
		private const float MaxTerrainSlope = 2.0f;

		public event Action<BuildingData> BuildingPlaced;
		public event Action<Guid, ChunkCoord> BuildingRemoved;

		public override void _Ready()
		{
			if (_instance != null && _instance != this)
			{
				GD.PushError("Multiple BuildingSystem instances detected. Destroying duplicate.");
				QueueFree();
				return;
			}

			_instance = this;
			GD.Print("[BuildingSystem] Initialized successfully");
			
			Load();
		}

		/// <summary>
		/// Attempts to place a building with full validation and cost checking.
		/// Returns BuildingData if successful, null if placement failed.
		/// </summary>
		public BuildingData PlaceBuilding(BuildingType type, Vector3 position, float rotation)
		{
			// Snap position to terrain height
			float terrainHeight = LandManagementSim.Terrain.TerrainQuery.GetHeight(position.X, position.Z);
			position.Y = terrainHeight;

			ChunkCoord chunk = ChunkCoord.FromWorldPosition(position);

			// Comprehensive placement validation
			if (!IsPlacementValid(type, position, rotation, chunk))
			{
				GD.PrintErr($"[BuildingSystem] Invalid placement for {type} at {position}");
				return null;
			}

			// Economy validation
			int cost = BuildingCosts[type];
			if (EconomySystem.Instance == null || !EconomySystem.Instance.CanAfford(cost))
			{
				GD.PrintErr($"[BuildingSystem] Insufficient funds for {type} (costs R{cost})");
				return null;
			}

			// Create and store building data
			BuildingData building = new BuildingData(type, position, rotation, chunk);

			if (!_buildingsByChunk.ContainsKey(chunk))
			{
				_buildingsByChunk[chunk] = new List<BuildingData>();
			}

			_buildingsByChunk[chunk].Add(building);
			_buildingsById[building.Id] = building;

			// Deduct cost from economy
			EconomySystem.Instance.SpendMoney(cost, $"Built {type}");

			// Notify renderer and other systems
			BuildingPlaced?.Invoke(building);

			GD.Print($"[BuildingSystem] Successfully placed {type} at {position} for R{cost}");
			return building;
		}

		/// <summary>
		/// Removes a building by ID with proper cleanup
		/// </summary>
		public bool RemoveBuilding(Guid buildingId)
		{
			if (!_buildingsById.TryGetValue(buildingId, out BuildingData building))
			{
				GD.PrintErr($"[BuildingSystem] Building {buildingId} not found for removal");
				return false;
			}

			ChunkCoord chunk = building.ChunkCoord;

			// Remove from collections
			_buildingsById.Remove(buildingId);
			
			if (_buildingsByChunk.TryGetValue(chunk, out List<BuildingData> chunkBuildings))
			{
				chunkBuildings.Remove(building);
				
				if (chunkBuildings.Count == 0)
				{
					_buildingsByChunk.Remove(chunk);
				}
			}

			// Notify renderer
			BuildingRemoved?.Invoke(buildingId, chunk);

			GD.Print($"[BuildingSystem] Removed building {buildingId} from chunk {chunk}");
			return true;
		}

		/// <summary>
		/// Gets all buildings in a specific chunk (returns copy for safety)
		/// </summary>
		public List<BuildingData> GetBuildingsInChunk(ChunkCoord chunk)
		{
			if (_buildingsByChunk.TryGetValue(chunk, out List<BuildingData> buildings))
			{
				return new List<BuildingData>(buildings);
			}
			return new List<BuildingData>();
		}

		/// <summary>
		/// Gets a building by its unique ID
		/// </summary>
		public BuildingData GetBuilding(Guid id)
		{
			_buildingsById.TryGetValue(id, out BuildingData building);
			return building;
		}

		/// <summary>
		/// Validates if a building can be placed at the specified location
		/// </summary>
		public bool CanPlaceBuilding(BuildingType type, Vector3 position, float rotation)
		{
			float terrainHeight = LandManagementSim.Terrain.TerrainQuery.GetHeight(position.X, position.Z);
			position.Y = terrainHeight;
			ChunkCoord chunk = ChunkCoord.FromWorldPosition(position);
			
			return IsPlacementValid(type, position, rotation, chunk);
		}

		/// <summary>
		/// Comprehensive placement validation including terrain, overlaps, and ownership
		/// </summary>
		private bool IsPlacementValid(BuildingType type, Vector3 position, float rotation, ChunkCoord chunk)
		{
			// Terrain height validation
			float terrainHeight = LandManagementSim.Terrain.TerrainQuery.GetHeight(position.X, position.Z);
			if (float.IsNaN(terrainHeight) || Mathf.Abs(position.Y - terrainHeight) > 0.5f)
			{
				return false;
			}

			// Terrain slope validation for larger buildings
			if (IsTerrainTooSteep(position, type))
			{
				return false;
			}

			// Overlap detection with existing buildings
			if (IsOverlappingWithExisting(type, position, chunk))
			{
				return false;
			}

			// Land ownership validation (placeholder for future implementation)
			if (!IsOnPlayerLand(position))
			{
				return false;
			}

			return true;
		}

		/// <summary>
		/// Checks terrain slope using sampling at building corners
		/// </summary>
		private bool IsTerrainTooSteep(Vector3 position, BuildingType type)
		{
			Vector2 dimensions = BuildingDimensions[type];
			float checkRadius = Mathf.Max(dimensions.X, dimensions.Y) / 2.0f;

			// Sample terrain at multiple points
			float centerHeight = LandManagementSim.Terrain.TerrainQuery.GetHeight(position.X, position.Z);
			float[] sampleHeights = {
				LandManagementSim.Terrain.TerrainQuery.GetHeight(position.X + checkRadius, position.Z),
				LandManagementSim.Terrain.TerrainQuery.GetHeight(position.X - checkRadius, position.Z),
				LandManagementSim.Terrain.TerrainQuery.GetHeight(position.X, position.Z + checkRadius),
				LandManagementSim.Terrain.TerrainQuery.GetHeight(position.X, position.Z - checkRadius)
			};

			foreach (float height in sampleHeights)
			{
				if (Mathf.Abs(centerHeight - height) > MaxTerrainSlope)
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Checks for overlapping buildings using distance formula: $$d = \sqrt{(x_2 - x_1)^2 + (z_2 - z_1)^2}$$
		/// </summary>
		private bool IsOverlappingWithExisting(BuildingType type, Vector3 position, ChunkCoord chunk)
		{
			Vector2 dimensions = BuildingDimensions[type];
			float radius = Mathf.Max(dimensions.X, dimensions.Y) / 2.0f;

			// Check current chunk and all adjacent chunks
			for (int dx = -1; dx <= 1; dx++)
			{
				for (int dz = -1; dz <= 1; dz++)
				{
					ChunkCoord checkChunk = new ChunkCoord(chunk.X + dx, chunk.Z + dz);
					
					if (!_buildingsByChunk.TryGetValue(checkChunk, out List<BuildingData> buildings))
						continue;

					foreach (BuildingData existing in buildings)
					{
						Vector2 existingDimensions = BuildingDimensions[existing.Type];
						float existingRadius = Mathf.Max(existingDimensions.X, existingDimensions.Y) / 2.0f;
						
						float distance = position.DistanceTo(existing.Position);
						float minDistance = radius + existingRadius + MinBuildingDistance;

						if (distance < minDistance)
						{
							return true;
						}
					}
				}
			}

			return false;
		}

		/// <summary>
		/// Placeholder for land ownership validation
		/// </summary>
		private bool IsOnPlayerLand(Vector3 position)
		{
			// TODO: Integrate with land ownership system when implemented
			return true;
		}

		/// <summary>
		/// Saves all building data to JSON with proper error handling
		/// </summary>
		public void Save()
		{
			try
			{
				var saveData = new BuildingSaveData
				{
					Buildings = _buildingsById.Values.Select(b => new SerializedBuilding
					{
						Id = b.Id.ToString(),
						Type = b.Type.ToString(),
						PositionX = b.Position.X,
						PositionY = b.Position.Y,
						PositionZ = b.Position.Z,
						Rotation = b.Rotation,
						ChunkX = b.ChunkCoord.X,
						ChunkZ = b.ChunkCoord.Z,
						Condition = b.Condition,
						PlacedAt = b.PlacedAt.ToString("o")
					}).ToList()
				};

				string json = JsonSerializer.Serialize(saveData, new JsonSerializerOptions { WriteIndented = true });
				string savePath = ProjectSettings.GlobalizePath(SavePath);
				
				File.WriteAllText(savePath, json);
				
				GD.Print($"[BuildingSystem] Saved {saveData.Buildings.Count} buildings to {savePath}");
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[BuildingSystem] Save failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Loads building data from JSON with comprehensive error handling
		/// </summary>
		public void Load()
		{
			try
			{
				string savePath = ProjectSettings.GlobalizePath(SavePath);
				
				if (!File.Exists(savePath))
				{
					GD.Print("[BuildingSystem] No save file found, starting with empty building set");
					return;
				}

				string json = File.ReadAllText(savePath);
				var saveData = JsonSerializer.Deserialize<BuildingSaveData>(json);

				if (saveData?.Buildings == null)
				{
					GD.PrintErr("[BuildingSystem] Invalid save data format");
					return;
				}

				_buildingsByChunk.Clear();
				_buildingsById.Clear();

				foreach (var serialized in saveData.Buildings)
				{
					try
					{
						var building = new BuildingData
						{
							Id = Guid.Parse(serialized.Id),
							Type = Enum.Parse<BuildingType>(serialized.Type),
							Position = new Vector3(serialized.PositionX, serialized.PositionY, serialized.PositionZ),
							Rotation = serialized.Rotation,
							ChunkCoord = new ChunkCoord(serialized.ChunkX, serialized.ChunkZ),
							Condition = Mathf.Clamp(serialized.Condition, 0f, 1f),
							PlacedAt = DateTime.Parse(serialized.PlacedAt)
						};

						if (!_buildingsByChunk.ContainsKey(building.ChunkCoord))
						{
							_buildingsByChunk[building.ChunkCoord] = new List<BuildingData>();
						}

						_buildingsByChunk[building.ChunkCoord].Add(building);
						_buildingsById[building.Id] = building;
					}
					catch (Exception ex)
					{
						GD.PrintErr($"[BuildingSystem] Failed to load building: {ex.Message}");
					}
				}

				GD.Print($"[BuildingSystem] Loaded {_buildingsById.Count} buildings successfully");
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[BuildingSystem] Load failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Gets statistics about placed buildings
		/// </summary>
		public Dictionary<BuildingType, int> GetBuildingStatistics()
		{
			var stats = new Dictionary<BuildingType, int>();
			
			foreach (BuildingType type in Enum.GetValues<BuildingType>())
			{
				stats[type] = 0;
			}

			foreach (var building in _buildingsById.Values)
			{
				stats[building.Type]++;
			}

			return stats;
		}
	}

	// Serialization helper classes
	public class BuildingSaveData
	{
		public List<SerializedBuilding> Buildings { get; set; } = new();
	}

	public class SerializedBuilding
	{
		public string Id { get; set; }
		public string Type { get; set; }
		public float PositionX { get; set; }
		public float PositionY { get; set; }
		public float PositionZ { get; set; }
		public float Rotation { get; set; }
		public int ChunkX { get; set; }
		public int ChunkZ { get; set; }
		public float Condition { get; set; }
		public string PlacedAt { get; set; }
	}
}
