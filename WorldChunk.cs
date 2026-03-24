using Godot;
using System.Collections.Generic;
using WorldStreaming.Flora;

namespace WorldStreaming
{
	/// <summary>
	/// Represents one loaded 256m x 256m chunk of the South African landscape.
	/// Contains terrain mesh and MultiMeshInstance3D nodes for flora.
	/// All visual nodes are freed when chunk unloads - simulation data persists in FloraSystem.
	/// </summary>
	public partial class WorldChunk : Node3D
	{
		public ChunkCoord Coordinate { get; private set; }
		public bool IsLoaded { get; private set; }

		[Export]
		public float ChunkSize { get; set; } = 256f;

		// Visual components
		private MeshInstance3D _terrainMeshInstance;
		private readonly Dictionary<FloraType, MultiMeshInstance3D> _floraMultiMeshes = 
			new Dictionary<FloraType, MultiMeshInstance3D>();
		private Node3D _buildingsContainer;

		/// <summary>
		/// Initializes chunk with its coordinate and world position.
		/// Called immediately after instantiation by WorldChunkStreamer.
		/// </summary>
		public void Initialize(ChunkCoord coord, float chunkSize = 256f)
		{
			Coordinate = coord;
			ChunkSize = chunkSize;
			Name = $"Chunk_{coord.X}_{coord.Z}";
			Position = coord.GetWorldOrigin(chunkSize);
			IsLoaded = false;
		}

		/// <summary>
		/// Builds chunk content on background thread.
		/// Returns data to be applied on main thread via ApplyBuildResult.
		/// </summary>
		public ChunkBuildResult BuildChunkContent()
		{
			// Generate terrain mesh
			ArrayMesh terrainMesh = TerrainGenerator.GenerateTerrainMesh(Coordinate, ChunkSize);

			// Get flora data from FloraSystem
			List<FloraEntry> floraEntries = FloraSystem.Instance.GetFloraForChunk(Coordinate);

			// Create MultiMesh instances for flora
			Dictionary<FloraType, MultiMesh> floraMultiMeshes = 
				FloraPopulator.CreateFloraMultiMeshes(floraEntries, Coordinate, ChunkSize);

			return new ChunkBuildResult
			{
				Coordinate = Coordinate,
				TerrainMesh = terrainMesh,
				FloraMultiMeshes = floraMultiMeshes
			};
		}

		/// <summary>
		/// Applies build result to scene tree. Must be called on main thread.
		/// </summary>
		public void ApplyBuildResult(ChunkBuildResult buildResult)
		{
			if (buildResult.Coordinate != Coordinate)
			{
				GD.PushError($"[WorldChunk] Build result coordinate mismatch: " +
						   $"expected {Coordinate}, got {buildResult.Coordinate}");
				return;
			}

			// Clear any existing content
			ClearVisualContent();

			// Create terrain mesh instance
			_terrainMeshInstance = new MeshInstance3D
			{
				Mesh = buildResult.TerrainMesh,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, // Low-poly aesthetic
				Name = "Terrain"
			};
			
			// TODO: Apply terrain shader material with South African parameters
			// var terrainMaterial = new ShaderMaterial();
			// terrainMaterial.Shader = GD.Load<Shader>("res://shaders/south_african_terrain.gdshader");
			// terrainMaterial.SetShaderParameter("height_scale", 25.0f);
			// terrainMaterial.SetShaderParameter("moisture_contrast", 1.2f);
			// _terrainMeshInstance.MaterialOverride = terrainMaterial;

			AddChild(_terrainMeshInstance);

			// Create MultiMeshInstance3D for each flora type
			foreach (var kvp in buildResult.FloraMultiMeshes)
			{
				FloraType type = kvp.Key;
				MultiMesh multiMesh = kvp.Value;

				var mmi = new MultiMeshInstance3D
				{
					Multimesh = multiMesh,
					CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, // Performance
					Name = $"Flora_{type}"
				};

				// Set visibility range based on flora type
				float visibilityRange = GetFloraVisibilityRange(type);
				mmi.VisibilityRangeBegin = 0f;
				mmi.VisibilityRangeEnd = visibilityRange;
				mmi.VisibilityRangeEndMargin = 10f;

				AddChild(mmi);
				_floraMultiMeshes[type] = mmi;
			}

			// Create container for buildings and infrastructure
			_buildingsContainer = new Node3D { Name = "Buildings" };
			AddChild(_buildingsContainer);

			// TODO: Load any persistent buildings/infrastructure for this chunk

			IsLoaded = true;
			GD.Print($"[WorldChunk] Loaded {Coordinate} with {buildResult.FloraMultiMeshes.Count} flora types");
		}

		/// <summary>
		/// Adds a building or infrastructure node to this chunk.
		/// Buildings persist between chunk loads/unloads.
		/// </summary>
		public void AddBuilding(Node3D buildingNode)
		{
			if (_buildingsContainer != null)
			{
				_buildingsContainer.AddChild(buildingNode);
			}
			else
			{
				GD.PushWarning($"[WorldChunk] Cannot add building - chunk {Coordinate} not loaded");
			}
		}

		/// <summary>
		/// Unloads all visual content. Called when chunk moves out of active range.
		/// Simulation data in FloraSystem remains intact.
		/// </summary>
		public void UnloadChunk()
		{
			ClearVisualContent();
			IsLoaded = false;
			GD.Print($"[WorldChunk] Unloaded {Coordinate}");
		}

		private void ClearVisualContent()
		{
			// Free terrain
			if (_terrainMeshInstance != null)
			{
				_terrainMeshInstance.QueueFree();
				_terrainMeshInstance = null;
			}

			// Free flora MultiMeshes
			foreach (var mmi in _floraMultiMeshes.Values)
			{
				if (mmi != null && IsInstanceValid(mmi))
				{
					mmi.QueueFree();
				}
			}
			_floraMultiMeshes.Clear();

			// Free buildings container (TODO: save building state first)
			if (_buildingsContainer != null)
			{
				// TODO: Serialize building states to persistence layer
				_buildingsContainer.QueueFree();
				_buildingsContainer = null;
			}
		}

		private static float GetFloraVisibilityRange(FloraType type)
		{
			return type switch
			{
				FloraType.RedGrass or FloraType.PanicGrass => 80f,  // Grass - short range
				FloraType.MagicGuarana or FloraType.SicklebushDichrostachys or 
				FloraType.InvasiveLantana or FloraType.InvasiveBugweed => 150f, // Shrubs - medium
				_ => 300f // Trees - long range
			};
		}

		public override void _ExitTree()
		{
			UnloadChunk();
			base._ExitTree();
		}

		/// <summary>
		/// Data structure for passing build results from background thread to main thread.
		/// </summary>
		public struct ChunkBuildResult
		{
			public ChunkCoord Coordinate;
			public ArrayMesh TerrainMesh;
			public Dictionary<FloraType, MultiMesh> FloraMultiMeshes;
		}
	}
}
