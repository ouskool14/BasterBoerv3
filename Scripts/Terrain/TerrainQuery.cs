using Godot;
using WorldStreaming.Terrain;

namespace LandManagementSim.Terrain
{
	/// <summary>
	/// Enhanced terrain query system that consumes TerrainSystem's rich terrain data.
	/// Provides efficient height, slope, moisture, and gameplay-relevant terrain queries.
	/// 
	/// This replaces the old static heightmap-based approach with direct consumption
	/// of TerrainSystem's cached terrain fields.
	/// </summary>
	public static class TerrainQuery
	{
		// ── Configurable thresholds ─────────────────────────────────────────
		public static float MaxSlopeDegrees { get; set; } = 45f;
		public static float MaxDropHeight { get; set; } = 2.0f;
		public static float BuildableMaxSlope { get; set; } = 15f;
		
		// ── Initialization state ────────────────────────────────────────────
		private static bool _initialized;
		private static TerrainSystem _terrainSystem;
		
		/// <summary>
		/// Initializes the terrain query system.
		/// </summary>
		public static void Initialize()
		{
			_terrainSystem = TerrainSystem.Instance;
			
			if (_terrainSystem == null)
			{
				GD.PushWarning("[TerrainQuery] TerrainSystem not found - queries will use fallback");
			}
			else
			{
				GD.Print("[TerrainQuery] Initialized with TerrainSystem");
			}
			
			_initialized = true;
		}
		
		// ── Height Queries ──────────────────────────────────────────────────
		
		/// <summary>
		/// Gets terrain height at world position.
		/// Uses TerrainSystem for accurate, layered terrain height.
		/// </summary>
		public static float GetHeight(Vector3 worldPos)
		{
			return GetHeight(worldPos.X, worldPos.Z);
		}
		
		/// <summary>
		/// Gets terrain height at world (x, z).
		/// </summary>
		public static float GetHeight(float worldX, float worldZ)
		{
			if (!_initialized)
				Initialize();
			
			if (_terrainSystem != null)
			{
				return _terrainSystem.GetTerrainHeight(worldX, worldZ);
			}
			
			// Fallback - should not happen in normal operation
			return 0f;
		}
		
		/// <summary>
		/// Gets terrain height with bilinear interpolation for smoother results.
		/// </summary>
		public static float GetHeightSmooth(float worldX, float worldZ)
		{
			// TerrainSystem already does bilinear interpolation
			return GetHeight(worldX, worldZ);
		}
		
		// ── Slope Queries ───────────────────────────────────────────────────
		
		/// <summary>
		/// Gets terrain slope in degrees at world position.
		/// </summary>
		public static float GetSlope(Vector3 worldPos)
		{
			return GetSlope(worldPos.X, worldPos.Z);
		}
		
		/// <summary>
		/// Gets terrain slope in degrees at world (x, z).
		/// </summary>
		public static float GetSlope(float worldX, float worldZ)
		{
			if (!_initialized)
				Initialize();
			
			if (_terrainSystem != null)
			{
				var sample = _terrainSystem.GetTerrainSample(worldX, worldZ);
				return sample.Slope;
			}
			
			// Compute from height samples
			float hC = GetHeight(worldX, worldZ);
			float hR = GetHeight(worldX + 2f, worldZ);
			float hU = GetHeight(worldX, worldZ + 2f);
			
			float dX = hR - hC;
			float dZ = hU - hC;
			
			return Mathf.Atan(Mathf.Sqrt(dX * dX + dZ * dZ) / 2f) * 57.29578f;
		}
		
		/// <summary>
		/// Gets terrain normal at world position.
		/// </summary>
		public static Vector3 GetNormal(Vector3 worldPos)
		{
			return GetNormal(worldPos.X, worldPos.Z);
		}
		
		/// <summary>
		/// Gets terrain normal at world (x, z).
		/// </summary>
		public static Vector3 GetNormal(float worldX, float worldZ)
		{
			if (!_initialized)
				Initialize();
			
			if (_terrainSystem != null)
			{
				return _terrainSystem.GetTerrainNormal(worldX, worldZ);
			}
			
			// Compute from height samples
			float hC = GetHeight(worldX, worldZ);
			float hR = GetHeight(worldX + 2f, worldZ);
			float hU = GetHeight(worldX, worldZ + 2f);
			
			Vector3 tangentX = new Vector3(2f, hR - hC, 0f);
			Vector3 tangentZ = new Vector3(0f, hU - hC, 2f);
			
			return tangentX.Cross(tangentZ).Normalized();
		}
		
		// ── Rich Terrain Queries ────────────────────────────────────────────
		
		/// <summary>
		/// Gets a complete terrain sample at world position.
		/// Includes height, slope, wetness, rockiness, road influence, and more.
		/// </summary>
		public static TerrainSample GetSample(Vector3 worldPos)
		{
			return GetSample(worldPos.X, worldPos.Z);
		}
		
		/// <summary>
		/// Gets a complete terrain sample at world (x, z).
		/// </summary>
		public static TerrainSample GetSample(float worldX, float worldZ)
		{
			if (!_initialized)
				Initialize();
			
			if (_terrainSystem != null)
			{
				return _terrainSystem.GetTerrainSample(worldX, worldZ);
			}
			
			// Fallback basic sample
			return new TerrainSample
			{
				Height = GetHeight(worldX, worldZ),
				Slope = GetSlope(worldX, worldZ),
				Wetness = 0.5f,
				Rockiness = 0f,
				RoadInfluence = 0f
			};
		}
		
		/// <summary>
		/// Gets terrain wetness/moisture at world position.
		/// </summary>
		public static float GetWetness(Vector3 worldPos)
		{
			return GetSample(worldPos).Wetness;
		}
		
		/// <summary>
		/// Gets terrain rockiness at world position.
		/// </summary>
		public static float GetRockiness(Vector3 worldPos)
		{
			return GetSample(worldPos).Rockiness;
		}
		
		/// <summary>
		/// Gets road influence at world position (0-1).
		/// </summary>
		public static float GetRoadInfluence(Vector3 worldPos)
		{
			return GetSample(worldPos).RoadInfluence;
		}
		
		/// <summary>
		/// Gets soil/biome type at world position.
		/// </summary>
		public static float GetSoilType(Vector3 worldPos)
		{
			return GetSample(worldPos).SoilType;
		}
		
		/// <summary>
		/// Gets local roughness at world position.
		/// </summary>
		public static float GetRoughness(Vector3 worldPos, float sampleRadius = 5f)
		{
			float centerH = GetHeight(worldX(worldPos), worldZ(worldPos));
			float hN = GetHeight(worldX(worldPos), worldZ(worldPos) - sampleRadius);
			float hS = GetHeight(worldX(worldPos), worldZ(worldPos) + sampleRadius);
			float hE = GetHeight(worldX(worldPos) + sampleRadius, worldZ(worldPos));
			float hW = GetHeight(worldX(worldPos) - sampleRadius, worldZ(worldPos));
			
			float variance = (Mathf.Abs(hN - centerH) + Mathf.Abs(hS - centerH) + 
							Mathf.Abs(hE - centerH) + Mathf.Abs(hW - centerH)) / 4f;
			
			return Mathf.Clamp(variance / sampleRadius, 0f, 1f);
		}
		
		// ── Walkability Queries ─────────────────────────────────────────────
		
		/// <summary>
		/// True if the position is walkable (not blocked, slope within threshold).
		/// </summary>
		public static bool IsWalkable(Vector3 worldPos)
		{
			return IsWalkable(worldPos.X, worldPos.Z);
		}
		
		/// <summary>
		/// True if (x, z) is walkable.
		/// </summary>
		public static bool IsWalkable(float worldX, float worldZ)
		{
			var sample = GetSample(worldX, worldZ);
			return sample.IsWalkable(MaxSlopeDegrees);
		}
		
		/// <summary>
		/// Returns true if the position is suitable for building.
		/// </summary>
		public static bool IsBuildable(Vector3 worldPos)
		{
			return IsBuildable(worldPos.X, worldPos.Z);
		}
		
		/// <summary>
		/// Returns true if (x, z) is suitable for building.
		/// </summary>
		public static bool IsBuildable(float worldX, float worldZ)
		{
			var sample = GetSample(worldX, worldZ);
			return sample.IsBuildable(BuildableMaxSlope);
		}
		
		/// <summary>
		/// Returns a movement cost factor (1.0 = normal, higher = harder).
		/// </summary>
		public static float GetMovementCost(Vector3 worldPos)
		{
			return GetSample(worldPos).GetMovementCost();
		}
		
		// ── Movement Validation ─────────────────────────────────────────────
		
		/// <summary>
		/// Returns true if movement from one position to another is valid.
		/// Checks walkability at destination, slope between positions, and drop height.
		/// </summary>
		public static bool CanMove(Vector3 from, Vector3 to)
		{
			// Check walkability at destination
			if (!IsWalkable(to))
				return false;
			
			float fromH = GetHeight(from);
			float toH = GetHeight(to);
			float deltaH = toH - fromH;
			
			// Reject large downward drops
			if (deltaH < -MaxDropHeight)
				return false;
			
			// Reject steep slopes
			float dx = to.X - from.X;
			float dz = to.Z - from.Z;
			float distSq = dx * dx + dz * dz;
			if (distSq > 0.001f)
			{
				float dist = Mathf.Sqrt(distSq);
				float slopeDeg = Mathf.Atan2(Mathf.Abs(deltaH), dist) * 57.29578f;
				if (slopeDeg > MaxSlopeDegrees)
					return false;
			}
			
			return true;
		}
		
		/// <summary>
		/// Tries to validate a move, returning an adjusted position if blocked.
		/// </summary>
		public static bool TryValidateMove(Vector3 currentPos, Vector3 desiredPos, out Vector3 validatedPos)
		{
			validatedPos = desiredPos;
			
			if (CanMove(currentPos, desiredPos))
				return true;
			
			// Try perpendicular offsets
			Vector3 direction = (desiredPos - currentPos).Normalized();
			Vector3 perpendicular = new Vector3(-direction.Z, 0f, direction.X);
			
			for (float offset = 2f; offset <= 8f; offset += 2f)
			{
				// Try left offset
				Vector3 leftPos = desiredPos + perpendicular * offset;
				leftPos.Y = GetHeight(leftPos);
				if (CanMove(currentPos, leftPos))
				{
					validatedPos = leftPos;
					return true;
				}
				
				// Try right offset
				Vector3 rightPos = desiredPos - perpendicular * offset;
				rightPos.Y = GetHeight(rightPos);
				if (CanMove(currentPos, rightPos))
				{
					validatedPos = rightPos;
					return true;
				}
			}
			
			return false;
		}
		
		// ── Position Correction ─────────────────────────────────────────────
		
		/// <summary>
		/// Snaps a position to the terrain surface.
		/// </summary>
		public static Vector3 SnapToTerrain(Vector3 position)
		{
			float height = GetHeight(position);
			return new Vector3(position.X, height, position.Z);
		}
		
		/// <summary>
		/// Snaps a position to the terrain surface with normal alignment.
		/// </summary>
		public static Vector3 SnapToTerrain(Vector3 position, out Vector3 normal)
		{
			normal = GetNormal(position);
			float height = GetHeight(position);
			return new Vector3(position.X, height, position.Z);
		}
		
		// ── Waterhole Queries ───────────────────────────────────────────────
		
		/// <summary>
		/// Gets the nearest waterhole to a position.
		/// </summary>
		public static WaterholeInfo GetNearestWaterhole(Vector3 position, out float distance)
		{
			if (_terrainSystem != null)
			{
				return _terrainSystem.GetNearestWaterhole(position, out distance);
			}
			
			distance = float.MaxValue;
			return default;
		}
		
		/// <summary>
		/// Gets all waterholes within range of a position.
		/// </summary>
		public static WaterholeInfo[] GetWaterholesInRange(Vector3 position, float maxDistance)
		{
			if (_terrainSystem != null)
			{
				return _terrainSystem.GetWaterholesInRange(position, maxDistance);
			}
			
			return System.Array.Empty<WaterholeInfo>();
		}
		
		// ── Batch Queries ───────────────────────────────────────────────────
		
		/// <summary>
		/// Gets heights for multiple positions efficiently.
		/// </summary>
		public static float[] GetHeights(Vector2[] positions)
		{
			float[] heights = new float[positions.Length];
			for (int i = 0; i < positions.Length; i++)
			{
				heights[i] = GetHeight(positions[i].X, positions[i].Y);
			}
			return heights;
		}
		
		/// <summary>
		/// Gets terrain samples for multiple positions efficiently.
		/// </summary>
		public static TerrainSample[] GetSamples(Vector3[] positions)
		{
			TerrainSample[] samples = new TerrainSample[positions.Length];
			for (int i = 0; i < positions.Length; i++)
			{
				samples[i] = GetSample(positions[i]);
			}
			return samples;
		}
		
		// ── Utility ─────────────────────────────────────────────────────────
		
		private static float worldX(Vector3 v) => v.X;
		private static float worldZ(Vector3 v) => v.Z;
		
		/// <summary>
		/// Gets debug information about the query system.
		/// </summary>
		public static string GetDebugInfo()
		{
			return $"[TerrainQuery]\n" +
				   $"  Initialized: {_initialized}\n" +
				   $"  TerrainSystem: {(_terrainSystem != null ? "connected" : "not connected")}\n" +
				   $"  MaxSlope: {MaxSlopeDegrees}\n" +
				   $"  MaxDrop: {MaxDropHeight}\n" +
				   $"  BuildableSlope: {BuildableMaxSlope}";
		}
	}
}
