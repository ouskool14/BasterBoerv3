using Godot;
using System.Collections.Generic;
using WorldStreaming.Flora;
using WorldStreaming.Terrain;

namespace WorldStreaming
{
	/// <summary>
	/// Represents one loaded chunk of the South African landscape.
	/// Contains terrain mesh with rich mask data and MultiMeshInstance3D nodes for flora.
	/// 
	/// Updated for TerrainSystem: Now consumes rich terrain payloads instead of
	/// simple mesh generation. All visual nodes are freed when chunk unloads -
	/// simulation data persists in FloraSystem.
	/// </summary>
	public partial class WorldChunk : Node3D
	{
		public ChunkCoord Coordinate { get; private set; }
		public bool IsLoaded { get; private set; }
		
		/// <summary>
		/// The rich terrain data for this chunk. Contains heightmaps, masks, and metadata.
		/// </summary>
		public TerrainChunkData TerrainData { get; private set; }

		[Export]
		public float ChunkSize { get; set; } = 256f;

		// Visual components
		private MeshInstance3D _terrainMeshInstance;
		private readonly Dictionary<byte, MultiMeshInstance3D> _floraMultiMeshes = 
			new Dictionary<byte, MultiMeshInstance3D>();
		private Node3D _buildingsContainer;
		
		// Terrain material with mask support
		private ShaderMaterial _terrainMaterial;

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
		/// Builds chunk content on background thread using TerrainSystem.
		/// Returns data to be applied on main thread via ApplyBuildResult.
		/// </summary>
		public ChunkBuildResult BuildChunkContent()
		{
			// Get rich terrain data from TerrainSystem
			TerrainData = TerrainSystem.Instance?.BuildChunk(Coordinate);
	
			if (TerrainData == null)
			{
				GD.PushError($"[WorldChunk] Failed to build terrain data for chunk {Coordinate}");
				TerrainData = TerrainChunkData.Create(Coordinate, ChunkSize, 64);
			}

			// Get flora data from FloraSystem (NEW API)
			List<FloraEntry> structuralEntries = FloraSystem.Instance?.GetStructuralForChunk(Coordinate) 
				?? new List<FloraEntry>();
			List<FloraPatch> patches = FloraSystem.Instance?.GetPatchesForChunk(Coordinate)
				?? new List<FloraPatch>();
			ChunkEcologyState ecology = FloraSystem.Instance?.GetChunkEcology(Coordinate)
				?? ChunkEcologyState.CreateNeutral();

			// Create MultiMesh instances for flora (NEW API)
			Dictionary<byte, MultiMesh> floraMultiMeshes = 
				FloraPopulator.CreateFloraMultiMeshes(structuralEntries, patches, Coordinate, ChunkSize, ecology);

			return new ChunkBuildResult
			{
				Coordinate = Coordinate,
				TerrainData = TerrainData,
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

			// Store terrain data reference
			TerrainData = buildResult.TerrainData;

			// Clear any existing content
			ClearVisualContent();

			// Create terrain mesh instance
			_terrainMeshInstance = new MeshInstance3D
			{
				Mesh = TerrainData?.TerrainMesh,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, // Low-poly aesthetic
				Name = "Terrain"
			};
			
			// Apply terrain shader material with mask support
			ApplyTerrainMaterial();
			
			AddChild(_terrainMeshInstance);

			// Collision - built from the same mesh so heights always match what the player sees.
			CreateTerrainCollision();

			// Create MultiMeshInstance3D for each flora type
			foreach (var kvp in buildResult.FloraMultiMeshes)
{
	byte archetypeId = kvp.Key;
	MultiMesh multiMesh = kvp.Value;

	// Handle patch archetypes (key > 127 means patch instance)
	bool isPatch = archetypeId >= 128;
	byte actualArchetypeId = isPatch ? (byte)(archetypeId - 128) : archetypeId;

	string namePrefix = isPatch ? "Patch_" : "Flora_";
	var mmi = new MultiMeshInstance3D
	{
		Multimesh = multiMesh,
		CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		Name = $"{namePrefix}{FloraArchetypeIds.GetDisplayName(actualArchetypeId)}"
	};

	float visibilityRange = FloraPopulator.GetVisibilityRange(actualArchetypeId);
	mmi.VisibilityRangeBegin = 0f;
	mmi.VisibilityRangeEnd = visibilityRange;
	mmi.VisibilityRangeEndMargin = 10f;

	AddChild(mmi);
	_floraMultiMeshes[archetypeId] = mmi;
}

			// Create container for buildings and infrastructure
			_buildingsContainer = new Node3D { Name = "Buildings" };
			AddChild(_buildingsContainer);

			IsLoaded = true;
			GD.Print($"[WorldChunk] Loaded {Coordinate} with " +
					$"{buildResult.FloraMultiMeshes.Count} flora types, " +
					$"build time: {TerrainData?.BuildTimeMs:F1}ms");
		}
		
		/// <summary>
		/// Applies the terrain shader material with mask support.
		/// </summary>
		private void ApplyTerrainMaterial()
		{
			// Create or load terrain shader material
			_terrainMaterial = new ShaderMaterial();
			
			// Try to load the custom terrain shader
			var shader = GD.Load<Shader>("res://shaders/terrain_system.gdshader");
			if (shader != null)
			{
				_terrainMaterial.Shader = shader;
			}
			else
			{
				// Fallback to basic shader
				GD.PushWarning("[WorldChunk] Terrain shader not found, using fallback");
				_terrainMaterial.Shader = GD.Load<Shader>("res://shaders/south_african_terrain.gdshader");
			}
			
			// Set shader parameters from terrain data
			if (TerrainData != null)
			{
				_terrainMaterial.SetShaderParameter("height_scale", TerrainData.MaxHeight);
				_terrainMaterial.SetShaderParameter("min_height", TerrainData.MinHeight);
				_terrainMaterial.SetShaderParameter("chunk_size", ChunkSize);
				_terrainMaterial.SetShaderParameter("world_origin", TerrainData.WorldOrigin);
				
				// Seasonal parameters
				var terrainSystem = TerrainSystem.Instance;
				if (terrainSystem != null)
				{
					_terrainMaterial.SetShaderParameter("global_wetness", terrainSystem.GlobalWetness);
					_terrainMaterial.SetShaderParameter("global_dryness", terrainSystem.GlobalDryness);
					_terrainMaterial.SetShaderParameter("green_bias", terrainSystem.SeasonalGreenBias);
				}
			}
			
			_terrainMeshInstance.MaterialOverride = _terrainMaterial;
		}
		
		/// <summary>
		/// Creates terrain collision from the mesh.
		/// </summary>
		private void CreateTerrainCollision()
		{
			if (TerrainData?.TerrainMesh == null) return;
			
			var concaveShape = TerrainData.TerrainMesh.CreateTrimeshShape();
			if (concaveShape != null)
			{
				// BackfaceCollision ensures the CharacterBody3D hits the surface regardless
				// of which side the physics engine approaches from (needed with Jolt).
				concaveShape.BackfaceCollision = true;

				var terrainCollision = new CollisionShape3D
				{
					Shape = concaveShape,
					Name = "TerrainCollision"
				};
				var terrainBody = new StaticBody3D { Name = "TerrainBody" };
				terrainBody.AddChild(terrainCollision);
				AddChild(terrainBody);
			}
			else
			{
				GD.PushError($"[WorldChunk] CreateTrimeshShape returned null for chunk {Coordinate} — no collision!");
			}
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
		/// Gets terrain height at a local position within this chunk.
		/// </summary>
		public float GetHeightAtLocal(Vector3 localPos)
		{
			if (TerrainData == null) return 0f;
			
			Vector3 worldPos = Position + localPos;
			return TerrainData.GetHeightAtWorld(worldPos);
		}
		
		/// <summary>
		/// Gets terrain sample at a local position within this chunk.
		/// </summary>
		public TerrainSample GetSampleAtLocal(Vector3 localPos)
		{
			if (TerrainData == null) return default;
			
			Vector3 worldPos = Position + localPos;
			return TerrainData.GetSampleAtWorld(worldPos);
		}
		
		/// <summary>
		/// Updates seasonal visual state for this chunk.
		/// </summary>
		public void UpdateSeasonalState(float wetness, float dryness, float greenBias)
		{
			if (_terrainMaterial != null)
			{
				_terrainMaterial.SetShaderParameter("global_wetness", wetness);
				_terrainMaterial.SetShaderParameter("global_dryness", dryness);
				_terrainMaterial.SetShaderParameter("green_bias", greenBias);
			}
		}

		/// <summary>
		/// Unloads all visual content. Called when chunk moves out of active range.
		/// Simulation data in FloraSystem remains intact.
		/// </summary>
		public void UnloadChunk()
		{
			ClearVisualContent();
			TerrainData = null;
			IsLoaded = false;
			GD.Print($"[WorldChunk] Unloaded {Coordinate}");
		}

		/// <summary>
		/// Reloads only the flora visuals for this chunk.
		/// Called when ecology state changes significantly.
	 /// </summary>
	 public void ReloadFloraVisuals()
	 {
		 if (!IsLoaded) return;

		 // Remove existing flora MultiMeshes
		 foreach (var mmi in _floraMultiMeshes.Values)
		 {
			 if (mmi != null && IsInstanceValid(mmi))
			 {
				 mmi.QueueFree();
			 }
		 }
		 _floraMultiMeshes.Clear();

		 // Get fresh flora data
		 List<FloraEntry> structuralEntries = FloraSystem.Instance?.GetStructuralForChunk(Coordinate) 
			 ?? new List<FloraEntry>();
		 List<FloraPatch> patches = FloraSystem.Instance?.GetPatchesForChunk(Coordinate)
			 ?? new List<FloraPatch>();
		 ChunkEcologyState ecology = FloraSystem.Instance?.GetChunkEcology(Coordinate)
			 ?? ChunkEcologyState.CreateNeutral();

		 // Create new MultiMeshes
		 Dictionary<byte, MultiMesh> floraMultiMeshes = 
			 FloraPopulator.CreateFloraMultiMeshes(structuralEntries, patches, Coordinate, ChunkSize, ecology);

		 // Add to scene (same code as ApplyBuildResult)
		 foreach (var kvp in floraMultiMeshes)
		 {
			 byte archetypeId = kvp.Key;
			 MultiMesh multiMesh = kvp.Value;

			 bool isPatch = archetypeId >= 128;
			 byte actualArchetypeId = isPatch ? (byte)(archetypeId - 128) : archetypeId;

			 string namePrefix = isPatch ? "Patch_" : "Flora_";
			 var mmi = new MultiMeshInstance3D
			 {
				 Multimesh = multiMesh,
				 CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				 Name = $"{namePrefix}{FloraArchetypeIds.GetDisplayName(actualArchetypeId)}"
			 };

			 float visibilityRange = FloraPopulator.GetVisibilityRange(actualArchetypeId);
			 mmi.VisibilityRangeBegin = 0f;
			 mmi.VisibilityRangeEnd = visibilityRange;
			 mmi.VisibilityRangeEndMargin = 10f;

			 AddChild(mmi);
			 _floraMultiMeshes[archetypeId] = mmi;
		 }

		 GD.Print($"[WorldChunk] Reloaded flora visuals for {Coordinate}");
	 }

		private void ClearVisualContent()
		{
			// Free terrain
			if (_terrainMeshInstance != null)
			{
				_terrainMeshInstance.QueueFree();
				_terrainMeshInstance = null;
			}
			_terrainMaterial = null;

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

		public override void _ExitTree()
		{
			UnloadChunk();
			base._ExitTree();
		}

		/// <summary>
		/// Data structure for passing build results from background thread to main thread.
		/// Updated to include rich TerrainChunkData.
		/// </summary>
		public struct ChunkBuildResult
		{
			public ChunkCoord Coordinate;
			public TerrainChunkData TerrainData;
			public Dictionary<byte, MultiMesh> FloraMultiMeshes;
		}
	}
}
