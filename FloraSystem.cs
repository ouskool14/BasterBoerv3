using Godot;
using System.Collections.Generic;
using System.Threading;
using WorldStreaming;

namespace WorldStreaming.Flora
{
	/// <summary>
	/// Global singleton managing all flora simulation data for the entire 10,000 hectare world.
	/// Thread-safe for background chunk loading. Pure C# data - no Godot nodes.
	/// </summary>
	[GlobalClass]
	public partial class FloraSystem : Node
	{
		private static FloraSystem _instance;
		public static FloraSystem Instance => _instance;

		// Spatial indexing: chunk coordinate -> flora in that chunk
		private readonly Dictionary<ChunkCoord, List<FloraEntry>> _floraByChunk = 
			new Dictionary<ChunkCoord, List<FloraEntry>>();

		// Thread safety for concurrent chunk loading
		private readonly ReaderWriterLockSlim _dataLock = new ReaderWriterLockSlim();

		[Export]
		public float ChunkSizeMeters { get; set; } = 256f;

		[Export]
		public int TotalFloraCount { get; private set; }

		public override void _EnterTree()
		{
			if (_instance != null && _instance != this)
			{
				GD.PushWarning("Multiple FloraSystem instances detected. Keeping the first one.");
				return;
			}
			_instance = this;
		}

		public override void _ExitTree()
		{
			if (_instance == this)
				_instance = null;
			
			_dataLock?.Dispose();
		}

		/// <summary>
		/// Gets all flora entries for a specific chunk. Thread-safe.
		/// If data doesn't exist, procedurally generates it (lazy loading).
		/// </summary>
		public List<FloraEntry> GetFloraForChunk(ChunkCoord coord)
		{
			_dataLock.EnterReadLock();
			try
			{
				if (_floraByChunk.TryGetValue(coord, out List<FloraEntry> existing))
				{
					return new List<FloraEntry>(existing); // Return copy for thread safety
				}
			}
			finally
			{
				_dataLock.ExitReadLock();
			}

			// Generate new data if it doesn't exist
			return GenerateFloraDataForChunk(coord);
		}

		/// <summary>
		/// Procedurally generates flora data for a chunk. Called on background threads.
		/// </summary>
		private List<FloraEntry> GenerateFloraDataForChunk(ChunkCoord coord)
		{
			var entries = new List<FloraEntry>();
			var rng = new RandomNumberGenerator();
			rng.Seed = (ulong)coord.GetHashCode(); // Deterministic per chunk

			Vector3 chunkOrigin = coord.GetWorldOrigin(ChunkSizeMeters);
			
			// South African bushveld density: 50-300 plants per 256m chunk depending on terrain
			int floraCount = rng.RandiRange(80, 250);

			for (int i = 0; i < floraCount; i++)
			{
				float localX = rng.Randf() * ChunkSizeMeters;
				float localZ = rng.Randf() * ChunkSizeMeters;
				Vector2 worldPos = new Vector2(chunkOrigin.X + localX, chunkOrigin.Z + localZ);

				// Biome-appropriate flora selection
				FloraType type = SelectFloraType(rng, worldPos);
				float health = rng.RandfRange(0.6f, 1f); // Most plants healthy
				float age = rng.RandfRange(1f, 15f); // Mixed ages

				entries.Add(new FloraEntry(worldPos, type, health, age));
			}

			// Cache the generated data
			_dataLock.EnterWriteLock();
			try
			{
				if (!_floraByChunk.ContainsKey(coord))
				{
					_floraByChunk[coord] = entries;
					TotalFloraCount += entries.Count;
				}
			}
			finally
			{
				_dataLock.ExitWriteLock();
			}

			return entries;
		}

		/// <summary>
		/// Selects appropriate flora type based on South African bushveld ecology.
		/// </summary>
		private FloraType SelectFloraType(RandomNumberGenerator rng, Vector2 worldPos)
		{
			// TODO: Integrate with moisture and elevation maps for realistic distribution
			
			float rand = rng.Randf();
			
			// Weighted distribution typical of South African bushveld
			return rand switch
			{
				< 0.15f => FloraType.AcaciaThorn,      // Dominant species
				< 0.25f => FloraType.MarulaMpopona,
				< 0.35f => FloraType.BuffaloThorn,
				< 0.45f => FloraType.Tamboti,
				< 0.55f => FloraType.KnobtornAcacia,
				< 0.65f => FloraType.MagicGuarana,     // Shrubs
				< 0.75f => FloraType.SicklebushDichrostachys,
				< 0.85f => FloraType.RedGrass,         // Grasses
				< 0.95f => FloraType.PanicGrass,
				< 0.98f => FloraType.InvasiveLantana,  // Small invasive population
				_ => FloraType.InvasiveBugweed
			};
		}

		/// <summary>
		/// Updates flora health in a region (for fire, disease, herbivore damage simulation).
		/// </summary>
		public void UpdateFloraHealth(Vector2 worldPosition, float healthDelta, float radius = 10f)
		{
			ChunkCoord centerChunk = ChunkCoord.FromWorldPosition(
				new Vector3(worldPosition.X, 0, worldPosition.Y), ChunkSizeMeters);

			_dataLock.EnterWriteLock();
			try
			{
				// Check center chunk and neighbors for affected flora
				ChunkCoord[] nearbyChunks = centerChunk.Get3x3Grid();
				
				foreach (var coord in nearbyChunks)
				{
					if (!_floraByChunk.TryGetValue(coord, out List<FloraEntry> entries))
						continue;

					for (int i = 0; i < entries.Count; i++)
					{
						FloraEntry entry = entries[i];
						float distance = entry.WorldPosition2D.DistanceTo(worldPosition);
						
						if (distance <= radius)
						{
							entry.Health = Mathf.Clamp(entry.Health + healthDelta, 0f, 1f);
							// Recalculate scale based on new health
							float ageScale = Mathf.Clamp(entry.Age / 10f, 0.3f, 1.2f);
							float healthScale = Mathf.Lerp(0.6f, 1f, entry.Health);
							entry.ScaleMultiplier = ageScale * healthScale;
							entries[i] = entry;
						}
					}
				}
			}
			finally
			{
				_dataLock.ExitWriteLock();
			}
		}
	}
}
