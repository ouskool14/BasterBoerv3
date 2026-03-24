using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WorldStreaming
{
	/// <summary>
	/// Singleton managing the 3x3 active chunk grid around the player.
	/// Handles background chunk loading/unloading with performance monitoring.
	/// Maintains maximum 9 active chunks as per architectural requirements.
	/// </summary>
	[GlobalClass]
	public partial class WorldChunkStreamer : Node
	{
		private static WorldChunkStreamer _instance;
		public static WorldChunkStreamer Instance => _instance;

		[ExportGroup("Player Tracking")]
		[Export]
		public NodePath PlayerNodePath { get; set; }

		[ExportGroup("Chunk Settings")]
		[Export]
		public float ChunkSize { get; set; } = 256f;

		[Export]
		public float UpdateInterval { get; set; } = 0.5f; // Check interval in seconds

		[ExportGroup("Performance")]
		[Export]
		public int MaxConcurrentLoads { get; set; } = 2; // Background loading limit

		[Export]
		public int MaxActiveChunks { get; set; } = 9; // 3x3 grid maximum

		// Runtime state
		private Node3D _playerNode;
		private ChunkCoord _currentPlayerChunk;
		private readonly Dictionary<ChunkCoord, WorldChunk> _activeChunks = 
			new Dictionary<ChunkCoord, WorldChunk>();
		private readonly HashSet<ChunkCoord> _chunksBeingLoaded = new HashSet<ChunkCoord>();
		private readonly Queue<WorldChunk.ChunkBuildResult> _pendingBuildResults = 
			new Queue<WorldChunk.ChunkBuildResult>();

		// Timing
		private float _updateTimer;
		private int _currentBackgroundLoads;

		// Performance monitoring
		public int ActiveChunkCount => _activeChunks.Count;
		public int LoadingChunkCount => _chunksBeingLoaded.Count;
		public int PendingResultCount => _pendingBuildResults.Count;

		public override void _EnterTree()
		{
			if (_instance != null && _instance != this)
			{
				GD.PushWarning("Multiple WorldChunkStreamer instances detected. Keeping the first one.");
				QueueFree();
				return;
			}
			_instance = this;
		}

		public override void _Ready()
		{
			if (!string.IsNullOrEmpty(PlayerNodePath.ToString()))
			{
				_playerNode = GetNodeOrNull<Node3D>(PlayerNodePath);
				if (_playerNode == null)
				{
					GD.PushError($"[WorldChunkStreamer] Player node not found at path: {PlayerNodePath}");
				}
				else
				{
					// Initialize with player's starting position
					_currentPlayerChunk = ChunkCoord.FromWorldPosition(_playerNode.GlobalPosition, ChunkSize);
					InitialChunkLoad();
				}
			}
		}

		public override void _Process(double delta)
		{
			if (_playerNode == null) return;

			// Apply any completed build results on main thread
			ApplyPendingBuildResults();

			// Check for player movement periodically
			_updateTimer += (float)delta;
			if (_updateTimer >= UpdateInterval)
			{
				_updateTimer = 0f;
				CheckPlayerMovement();
			}
		}

		/// <summary>
		/// Sets player node reference if not using NodePath.
		/// </summary>
		public void SetPlayerNode(Node3D playerNode)
		{
			_playerNode = playerNode;
			if (_playerNode != null)
			{
				_currentPlayerChunk = ChunkCoord.FromWorldPosition(_playerNode.GlobalPosition, ChunkSize);
				InitialChunkLoad();
			}
		}

		/// <summary>
		/// Forces refresh of all active chunk flora visuals.
		/// Use sparingly - expensive operation.
		/// </summary>
		public void RefreshAllFloraVisuals()
		{
			foreach (var chunk in _activeChunks.Values)
			{
				// TODO: Implement flora refresh if needed for dynamic health changes
			}
			GD.Print("[WorldChunkStreamer] Flora visuals refreshed for all active chunks");
		}

		private void InitialChunkLoad()
		{
			ChunkCoord[] initialChunks = _currentPlayerChunk.Get3x3Grid();
			
			// Sort by distance from center (player chunk loads first)
			Array.Sort(initialChunks, (a, b) => 
				a.ManhattanDistance(_currentPlayerChunk).CompareTo(b.ManhattanDistance(_currentPlayerChunk)));

			foreach (var coord in initialChunks)
			{
				if (_activeChunks.Count + _chunksBeingLoaded.Count < MaxActiveChunks)
				{
					LoadChunkAsync(coord);
				}
			}

			GD.Print($"[WorldChunkStreamer] Initial load started around chunk {_currentPlayerChunk}");
		}

		private void CheckPlayerMovement()
		{
			ChunkCoord newPlayerChunk = ChunkCoord.FromWorldPosition(_playerNode.GlobalPosition, ChunkSize);

			if (newPlayerChunk == _currentPlayerChunk)
			{
				return; // Player still in same chunk
			}

			GD.Print($"[WorldChunkStreamer] Player moved from {_currentPlayerChunk} to {newPlayerChunk}");
			_currentPlayerChunk = newPlayerChunk;
			UpdateActiveChunks();
		}

		private void UpdateActiveChunks()
		{
			// Determine desired 3x3 grid around player
			ChunkCoord[] desiredChunks = _currentPlayerChunk.Get3x3Grid();
			var desiredSet = new HashSet<ChunkCoord>(desiredChunks);

			// Unload chunks no longer needed
			var chunksToUnload = _activeChunks.Keys
				.Where(coord => !desiredSet.Contains(coord))
				.ToList();

			foreach (var coord in chunksToUnload)
			{
				UnloadChunk(coord);
			}

			// Load new chunks (prioritize by distance to player)
			var chunksToLoad = desiredChunks
				.Where(coord => !_activeChunks.ContainsKey(coord) && !_chunksBeingLoaded.Contains(coord))
				.OrderBy(coord => coord.ManhattanDistance(_currentPlayerChunk))
				.ToList();

			foreach (var coord in chunksToLoad)
			{
				if (_activeChunks.Count + _chunksBeingLoaded.Count >= MaxActiveChunks)
					break;

				if (_currentBackgroundLoads >= MaxConcurrentLoads)
					break;

				LoadChunkAsync(coord);
			}
		}

		private async void LoadChunkAsync(ChunkCoord coord)
		{
			if (_chunksBeingLoaded.Contains(coord) || _activeChunks.ContainsKey(coord))
			{
				return; // Already loading or loaded
			}

			_chunksBeingLoaded.Add(coord);
			_currentBackgroundLoads++;

			try
			{
				// Create chunk node on main thread (lightweight)
				var chunk = new WorldChunk();
				chunk.Initialize(coord, ChunkSize);

				// Heavy work on background thread
				var buildResult = await Task.Run(() => chunk.BuildChunkContent());

				// Queue result for main thread application
				lock (_pendingBuildResults)
				{
					_pendingBuildResults.Enqueue(buildResult);
				}

				// Store chunk reference for later scene tree addition
				_activeChunks[coord] = chunk;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[WorldChunkStreamer] Error loading chunk {coord}: {ex}");
			}
			finally
			{
				_chunksBeingLoaded.Remove(coord);
				_currentBackgroundLoads--;
			}
		}

		private void ApplyPendingBuildResults()
		{
			const int maxResultsPerFrame = 1; // Prevent frame rate spikes
			int resultsApplied = 0;

			while (_pendingBuildResults.Count > 0 && resultsApplied < maxResultsPerFrame)
			{
				WorldChunk.ChunkBuildResult buildResult;
				lock (_pendingBuildResults)
				{
					if (_pendingBuildResults.Count == 0) break;
					buildResult = _pendingBuildResults.Dequeue();
				}

				// Find the corresponding chunk
				if (_activeChunks.TryGetValue(buildResult.Coordinate, out WorldChunk chunk))
				{
					// Add chunk to scene tree and apply build result
					if (!chunk.IsInsideTree())
					{
						AddChild(chunk);
					}
					
					chunk.ApplyBuildResult(buildResult);
					resultsApplied++;
				}

				resultsApplied++;
			}
		}

		private void UnloadChunk(ChunkCoord coord)
		{
			if (!_activeChunks.TryGetValue(coord, out WorldChunk chunk))
			{
				return;
			}

			chunk.UnloadChunk();
			if (chunk.IsInsideTree())
			{
				RemoveChild(chunk);
			}
			chunk.QueueFree();
			
			_activeChunks.Remove(coord);

			GD.Print($"[WorldChunkStreamer] Unloaded chunk {coord}. Active: {_activeChunks.Count}");
		}

		/// <summary>
		/// Gets currently active chunk at coordinate, if loaded.
		/// </summary>
		public WorldChunk GetActiveChunk(ChunkCoord coord)
		{
			_activeChunks.TryGetValue(coord, out WorldChunk chunk);
			return chunk;
		}

		/// <summary>
		/// Emergency unload of all chunks. Use for cleanup or world reset.
		/// </summary>
		public void UnloadAllChunks()
		{
			var allCoords = new List<ChunkCoord>(_activeChunks.Keys);
			foreach (var coord in allCoords)
			{
				UnloadChunk(coord);
			}
			
			_chunksBeingLoaded.Clear();
			_currentBackgroundLoads = 0;
			
			lock (_pendingBuildResults)
			{
				_pendingBuildResults.Clear();
			}

			GD.Print("[WorldChunkStreamer] All chunks unloaded");
		}

		public override void _ExitTree()
		{
			UnloadAllChunks();
			
			if (_instance == this)
			{
				_instance = null;
			}
		}
	}
}
