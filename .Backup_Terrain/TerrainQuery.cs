using Godot;
using WorldStreaming;

namespace LandManagementSim.Terrain
{
	/// <summary>
	/// Heightmap-based terrain query system for efficient animal movement.
	/// Pre-samples TerrainGenerator noise into a grid, then uses bilinear
	/// interpolation for smooth height lookups. Zero allocations at runtime.
	///
	/// Call Initialize() once at startup before any animal queries.
	/// </summary>
	public static class TerrainQuery
	{
		// ── Heightmap data ───────────────────────────────────────────────
		private static float[] _heights;
		private static byte[] _walkability;
		private static float[] _slopes;

		// ── Grid parameters ──────────────────────────────────────────────
		private static float _originX;
		private static float _originZ;
		private static float _cellSize;
		private static int _width;   // columns (X)
		private static int _depth;   // rows (Z)
		private static float _invCellSize;

		private static bool _initialized;

		// ── Configurable thresholds ──────────────────────────────────────
		public static float MaxSlopeDegrees { get; set; } = 45f;
		public static float MaxDropHeight { get; set; } = 2.0f;

		/// <summary>
		/// Samples TerrainGenerator into a heightmap grid covering the world.
		/// </summary>
		/// <param name="worldSizeX">Full world width in metres.</param>
		/// <param name="worldSizeZ">Full world depth in metres.</param>
		/// <param name="cellSize">Metres per grid cell. 4 = match terrain mesh resolution.</param>
		public static void Initialize(float worldSizeX, float worldSizeZ, float cellSize = 4f)
		{
			_cellSize = cellSize;
			_invCellSize = 1f / cellSize;
			_width = Mathf.CeilToInt(worldSizeX / cellSize) + 1;
			_depth = Mathf.CeilToInt(worldSizeZ / cellSize) + 1;
			// World is centered on (0,0) so the grid spans from -worldSize/2 to +worldSize/2.
			// This matches the chunk grid layout where chunk (0,0) has its southwest corner
			// at (0,0) and the full map extends symmetrically around the origin.
			_originX = -worldSizeX * 0.5f;
			_originZ = -worldSizeZ * 0.5f;

			int total = _width * _depth;
			_heights = new float[total];
			_slopes = new float[total];
			_walkability = new byte[total];

			// Sample heights
			for (int z = 0; z < _depth; z++)
			{
				float wz = _originZ + z * cellSize;
				for (int x = 0; x < _width; x++)
				{
					float wx = _originX + x * cellSize;
					_heights[z * _width + x] = LandManagementSim.Terrain.TerrainQuery.GetHeight(wx, wz);
				}
			}

			// Compute slopes
			ComputeSlopes();

			// Build walkability mask from slope threshold
			RebuildWalkability();

			_initialized = true;
			GD.Print($"[TerrainQuery] Initialized {_width}x{_depth} grid ({cellSize}m cells, {total} samples)");
		}

		// ── Public queries ───────────────────────────────────────────────

		/// <summary>
		/// Terrain height at world position via bilinear interpolation.
		/// </summary>
		public static float GetHeight(Vector3 worldPos)
		{
			return GetHeight(worldPos.X, worldPos.Z);
		}

		/// <summary>
		/// Terrain height at world (x, z) via bilinear interpolation.
		/// </summary>
		public static float GetHeight(float worldX, float worldZ)
		{
			if (!_initialized)
				return LandManagementSim.Terrain.TerrainQuery.GetHeight(worldX, worldZ);

			float gx = (worldX - _originX) * _invCellSize;
			float gz = (worldZ - _originZ) * _invCellSize;

			int x0 = Mathf.FloorToInt(gx);
			int z0 = Mathf.FloorToInt(gz);

			// Clamp to valid range
			if (x0 < 0) x0 = 0;
			if (z0 < 0) z0 = 0;
			int x1 = x0 + 1;
			int z1 = z0 + 1;
			if (x1 >= _width) x1 = _width - 1;
			if (z1 >= _depth) z1 = _depth - 1;

			float fx = gx - x0;
			float fz = gz - z0;

			float h00 = _heights[z0 * _width + x0];
			float h10 = _heights[z0 * _width + x1];
			float h01 = _heights[z1 * _width + x0];
			float h11 = _heights[z1 * _width + x1];

			float hz0 = h00 + (h10 - h00) * fx;
			float hz1 = h01 + (h11 - h01) * fx;

			return hz0 + (hz1 - hz0) * fz;
		}

		/// <summary>
		/// Terrain slope in degrees at world position (bilinear).
		/// </summary>
		public static float GetSlope(Vector3 worldPos)
		{
			if (!_initialized) return 0f;

			float gx = (worldX(worldPos) - _originX) * _invCellSize;
			float gz = (worldZ(worldPos) - _originZ) * _invCellSize;
			return SampleGrid(_slopes, gx, gz);
		}

		/// <summary>
		/// Terrain slope in degrees at world (x, z).
		/// </summary>
		public static float GetSlope(float worldX, float worldZ)
		{
			if (!_initialized) return 0f;

			float gx = (worldX - _originX) * _invCellSize;
			float gz = (worldZ - _originZ) * _invCellSize;
			return SampleGrid(_slopes, gx, gz);
		}

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
			if (!_initialized) return true;

			int gx, gz;
			if (!WorldToGrid(worldX, worldZ, out gx, out gz))
				return false;

			return _walkability[gz * _width + gx] == 0;
		}

		/// <summary>
		/// Marks a world-space rectangle as blocked (1) or clear (0).
		/// Use for fences, buildings, water, etc.
		/// </summary>
		public static void SetBlocked(float minX, float minZ, float maxX, float maxZ, bool blocked)
		{
			if (!_initialized) return;

			int gx0, gz0, gx1, gz1;
			WorldToGridClamped(minX, minZ, out gx0, out gz0);
			WorldToGridClamped(maxX, maxZ, out gx1, out gz1);

			byte val = blocked ? (byte)1 : (byte)0;

			for (int z = gz0; z <= gz1; z++)
			{
				for (int x = gx0; x <= gx1; x++)
				{
					// Don't override slope-based blocking unless explicitly clearing
					if (blocked || _slopes[z * _width + x] <= MaxSlopeDegrees)
					{
						_walkability[z * _width + x] = val;
					}
				}
			}
		}

		/// <summary>
		/// Rebuilds the walkability mask from current slope data and MaxSlopeDegrees.
		/// Call after changing MaxSlopeDegrees.
		/// </summary>
		public static void RebuildWalkability()
		{
			if (_slopes == null) return;

			for (int i = 0; i < _slopes.Length; i++)
			{
				// Preserve manually-set blocks (value 2), otherwise derive from slope
				if (_walkability[i] != 2)
				{
					_walkability[i] = _slopes[i] > MaxSlopeDegrees ? (byte)1 : (byte)0;
				}
			}
		}

		/// <summary>
		/// Returns true if movement from one position to another is valid.
		/// Checks walkability at destination, slope between positions, and drop height.
		/// </summary>
		public static bool CanMove(Vector3 from, Vector3 to)
		{
			if (!_initialized) return true;

			// Check walkability at destination
			if (!IsWalkable(to.X, to.Z))
				return false;

			float fromH = GetHeight(from.X, from.Z);
			float toH = GetHeight(to.X, to.Z);
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

		// ── Internal helpers ─────────────────────────────────────────────

		private static void ComputeSlopes()
		{
			for (int z = 0; z < _depth; z++)
			{
				for (int x = 0; x < _width; x++)
				{
					float h = _heights[z * _width + x];

					// Sample neighbours (clamp at edges)
					float hL = _heights[z * _width + Mathf.Max(0, x - 1)];
					float hR = _heights[z * _width + Mathf.Min(_width - 1, x + 1)];
					float hD = _heights[Mathf.Max(0, z - 1) * _width + x];
					float hU = _heights[Mathf.Min(_depth - 1, z + 1) * _width + x];

					float dX = (hR - hL) * 0.5f;
					float dZ = (hU - hD) * 0.5f;
					float run = _cellSize;

					_slopes[z * _width + x] = Mathf.Atan(Mathf.Sqrt(dX * dX + dZ * dZ) / run)
											   * 57.29578f;
				}
			}
		}

		/// <summary>
		/// Bilinear sample of a float grid.
		/// </summary>
		private static float SampleGrid(float[] grid, float gx, float gz)
		{
			int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, _width - 1);
			int z0 = Mathf.Clamp(Mathf.FloorToInt(gz), 0, _depth - 1);
			int x1 = Mathf.Min(x0 + 1, _width - 1);
			int z1 = Mathf.Min(z0 + 1, _depth - 1);

			float fx = gx - x0;
			float fz = gz - z0;

			float v00 = grid[z0 * _width + x0];
			float v10 = grid[z0 * _width + x1];
			float v01 = grid[z1 * _width + x0];
			float v11 = grid[z1 * _width + x1];

			return v00 + (v10 - v00) * fx + (v01 - v00) * fz + (v00 - v10 - v01 + v11) * fx * fz;
		}

		private static bool WorldToGrid(float wx, float wz, out int gx, out int gz)
		{
			gx = Mathf.FloorToInt((wx - _originX) * _invCellSize);
			gz = Mathf.FloorToInt((wz - _originZ) * _invCellSize);
			return gx >= 0 && gx < _width && gz >= 0 && gz < _depth;
		}

		private static void WorldToGridClamped(float wx, float wz, out int gx, out int gz)
		{
			gx = Mathf.Clamp(Mathf.FloorToInt((wx - _originX) * _invCellSize), 0, _width - 1);
			gz = Mathf.Clamp(Mathf.FloorToInt((wz - _originZ) * _invCellSize), 0, _depth - 1);
		}

		// Shorthand to avoid property accessors on Vector3 in tight loops
		private static float worldX(Vector3 v) => v.X;
		private static float worldZ(Vector3 v) => v.Z;
	}
}
