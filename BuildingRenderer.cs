using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using WorldStreaming;

namespace Basterboer.Buildings
{
	/// <summary>
	/// Handles visual representation of buildings using MultiMeshInstance3D for performance.
	/// Integrates with WorldChunkStreamer for chunk-based loading/unloading.
	/// </summary>
	public partial class BuildingRenderer : Node3D
	{
		private static BuildingRenderer _instance;
		public static BuildingRenderer Instance => _instance;

		// Cache of rendered meshes per chunk
		private Dictionary<ChunkCoord, List<Node3D>> _chunkMeshes = new();
		private Dictionary<ChunkCoord, Dictionary<BuildingType, MultiMeshInstance3D>> _chunkMultiMeshes = new();

		// Material cache for building types
		private Dictionary<BuildingType, StandardMaterial3D> _buildingMaterials = new();

		public override void _Ready()
		{
			if (_instance != null && _instance != this)
			{
				QueueFree();
				return;
			}

			_instance = this;

			InitializeMaterials();
			ConnectToSystems();

			GD.Print("[BuildingRenderer] Initialized successfully");
		}

		/// <summary>
		/// Creates distinct materials for each building type
		/// </summary>
		private void InitializeMaterials()
		{
			var materialConfigs = new Dictionary<BuildingType, Color>
			{
				{ BuildingType.Wall, new Color(0.85f, 0.75f, 0.65f) },        // Light brown/beige
				{ BuildingType.Roof, new Color(0.6f, 0.3f, 0.2f) },           // Reddish brown
				{ BuildingType.Floor, new Color(0.7f, 0.7f, 0.7f) },          // Light gray
				{ BuildingType.Door, new Color(0.5f, 0.3f, 0.2f) },           // Dark brown
				{ BuildingType.Window, new Color(0.6f, 0.8f, 0.9f) },         // Light blue
				{ BuildingType.Stoep, new Color(0.75f, 0.65f, 0.55f) },       // Warm beige
				{ BuildingType.BraaiArea, new Color(0.3f, 0.3f, 0.3f) },      // Dark gray
				{ BuildingType.WaterTrough, new Color(0.4f, 0.6f, 0.8f) },    // Blue
				{ BuildingType.FeedingStation, new Color(0.7f, 0.6f, 0.4f) }, // Tan
				{ BuildingType.StaffQuarters, new Color(0.8f, 0.7f, 0.6f) },  // Light brown
				{ BuildingType.Boma, new Color(0.6f, 0.5f, 0.4f) },           // Medium brown
				{ BuildingType.Hide, new Color(0.4f, 0.5f, 0.3f) }            // Olive green
			};

			foreach (var config in materialConfigs)
			{
				var material = new StandardMaterial3D
				{
					AlbedoColor = config.Value,
					Roughness = 0.8f,
					Metallic = 0.1f
				};
				
				_buildingMaterials[config.Key] = material;
			}
		}

		/// <summary>
		/// Connects to BuildingSystem and WorldChunkStreamer
		/// </summary>
		private void ConnectToSystems()
		{
			// Connect to BuildingSystem signals
			if (BuildingSystem.Instance != null)
			{
				BuildingSystem.Instance.BuildingPlaced += OnBuildingPlaced;
				BuildingSystem.Instance.BuildingRemoved += OnBuildingRemoved;
			}

			// Connect to WorldChunkStreamer - this should be called from WorldChunkStreamer
			// when chunks are loaded/unloaded (see integration section below)
		}

		/// <summary>
		/// Called when a building is placed - updates the relevant chunk
		/// </summary>
		private void OnBuildingPlaced(BuildingData building)
		{
			// If the chunk is currently loaded, re-render it
			if (_chunkMeshes.ContainsKey(building.ChunkCoord))
			{
				UnrenderChunkBuildings(building.ChunkCoord);
				RenderChunkBuildings(building.ChunkCoord);
			}
		}

		/// <summary>
		/// Called when a building is removed - updates the relevant chunk
		/// </summary>
		private void OnBuildingRemoved(Guid buildingId, ChunkCoord chunk)
		{
			// If the chunk is currently loaded, re-render it
			if (_chunkMeshes.ContainsKey(chunk))
			{
				UnrenderChunkBuildings(chunk);
				RenderChunkBuildings(chunk);
			}
		}

		/// <summary>
		/// Called by WorldChunkStreamer when a chunk is loaded
		/// </summary>
		public void OnChunkLoaded(ChunkCoord chunk)
		{
			RenderChunkBuildings(chunk);
		}

		/// <summary>
		/// Called by WorldChunkStreamer when a chunk is unloaded
		/// </summary>
		public void OnChunkUnloaded(ChunkCoord chunk)
		{
			UnrenderChunkBuildings(chunk);
		}

		/// <summary>
		/// Renders all buildings in a chunk using MultiMesh for performance
		/// </summary>
		private void RenderChunkBuildings(ChunkCoord chunk)
		{
			if (BuildingSystem.Instance == null)
				return;

			List<BuildingData> buildings = BuildingSystem.Instance.GetBuildingsInChunk(chunk);

			if (buildings.Count == 0)
				return;

			// Group buildings by type for efficient MultiMesh rendering
			var buildingsByType = buildings.GroupBy(b => b.Type)
										 .ToDictionary(g => g.Key, g => g.ToList());

			_chunkMeshes[chunk] = new List<Node3D>();
			_chunkMultiMeshes[chunk] = new Dictionary<BuildingType, MultiMeshInstance3D>();

			foreach (var kvp in buildingsByType)
			{
				BuildingType type = kvp.Key;
				List<BuildingData> typedBuildings = kvp.Value;

				if (typedBuildings.Count > 1)
				{
					// Use MultiMesh for multiple instances of the same type
					CreateMultiMeshForBuildings(chunk, type, typedBuildings);
				}
				else
				{
					// Use single MeshInstance3D for lone buildings
					CreateSingleMeshForBuilding(chunk, typedBuildings[0]);
				}
			}

			GD.Print($"[BuildingRenderer] Rendered {buildings.Count} buildings in chunk {chunk}");
		}

		/// <summary>
		/// Creates a MultiMeshInstance3D for multiple buildings of the same type
		/// </summary>
		private void CreateMultiMeshForBuildings(ChunkCoord chunk, BuildingType type, List<BuildingData> buildings)
		{
			var multiMeshInstance = new MultiMeshInstance3D();
			AddChild(multiMeshInstance);

			var multiMesh = new MultiMesh
			{
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				InstanceCount = buildings.Count,
				Mesh = CreateMeshForBuildingType(type)
			};

			// Set transform for each building instance
			for (int i = 0; i < buildings.Count; i++)
			{
				BuildingData building = buildings[i];
				
				Transform3D transform = Transform3D.Identity;
				transform.Origin = building.Position;
				transform.Basis = Basis.Identity.Rotated(Vector3.Up, building.Rotation);

				multiMesh.SetInstanceTransform(i, transform);
			}

			multiMeshInstance.Multimesh = multiMesh;
			multiMeshInstance.MaterialOverride = _buildingMaterials[type];

			_chunkMultiMeshes[chunk][type] = multiMeshInstance;
			_chunkMeshes[chunk].Add(multiMeshInstance);
		}

		/// <summary>
		/// Creates a single MeshInstance3D for one building
		/// </summary>
		private void CreateSingleMeshForBuilding(ChunkCoord chunk, BuildingData building)
		{
			var meshInstance = new MeshInstance3D
			{
				Mesh = CreateMeshForBuildingType(building.Type),
				MaterialOverride = _buildingMaterials[building.Type],
				Position = building.Position,
				Rotation = new Vector3(0, building.Rotation, 0)
			};

			AddChild(meshInstance);
			_chunkMeshes[chunk].Add(meshInstance);
		}

		/// <summary>
		/// Creates appropriate mesh for each building type (placeholder primitives)
		/// </summary>
		private Mesh CreateMeshForBuildingType(BuildingType type)
		{
			Vector2 dimensions = BuildingSystem.BuildingDimensions[type];

			return type switch
			{
				BuildingType.Wall => new BoxMesh 
				{ 
					Size = new Vector3(dimensions.X, 3.0f, dimensions.Y) 
				},
				BuildingType.Roof => new PrismMesh 
				{ 
					Size = new Vector3(dimensions.X, 2.0f, dimensions.Y) 
				},
				BuildingType.Floor or BuildingType.Stoep => new BoxMesh 
				{ 
					Size = new Vector3(dimensions.X, 0.2f, dimensions.Y) 
				},
				BuildingType.Door => new BoxMesh 
				{ 
					Size = new Vector3(dimensions.X, 2.5f, dimensions.Y) 
				},
				BuildingType.Window => new BoxMesh 
				{ 
					Size = new Vector3(dimensions.X, 1.5f, dimensions.Y) 
				},
				BuildingType.WaterTrough or BuildingType.FeedingStation => new BoxMesh 
				{ 
					Size = new Vector3(dimensions.X, 0.6f, dimensions.Y) 
				},
				BuildingType.BraaiArea or BuildingType.Boma => new CylinderMesh 
				{ 
					TopRadius = dimensions.X / 2.0f, 
					BottomRadius = dimensions.X / 2.0f, 
					Height = 1.0f 
				},
				BuildingType.StaffQuarters or BuildingType.Hide => new BoxMesh 
				{ 
					Size = new Vector3(dimensions.X, 3.5f, dimensions.Y) 
				},
				_ => new BoxMesh { Size = new Vector3(dimensions.X, 2.0f, dimensions.Y) }
			};
		}

		/// <summary>
		/// Removes all rendered buildings from a chunk
		/// </summary>
		private void UnrenderChunkBuildings(ChunkCoord chunk)
		{
			if (_chunkMeshes.TryGetValue(chunk, out List<Node3D> meshes))
			{
				foreach (Node3D mesh in meshes)
				{
					mesh.QueueFree();
				}

				_chunkMeshes.Remove(chunk);
			}

			if (_chunkMultiMeshes.ContainsKey(chunk))
			{
				_chunkMultiMeshes.Remove(chunk);
			}
		}

		/// <summary>
		/// Forces refresh of all currently loaded chunks
		/// </summary>
		public void RefreshAllChunks()
		{
			var loadedChunks = new List<ChunkCoord>(_chunkMeshes.Keys);

			foreach (ChunkCoord chunk in loadedChunks)
			{
				UnrenderChunkBuildings(chunk);
				RenderChunkBuildings(chunk);
			}

			GD.Print($"[BuildingRenderer] Refreshed {loadedChunks.Count} chunks");
		}
	}
}
