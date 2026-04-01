using Godot;

namespace WorldStreaming
{
	/// <summary>
	/// Static helper for generating flat-shaded, low-poly terrain meshes.
	/// Designed for South African bushveld aesthetic with warm earth tones.
	/// Thread-safe - can be called from background threads.
	/// </summary>
	public static class TerrainGenerator
	{
		// South African landscape parameters
		private const float HEIGHT_SCALE = 25f;      // Maximum elevation variation
		private const float NOISE_FREQUENCY = 0.003f; // Terrain feature scale
		private const int TERRAIN_RESOLUTION = 128;    // Vertices per chunk side

		private static FastNoiseLite _heightNoise;
		private static FastNoiseLite _moistureNoise;

		static TerrainGenerator()
		{
			// Height noise for primary terrain features
			_heightNoise = new FastNoiseLite
			{
				NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
				Seed = 12345,
				Frequency = NOISE_FREQUENCY,
				FractalOctaves = 4,
				FractalLacunarity = 2.0f,
				FractalGain = 0.5f
			};

			// Moisture noise for biome variation
			_moistureNoise = new FastNoiseLite
			{
				NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular,
				Seed = 67890,
				Frequency = NOISE_FREQUENCY * 0.7f
			};
		}

		/// <summary>
		/// Generates a flat-shaded terrain mesh for a chunk.
		/// Called on background thread during chunk loading.
		/// </summary>
		public static ArrayMesh GenerateTerrainMesh(ChunkCoord coord, float chunkSize)
		{
			return GenerateTerrainMesh(coord.GetWorldOrigin(chunkSize), chunkSize);
		}

		/// <summary>
		/// Generates terrain mesh with explicit world origin for noise sampling.
		/// Mesh vertices are in local space centered on chunkSize/2.
		/// </summary>
		public static ArrayMesh GenerateTerrainMesh(Vector3 worldOrigin, float chunkSize)
		{
			float vertexSpacing = chunkSize / (TERRAIN_RESOLUTION - 1);
			float halfSize = chunkSize * 0.5f;

			var surfaceTool = new SurfaceTool();
			surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

			// Generate flat-shaded quads
			for (int z = 0; z < TERRAIN_RESOLUTION - 1; z++)
			{
				for (int x = 0; x < TERRAIN_RESOLUTION - 1; x++)
				{
					// Local vertices centered on chunk (range: -halfSize to +halfSize)
					float lx0 = x * vertexSpacing - halfSize;
					float lx1 = (x + 1) * vertexSpacing - halfSize;
					float lz0 = z * vertexSpacing - halfSize;
					float lz1 = (z + 1) * vertexSpacing - halfSize;

					// World positions for noise sampling — must match local vertex
					// positions so that when the mesh is placed at worldOrigin,
					// the height at each vertex corresponds to the correct world coord.
					float wx0 = worldOrigin.X + lx0;
					float wx1 = worldOrigin.X + lx1;
					float wz0 = worldOrigin.Z + lz0;
					float wz1 = worldOrigin.Z + lz1;

					// Heights from world positions
					float h00 = GetTerrainHeight(wx0, wz0);
					float h10 = GetTerrainHeight(wx1, wz0);
					float h11 = GetTerrainHeight(wx1, wz1);
					float h01 = GetTerrainHeight(wx0, wz1);

					Vector3 v0 = new Vector3(lx0, h00, lz0);
					Vector3 v1 = new Vector3(lx1, h10, lz0);
					Vector3 v2 = new Vector3(lx1, h11, lz1);
					Vector3 v3 = new Vector3(lx0, h01, lz1);

					// Calculate flat normals for each triangle
					Vector3 normal1 = CalculateFlatNormal(v0, v1, v2);
					Vector3 normal2 = CalculateFlatNormal(v0, v2, v3);

					// Get South African terrain colors
					Color c0 = GetSouthAfricanTerrainColor(wx0, wz0, v0.Y);
					Color c1 = GetSouthAfricanTerrainColor(wx1, wz0, v1.Y);
					Color c2 = GetSouthAfricanTerrainColor(wx1, wz1, v2.Y);
					Color c3 = GetSouthAfricanTerrainColor(wx0, wz1, v3.Y);

					// Add first triangle (v0, v1, v2)
					AddFlatTriangle(surfaceTool, v0, v1, v2, normal1, c0, c1, c2);
					
					// Add second triangle (v0, v2, v3)
					AddFlatTriangle(surfaceTool, v0, v2, v3, normal2, c0, c2, c3);
				}
			}

			return surfaceTool.Commit();
		}

		/// <summary>
		/// Gets terrain height at world position. Must be consistent with FloraPopulator.
		/// </summary>
		public static float GetTerrainHeight(float worldX, float worldZ)
		{
			float noiseValue = _heightNoise.GetNoise2D(worldX, worldZ);
			
			// Remap from [-1, 1] to [0, HEIGHT_SCALE] with gentle rolling hills
			float height = (noiseValue + 1f) * 0.5f * HEIGHT_SCALE;
			
			// Add subtle detail for South African bushveld undulation
			float detailNoise = _heightNoise.GetNoise2D(worldX * 3f, worldZ * 3f) * 0.2f;
			height += detailNoise * HEIGHT_SCALE * 0.1f;
			
			return Mathf.Max(0f, height);
		}

		/// <summary>
		/// Gets terrain normal for proper flora placement alignment.
		/// </summary>
		public static Vector3 GetTerrainNormal(float worldX, float worldZ, float sampleDistance = 2f)
		{
			float heightCenter = GetTerrainHeight(worldX, worldZ);
			float heightRight = GetTerrainHeight(worldX + sampleDistance, worldZ);
			float heightUp = GetTerrainHeight(worldX, worldZ + sampleDistance);

			Vector3 tangentX = new Vector3(sampleDistance, heightRight - heightCenter, 0f);
			Vector3 tangentZ = new Vector3(0f, heightUp - heightCenter, sampleDistance);

			return tangentX.Cross(tangentZ).Normalized();
		}

		private static Vector3 GetTerrainVertex(Vector3 chunkOrigin, float localX, float localZ)
		{
			float worldX = chunkOrigin.X + localX;
			float worldZ = chunkOrigin.Z + localZ;
			float height = GetTerrainHeight(worldX, worldZ);
			
			return new Vector3(localX, height, localZ); // Local coordinates for chunk
		}

		private static Vector3 CalculateFlatNormal(Vector3 v0, Vector3 v1, Vector3 v2)
		{
			Vector3 edge1 = v1 - v0;
			Vector3 edge2 = v2 - v0;
			return edge1.Cross(edge2).Normalized();
		}

		private static void AddFlatTriangle(SurfaceTool st, Vector3 v0, Vector3 v1, Vector3 v2, 
										  Vector3 normal, Color c0, Color c1, Color c2)
		{
			// All vertices share the same normal for flat shading
			st.SetNormal(normal);
			st.SetColor(c0);
			st.SetUV(new Vector2(v0.X / 256f, v0.Z / 256f)); // UV for potential texture sampling
			st.AddVertex(v0);

			st.SetNormal(normal);
			st.SetColor(c1);
			st.SetUV(new Vector2(v1.X / 256f, v1.Z / 256f));
			st.AddVertex(v1);

			st.SetNormal(normal);
			st.SetColor(c2);
			st.SetUV(new Vector2(v2.X / 256f, v2.Z / 256f));
			st.AddVertex(v2);
		}

		/// <summary>
		/// Generates authentic South African terrain colors based on height and moisture.
		/// Warm earth tones, red soil, dry grass characteristic of bushveld.
		/// </summary>
		private static Color GetSouthAfricanTerrainColor(float worldX, float worldZ, float height)
		{
			float moisture = (_moistureNoise.GetNoise2D(worldX, worldZ) + 1f) * 0.5f;
			float heightNorm = Mathf.Clamp(height / HEIGHT_SCALE, 0f, 1f);

			// South African color palette
			Color redSoil = new Color(0.65f, 0.35f, 0.25f);      // Iron-rich red earth
			Color dryGrass = new Color(0.76f, 0.70f, 0.50f);     // Golden dry grass
			Color greenGrass = new Color(0.55f, 0.65f, 0.40f);   // Wet season grass
			Color rockGray = new Color(0.50f, 0.45f, 0.40f);     // Rocky outcrops

			Color baseColor;
			
			if (heightNorm < 0.3f)
			{
				// Lower areas - soil and grass mix
				baseColor = moisture > 0.6f ? 
					greenGrass.Lerp(redSoil, 0.3f) : 
					redSoil.Lerp(dryGrass, 0.6f);
			}
			else if (heightNorm < 0.7f)
			{
				// Mid elevation - mostly grassland
				baseColor = moisture > 0.5f ? greenGrass : dryGrass;
			}
			else
			{
				// Higher elevation - rockier terrain
				baseColor = rockGray.Lerp(dryGrass, moisture * 0.5f);
			}

			// Add subtle random variation
			uint hash = (uint)(worldX * 73856093) ^ (uint)(worldZ * 19349663);
			float variation = ((hash % 100) / 100f - 0.5f) * 0.1f;
			
			baseColor.R = Mathf.Clamp(baseColor.R + variation, 0f, 1f);
			baseColor.G = Mathf.Clamp(baseColor.G + variation, 0f, 1f);
			baseColor.B = Mathf.Clamp(baseColor.B + variation, 0f, 1f);

			// TODO: When shader is implemented, encode height/moisture in vertex color channels:
			// R = height factor, G = moisture, B = grass coverage, A = rock factor

			return baseColor;
		}
	}
}
