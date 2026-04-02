using Basterboer.Buildings;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WorldStreaming.Terrain;

namespace WorldStreaming
{
    /// <summary>
    /// Singleton managing the active chunk grid around the player.
    /// Handles background chunk loading/unloading with TerrainSystem integration.
    /// 
    /// Updated for TerrainSystem: Now uses TerrainSystem for all terrain generation,
    /// enabling rich terrain data, masks, and seasonal updates.
    /// </summary>
    [GlobalClass]
    public partial class WorldChunkStreamer : Node
    {
        /// <summary>
        /// Emitted when the center chunk's collision is in the scene tree.
        /// Player/Bakkie can safely spawn after this signal fires.
        /// </summary>
        [Signal]
        public delegate void InitialTerrainReadyEventHandler();
        
        /// <summary>
        /// Emitted when a chunk is fully loaded with all terrain data.
        /// </summary>
        public event System.Action<ChunkCoord> ChunkLoaded;
        
        /// <summary>
        /// Emitted when a chunk is unloaded.
        /// </summary>
        public event System.Action<ChunkCoord> ChunkUnloaded;

        private static WorldChunkStreamer _instance;
        public static WorldChunkStreamer Instance => _instance;

        /// <summary>
        /// True after the center chunk has been synchronously loaded and its
        /// collision is in the scene tree. Safe to spawn player when true.
        /// </summary>
        public bool IsInitialLoadComplete { get; private set; }

        [ExportGroup("Player Tracking")]
        [Export]
        public NodePath PlayerNodePath { get; set; }

        [ExportGroup("Chunk Settings")]
        [Export]
        public float ChunkSize { get; set; } = 256f;

        [Export]
        public float UpdateInterval { get; set; } = 0.5f; // Check interval in seconds
        
        [Export]
        public int GridRadius { get; set; } = 1; // 1 = 3x3 grid, 2 = 5x5 grid

        [ExportGroup("Performance")]
        [Export]
        public int MaxConcurrentLoads { get; set; } = 2; // Background loading limit

        [Export]
        public int MaxActiveChunks { get; set; } = 9; // 3x3 grid maximum
        
        [Export]
        public bool UseTerrainSystem { get; set; } = true; // Enable new terrain system

        // Runtime state
        private Node3D _playerNode;
        private BuildingRenderer _buildingRenderer;
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
        public float TotalChunkBuildTime { get; private set; }
        public int TotalChunksBuilt { get; private set; }

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
            // Initialize TerrainSystem if enabled
            if (UseTerrainSystem)
            {
                InitializeTerrainSystem();
            }
            
            // Cache BuildingRenderer reference for performance (no GetNode() in loops)
            _buildingRenderer = BuildingRenderer.Instance;
            if (_buildingRenderer == null)
            {
                GD.PrintErr("[WorldChunkStreamer] BuildingRenderer not found!");
            }

            if (PlayerNodePath != null && !PlayerNodePath.IsEmpty)
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
        
        /// <summary>
        /// Initializes the TerrainSystem with configuration.
        /// </summary>
        private void InitializeTerrainSystem()
        {
            var terrainSystem = TerrainSystem.Instance;
            if (terrainSystem == null)
            {
                // Create TerrainSystem node if it doesn't exist
                terrainSystem = new TerrainSystem();
                terrainSystem.Name = "TerrainSystem";
                
                // Try to load configuration
                var config = GD.Load<TerrainConfig>("res://resources/terrain_config.tres");
                if (config == null)
                {
                    config = TerrainConfig.CreateDefaultBushveldConfig();
                    GD.Print("[WorldChunkStreamer] Using default terrain configuration");
                }
                
                terrainSystem.Config = config;
                AddChild(terrainSystem);
                terrainSystem.Initialize();
            }
            
            // Initialize TerrainQuery
            LandManagementSim.Terrain.TerrainQuery.Initialize();
            
            GD.Print("[WorldChunkStreamer] TerrainSystem initialized");
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
        /// Gets terrain height at world coordinates via the unified TerrainQuery path.
        /// </summary>
        public float GetTerrainHeightAt(float worldX, float worldZ)
        {
            return LandManagementSim.Terrain.TerrainQuery.GetHeight(worldX, worldZ);
        }
        
        /// <summary>
        /// Gets a complete terrain sample at world position.
        /// </summary>
        public TerrainSample GetTerrainSample(Vector3 worldPos)
        {
            return LandManagementSim.Terrain.TerrainQuery.GetSample(worldPos);
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
        /// Updates seasonal state for all active chunks.
        /// Called by TimeSystem when season changes.
        /// </summary>
        public void UpdateSeasonalState(float wetness, float dryness, float greenBias)
        {
            // Update TerrainSystem global state
            TerrainSystem.Instance?.UpdateSeasonalState(wetness, dryness, greenBias);
            
            // Update each active chunk's material
            foreach (var chunk in _activeChunks.Values)
            {
                chunk.UpdateSeasonalState(wetness, dryness, greenBias);
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
        
        /// <summary>
        /// Gets the active chunk at the specified coordinate.
        /// </summary>
        public WorldChunk GetChunk(ChunkCoord coord)
        {
            _activeChunks.TryGetValue(coord, out var chunk);
            return chunk;
        }
        
        /// <summary>
        /// Gets terrain data for a chunk if loaded.
        /// </summary>
        public TerrainChunkData GetChunkTerrainData(ChunkCoord coord)
        {
            if (_activeChunks.TryGetValue(coord, out var chunk))
            {
                return chunk.TerrainData;
            }
            return null;
        }

        private bool IsChunkWithinMapBounds(ChunkCoord coord)
        {
            var gameState = GameState.Instance;
            if (gameState == null) return true; // Fail-safe: allow if no game state

            Vector3 origin = coord.GetWorldOrigin(ChunkSize);
            float halfX = gameState.MapSizeX / 2f;
            float halfZ = gameState.MapSizeZ / 2f;

            // Add a buffer of one chunk size to ensure terrain exists under the fence
            // and slightly beyond to prevent gaps at the boundary.
            const float boundaryBuffer = 10f; 

            bool overlapX = origin.X < halfX + boundaryBuffer && origin.X + ChunkSize > -halfX - boundaryBuffer;
            bool overlapZ = origin.Z < halfZ + boundaryBuffer && origin.Z + ChunkSize > -halfZ - boundaryBuffer;

            return overlapX && overlapZ;
        }

        private void InitialChunkLoad()
        {
            ChunkCoord[] initialChunks = GetChunksInRadius(_currentPlayerChunk, GridRadius);

            // 1. Load the center chunk SYNCHRONOUSLY so collision exists before
            //    any physics frame runs. The player must be able to stand on this.
            LoadChunkSync(_currentPlayerChunk);

            // Mark initial load complete and emit signal so Player/Bakkie can spawn
            IsInitialLoadComplete = true;
            EmitSignal(SignalName.InitialTerrainReady);
            GD.Print("[WorldChunkStreamer] Center chunk ready — InitialTerrainReady emitted");

            // 2. Load the remaining surrounding chunks asynchronously
            foreach (var coord in initialChunks)
            {
                if (coord == _currentPlayerChunk) continue; // Already loaded sync

                if (_activeChunks.Count + _chunksBeingLoaded.Count < MaxActiveChunks)
                {
                    LoadChunkAsync(coord);
                }
            }

            GD.Print($"[WorldChunkStreamer] Initial load started around chunk {_currentPlayerChunk}");
            FenceSystem.Instance?.UpdateVisibleChunks(initialChunks);
        }
        
        /// <summary>
        /// Gets all chunks within the specified radius of a center chunk.
        /// </summary>
        private ChunkCoord[] GetChunksInRadius(ChunkCoord center, int radius)
        {
            int diameter = radius * 2 + 1;
            var chunks = new ChunkCoord[diameter * diameter];
            int index = 0;
            
            for (int z = -radius; z <= radius; z++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    chunks[index++] = new ChunkCoord(center.X + x, center.Z + z);
                }
            }
            
            return chunks;
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
            // Determine desired grid around player
            ChunkCoord[] desiredChunks = GetChunksInRadius(_currentPlayerChunk, GridRadius);
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

            // Synchronize fence system visibility
            FenceSystem.Instance?.UpdateVisibleChunks(desiredChunks);
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
                
                // Track build time
                if (buildResult.TerrainData != null)
                {
                    TotalChunkBuildTime += buildResult.TerrainData.BuildTimeMs;
                    TotalChunksBuilt++;
                }
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
                    _buildingRenderer?.OnChunkLoaded(buildResult.Coordinate);
                    resultsApplied++;
                    
                    ChunkLoaded?.Invoke(buildResult.Coordinate);
                }
            }
        }

        /// <summary>
        /// Synchronously loads a chunk: builds content on the current thread and
        /// immediately adds it to the scene tree with collision. Used for the
        /// initial center chunk so the player can spawn before any physics frame.
        /// </summary>
        private void LoadChunkSync(ChunkCoord coord)
        {
            if (_activeChunks.ContainsKey(coord))
            {
                return; // Already loaded
            }

            try
            {
                var chunk = new WorldChunk();
                chunk.Initialize(coord, ChunkSize);

                // Build on main thread — blocking but guaranteed done before return
                var buildResult = chunk.BuildChunkContent();

                // Add to scene tree and apply result immediately
                AddChild(chunk);
                chunk.ApplyBuildResult(buildResult);
                _buildingRenderer?.OnChunkLoaded(coord);

                _activeChunks[coord] = chunk;
                
                // Track build time
                if (buildResult.TerrainData != null)
                {
                    TotalChunkBuildTime += buildResult.TerrainData.BuildTimeMs;
                    TotalChunksBuilt++;
                }

                GD.Print($"[WorldChunkStreamer] Sync loaded center chunk {coord}");
                ChunkLoaded?.Invoke(coord);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[WorldChunkStreamer] Error sync loading chunk {coord}: {ex}");
            }
        }

        private void UnloadChunk(ChunkCoord coord)
        {
            if (!_activeChunks.TryGetValue(coord, out WorldChunk chunk))
            {
                return;
            }

            // Notify BuildingRenderer before removing chunk
            _buildingRenderer?.OnChunkUnloaded(coord);

            chunk.UnloadChunk();
            if (chunk.IsInsideTree())
            {
                RemoveChild(chunk);
            }
            chunk.QueueFree();
            
            _activeChunks.Remove(coord);

            ChunkUnloaded?.Invoke(coord);
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
        /// Gets all currently active chunks.
        /// </summary>
        public IReadOnlyDictionary<ChunkCoord, WorldChunk> GetActiveChunks()
        {
            return _activeChunks;
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
            
            // Clear TerrainSystem cache
            TerrainSystem.Instance?.ClearCache();

            GD.Print("[WorldChunkStreamer] All chunks unloaded");
        }
        
        /// <summary>
        /// Gets performance statistics.
        /// </summary>
        public string GetPerformanceStats()
        {
            float avgBuildTime = TotalChunksBuilt > 0 ? TotalChunkBuildTime / TotalChunksBuilt : 0f;
            return $"[WorldChunkStreamer]\n" +
                   $"  Active chunks: {ActiveChunkCount}\n" +
                   $"  Loading: {LoadingChunkCount}\n" +
                   $"  Pending: {PendingResultCount}\n" +
                   $"  Total built: {TotalChunksBuilt}\n" +
                   $"  Avg build time: {avgBuildTime:F1}ms\n" +
                   $"  TerrainSystem: {TerrainSystem.Instance?.GetCacheStats()}";
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
