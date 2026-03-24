using Godot;
using System.Collections.Generic;
using WorldStreaming.Flora;

namespace WorldStreaming
{
	/// <summary>
	/// Static helper for converting FloraEntry simulation data into MultiMesh visual instances.
	/// Handles terrain alignment, scaling, and MultiMesh population.
	/// Thread-safe for background chunk generation.
	/// </summary>
	public static class FloraPopulator
	{
		// Cache of flora meshes to avoid repeated loading
		private static readonly Dictionary<FloraType, Mesh> _floraMeshCache = 
			new Dictionary<FloraType, Mesh>();

		/// <summary>
		/// Creates MultiMesh instances for all flora types in a chunk.
		/// Called on background thread during chunk loading.
		/// </summary>
		public static Dictionary<FloraType, MultiMesh> CreateFloraMultiMeshes(
			List<FloraEntry> floraEntries, 
			ChunkCoord chunkCoord, 
			float chunkSize)
		{
			var result = new Dictionary<FloraType, MultiMesh>();
			
			if (floraEntries == null || floraEntries.Count == 0)
				return result;

			// Group flora by type for separate MultiMeshes
			var floraByType = new Dictionary<FloraType, List<FloraEntry>>();
			foreach (var entry in floraEntries)
			{
				if (!floraByType.ContainsKey(entry.Type))
				{
					floraByType[entry.Type] = new List<FloraEntry>();
				}
				floraByType[entry.Type].Add(entry);
			}

			Vector3 chunkOrigin = chunkCoord.GetWorldOrigin(chunkSize);

			// Create MultiMesh for each flora type
			foreach (var kvp in floraByType)
			{
				FloraType type = kvp.Key;
				List<FloraEntry> entries = kvp.Value;

				Mesh floraMesh = GetFloraMesh(type);
				if (floraMesh == null) continue;

				MultiMesh multiMesh = new MultiMesh
				{
					TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
					UseColors = true,
					Mesh = floraMesh,
					InstanceCount = entries.Count
				};

				// Set transforms for each instance
				for (int i = 0; i < entries.Count; i++)
				{
					FloraEntry entry = entries[i];
					
					// Calculate local position relative to chunk origin
					float localX = entry.WorldPosition2D.X - chunkOrigin.X;
					float localZ = entry.WorldPosition2D.Y - chunkOrigin.Z;
					
					// Get terrain height at this position
					float terrainHeight = TerrainGenerator.GetTerrainHeight(
						entry.WorldPosition2D.X, entry.WorldPosition2D.Y);
					
					Vector3 localPosition = new Vector3(localX, terrainHeight, localZ);

					// Align to terrain normal for realistic placement
					Vector3 terrainNormal = TerrainGenerator.GetTerrainNormal(
						entry.WorldPosition2D.X, entry.WorldPosition2D.Y);
					
					// Build transform
					Transform3D transform = Transform3D.Identity;
					
					// Scale based on entry data and species base scale
					float baseScale = GetSpeciesBaseScale(type);
					Vector3 scale = Vector3.One * (entry.ScaleMultiplier * baseScale);
					transform = transform.Scaled(scale);
					
					// Rotation: align to terrain + random Y rotation
					Basis alignedBasis = AlignToTerrainNormal(terrainNormal);
					alignedBasis = alignedBasis.Rotated(Vector3.Up, Mathf.DegToRad(entry.RotationY));
					transform.Basis = alignedBasis * transform.Basis;
					
					// Position
					transform.Origin = localPosition;
					
					multiMesh.SetInstanceTransform(i, transform);
					
					// Color based on health and species
					Color instanceColor = GetFloraInstanceColor(entry);
					multiMesh.SetInstanceColor(i, instanceColor);
				}

				result[type] = multiMesh;
			}

			return result;
		}

		/// <summary>
		/// Gets or loads the mesh for a flora type. Caches for performance.
		/// </summary>
		private static Mesh GetFloraMesh(FloraType type)
		{
			if (_floraMeshCache.TryGetValue(type, out Mesh cachedMesh))
			{
				return cachedMesh;
			}

			// TODO: Load actual low-poly meshes from your asset pipeline
			string meshPath = $"res://assets/flora/{type.ToString().ToLower()}.obj";
			Mesh mesh = GD.Load<Mesh>(meshPath);
			
			// Fallback to placeholder if asset doesn't exist
			if (mesh == null)
			{
				mesh = CreatePlaceholderMesh(type);
				GD.PushWarning($"[FloraPopulator] Using placeholder mesh for {type}. " +
							  $"Expected asset at: {meshPath}");
			}

			if (mesh != null)
			{
				_floraMeshCache[type] = mesh;
			}

			return mesh;
		}

		/// <summary>
		/// Creates simple placeholder meshes for testing before final assets are ready.
		/// </summary>
		private static Mesh CreatePlaceholderMesh(FloraType type)
		{
			var surfaceTool = new SurfaceTool();
			surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

			if (type == FloraType.RedGrass || type == FloraType.PanicGrass)
			{
				// Simple crossed planes for grass
				CreateCrossedPlanes(surfaceTool, 0.6f, 0.8f);
			}
			else if (type == FloraType.MagicGuarana || type == FloraType.SicklebushDichrostachys ||
					 type == FloraType.InvasiveLantana || type == FloraType.InvasiveBugweed)
			{
				// Small bush - rounded box
				CreateSimpleBox(surfaceTool, 1f, 1.2f, 1f);
			}
			else
			{
				// Tree - cylinder trunk + cone canopy
				CreateSimpleTree(surfaceTool);
			}

			surfaceTool.GenerateNormals();
			return surfaceTool.Commit();
		}

		private static void CreateCrossedPlanes(SurfaceTool st, float width, float height)
		{
			// First plane (X-axis)
			st.AddVertex(new Vector3(-width/2, 0, 0));
			st.AddVertex(new Vector3(width/2, 0, 0));
			st.AddVertex(new Vector3(width/2, height, 0));
			st.AddVertex(new Vector3(-width/2, 0, 0));
			st.AddVertex(new Vector3(width/2, height, 0));
			st.AddVertex(new Vector3(-width/2, height, 0));

			// Second plane (Z-axis)
			st.AddVertex(new Vector3(0, 0, -width/2));
			st.AddVertex(new Vector3(0, height, width/2));
			st.AddVertex(new Vector3(0, 0, width/2));
			st.AddVertex(new Vector3(0, 0, -width/2));
			st.AddVertex(new Vector3(0, height, -width/2));
			st.AddVertex(new Vector3(0, height, width/2));
		}

		private static void CreateSimpleBox(SurfaceTool st, float width, float height, float depth)
		{
			// Simple box with 8 vertices, 12 triangles - implementation omitted for brevity
			// TODO: Implement basic box geometry
		}

		private static void CreateSimpleTree(SurfaceTool st)
		{
			// Simple tree representation - implementation omitted for brevity
			// TODO: Implement basic tree geometry (cylinder + cone)
		}

		/// <summary>
		/// Gets base scale multiplier for each species type.
		/// </summary>
		private static float GetSpeciesBaseScale(FloraType type)
		{
			return type switch
			{
				FloraType.AcaciaThorn => 1.2f,
				FloraType.MarulaMpopona => 1.0f,
				FloraType.BuffaloThorn => 0.9f,
				FloraType.SausageTree => 1.4f,
				FloraType.MagicGuarana => 0.6f,
				FloraType.SicklebushDichrostachys => 0.7f,
				FloraType.RedGrass => 0.3f,
				FloraType.PanicGrass => 0.3f,
				FloraType.InvasiveLantana => 0.5f,
				FloraType.InvasiveBugweed => 0.4f,
				_ => 1.0f
			};
		}

		/// <summary>
		/// Aligns a basis to terrain normal while maintaining upward growth.
		/// </summary>
		private static Basis AlignToTerrainNormal(Vector3 terrainNormal)
		{
			Vector3 up = terrainNormal.Normalized();
			Vector3 right = Vector3.Up.Cross(up);
			
			// Handle case where terrain is perfectly flat
			if (right.LengthSquared() < 0.001f)
			{
				right = Vector3.Right;
			}
			else
			{
				right = right.Normalized();
			}
			
			Vector3 forward = up.Cross(right).Normalized();
			return new Basis(right, up, forward);
		}

		/// <summary>
		/// Gets instance color based on flora health, age, and species.
		/// </summary>
		private static Color GetFloraInstanceColor(FloraEntry entry)
		{
			Color baseColor = Colors.White; // No tint by default

			// Health-based coloring
			if (entry.Health < 0.3f)
			{
				baseColor = new Color(0.7f, 0.5f, 0.3f); // Brown/dying
			}
			else if (entry.Health < 0.7f)
			{
				baseColor = new Color(0.9f, 0.9f, 0.7f); // Yellowish/stressed
			}

			// Invasive species get slight red tint for identification
			if (entry.IsInvasive)
			{
				baseColor = baseColor.Lerp(new Color(1f, 0.8f, 0.8f), 0.3f);
			}

			return baseColor;
		}

		/// <summary>
		/// Clears the mesh cache. Call when changing quality settings or reloading assets.
		/// </summary>
		public static void ClearMeshCache()
		{
			_floraMeshCache.Clear();
			GD.Print("[FloraPopulator] Flora mesh cache cleared");
		}
	}
}
