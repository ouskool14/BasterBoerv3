using System.Collections.Generic;
using System.Threading;
using BasterBoer.Core.Time;
using Godot;

namespace WorldStreaming.Flora
{
    /// <summary>
    /// Global singleton managing all flora simulation data for the entire world.
    /// Thread-safe for background chunk loading. Pure C# data - no Godot nodes.
    /// 
    /// REPLACES the old FloraSystem completely. Key changes:
    /// - Uses byte ArchetypeId instead of FloraType enum
    /// - ChunkEcologyState drives all visual variation
    /// - FloraPatch system for dense shrub placement
    /// - TimeSystem integration for daily/monthly/seasonal ticks
    /// - GrazingSystem integration for grazing pressure
    /// </summary>
    [GlobalClass]
    public partial class FloraSystem : Node
    {
        private static FloraSystem _instance;
        public static FloraSystem Instance => _instance;

        // ── Configuration ──────────────────────────────────────────────────

        [Export]
        public float ChunkSizeMeters { get; set; } = 256f;

        [Export]
        public ulong WorldSeed { get; set; } = 12345;

        /// <summary>
        /// The biome recipe for Bushveld. TODO: Support multiple biomes in future.
        /// </summary>
        public BushveldBiomeRecipe BiomeRecipe { get; private set; }

        // ── Data Storage ───────────────────────────────────────────────────

        /// <summary>
        /// Spatial indexing: chunk coordinate -> structural flora entries.
        /// </summary>
        private readonly Dictionary<ChunkCoord, List<FloraEntry>> _structuralByChunk = new();

        /// <summary>
        /// Spatial indexing: chunk coordinate -> flora patches.
        /// </summary>
        private readonly Dictionary<ChunkCoord, List<FloraPatch>> _patchesByChunk = new();

        /// <summary>
        /// Per-chunk ecology state. This is persisted to save files.
        /// </summary>
        private readonly Dictionary<ChunkCoord, ChunkEcologyState> _ecologyByChunk = new();

        /// <summary>
        /// Tracks which chunks have been modified by player (need persistence).
        /// </summary>
        private readonly HashSet<ChunkCoord> _modifiedChunks = new();

        // Thread safety for concurrent chunk loading
        private readonly ReaderWriterLockSlim _dataLock = new();

        // ── Statistics ─────────────────────────────────────────────────────

        public int TotalStructuralCount { get; private set; }
        public int TotalPatchCount { get; private set; }
        public int LoadedChunkCount => _structuralByChunk.Count;

        // ── Lifecycle ──────────────────────────────────────────────────────

        public override void _EnterTree()
        {
            if (_instance != null && _instance != this)
            {
                GD.PushWarning("[FloraSystem] Multiple instances detected. Keeping the first one.");
                QueueFree();
                return;
            }
            _instance = this;

            // Initialize default Bushveld recipe
            BiomeRecipe = BushveldBiomeRecipe.CreateBushveld();

            GD.Print("[FloraSystem] Initialized with Bushveld biome recipe");
        }

        public override void _ExitTree()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            _dataLock?.Dispose();
        }

        // ── Public API (Stable Signatures) ─────────────────────────────────

        /// <summary>
        /// Gets all flora entries for a specific chunk. Thread-safe.
        /// If data doesn't exist, procedurally generates it (lazy loading).
        /// </summary>
        public List<FloraEntry> GetFloraForChunk(ChunkCoord coord)
        {
            // Return structural entries (for backward compatibility)
            return GetStructuralForChunk(coord);
        }

        /// <summary>
        /// Gets structural flora entries for a chunk.
        /// </summary>
        public List<FloraEntry> GetStructuralForChunk(ChunkCoord coord)
        {
            _dataLock.EnterReadLock();
            try
            {
                if (_structuralByChunk.TryGetValue(coord, out var existing))
                {
                    return new List<FloraEntry>(existing);
                }
            }
            finally
            {
                _dataLock.ExitReadLock();
            }

            // Generate new data
            GenerateChunkData(coord);

            _dataLock.EnterReadLock();
            try
            {
                return new List<FloraEntry>(_structuralByChunk[coord]);
            }
            finally
            {
                _dataLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Gets flora patches for a chunk.
        /// </summary>
        public List<FloraPatch> GetPatchesForChunk(ChunkCoord coord)
        {
            _dataLock.EnterReadLock();
            try
            {
                if (_patchesByChunk.TryGetValue(coord, out var existing))
                {
                    return new List<FloraPatch>(existing);
                }
            }
            finally
            {
                _dataLock.ExitReadLock();
            }

            // Generate new data
            GenerateChunkData(coord);

            _dataLock.EnterReadLock();
            try
            {
                return new List<FloraPatch>(_patchesByChunk[coord]);
            }
            finally
            {
                _dataLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Gets the ecology state for a chunk.
        /// </summary>
        public ChunkEcologyState GetChunkEcology(ChunkCoord coord)
        {
            _dataLock.EnterReadLock();
            try
            {
                if (_ecologyByChunk.TryGetValue(coord, out var ecology))
                {
                    return ecology;
                }
            }
            finally
            {
                _dataLock.ExitReadLock();
            }

            // Return neutral ecology if not generated yet
            return ChunkEcologyState.CreateNeutral();
        }

        /// <summary>
        /// Sets the ecology state for a chunk. Called by GrazingSystem, fire system, etc.
        /// </summary>
        public void SetChunkEcology(ChunkCoord coord, ChunkEcologyState state)
        {
            _dataLock.EnterWriteLock();
            try
            {
                _ecologyByChunk[coord] = state;
                _modifiedChunks.Add(coord);
            }
            finally
            {
                _dataLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Checks if flora data exists for a chunk.
        /// </summary>
        public bool HasFloraForChunk(ChunkCoord coord)
        {
            _dataLock.EnterReadLock();
            try
            {
                return _structuralByChunk.ContainsKey(coord);
            }
            finally
            {
                _dataLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Gets flora entries within a radius of a center point.
        /// </summary>
        public List<FloraEntry> GetFloraInRadius(Vector3 center, float radius)
        {
            var result = new List<FloraEntry>();
            Vector2 center2D = new Vector2(center.X, center.Z);
            float radiusSq = radius * radius;

            // Determine which chunks to check
            int chunkRadius = Mathf.CeilToInt(radius / ChunkSizeMeters) + 1;
            ChunkCoord centerChunk = ChunkCoord.FromWorldPosition(center, ChunkSizeMeters);

            _dataLock.EnterReadLock();
            try
            {
                for (int z = -chunkRadius; z <= chunkRadius; z++)
                {
                    for (int x = -chunkRadius; x <= chunkRadius; x++)
                    {
                        var coord = new ChunkCoord(centerChunk.X + x, centerChunk.Z + z);

                        if (_structuralByChunk.TryGetValue(coord, out var entries))
                        {
                            foreach (var entry in entries)
                            {
                                if (entry.WorldPosition2D.DistanceSquaredTo(center2D) <= radiusSq)
                                {
                                    result.Add(entry);
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                _dataLock.ExitReadLock();
            }

            return result;
        }

        // ── TimeSystem Integration ────────────────────────────────────────

        /// <summary>
        /// Called by TimeSystem on daily tick. Cheap updates.
        /// </summary>
        public void OnDailyTick()
        {
            // Daily updates are minimal - most work happens monthly
            // Could update recovery factor decay here if needed
        }

        /// <summary>
        /// Called by TimeSystem on monthly tick. Heavier simulation updates.
        /// </summary>
        public void OnMonthlyTick(float rainfallThisMonth, float baseEvaporation = 0.08f)
        {
            _dataLock.EnterWriteLock();
            try
            {
                var coords = new List<ChunkCoord>(_ecologyByChunk.Keys);

                foreach (var coord in coords)
                {
                    var ecology = _ecologyByChunk[coord];

                    // Moisture update
                    ecology.Moisture += rainfallThisMonth * 0.3f;
                    ecology.Moisture -= baseEvaporation;

                    // Water proximity bonus (if near water)
                    Vector3 chunkCenter = coord.GetWorldCenter(ChunkSizeMeters);
                    float waterProximity = GetWaterProximityBonus(chunkCenter);
                    ecology.Moisture += waterProximity;

                    ecology.Moisture = Mathf.Clamp(ecology.Moisture, 0f, 1f);

                    // Drought stress update
                    if (ecology.Moisture < 0.2f)
                        ecology.DroughtStress += 0.12f;
                    else if (ecology.Moisture > 0.5f)
                        ecology.DroughtStress -= 0.08f;

                    ecology.DroughtStress = Mathf.Clamp(ecology.DroughtStress, 0f, 1f);

                    // Grazing pressure natural decay
                    if (ecology.GrazingPressure > 0f)
                    {
                        ecology.GrazingPressure -= 0.05f;
                        ecology.GrazingPressure = Mathf.Max(0f, ecology.GrazingPressure);
                    }

                    // Burn age update
                    if (ecology.HasBeenBurned && ecology.BurnAge < 1f)
                    {
                        ecology.BurnAge += 0.04f; // Full recovery ~25 months
                        if (ecology.BurnAge >= 1f)
                            ecology.BurnAge = 1f;
                    }

                    // Invasive pressure update
                    if (ecology.GrazingPressure > 0.5f && ecology.Moisture > 0.3f)
                    {
                        ecology.InvasivePressure += 0.03f;
                    }
                    ecology.InvasivePressure = Mathf.Clamp(ecology.InvasivePressure, 0f, 1f);

                    // Shrub encroachment update
                    if (ecology.GrazingPressure < 0.2f && ecology.Moisture > 0.4f)
                    {
                        ecology.ShrubEncroachment += 0.02f;
                    }
                    else if (ecology.GrazingPressure > 0.6f)
                    {
                        ecology.ShrubEncroachment -= 0.04f;
                    }
                    ecology.ShrubEncroachment = Mathf.Clamp(ecology.ShrubEncroachment, 0f, 1f);

                    // Recovery factor decay
                    if (ecology.RecoveryFactor > 0f)
                    {
                        ecology.RecoveryFactor -= 0.4f; // Decays over ~2 months
                        ecology.RecoveryFactor = Mathf.Max(0f, ecology.RecoveryFactor);
                    }

                    _ecologyByChunk[coord] = ecology;
                    _modifiedChunks.Add(coord);
                }
            }
            finally
            {
                _dataLock.ExitWriteLock();
            }

            GD.Print($"[FloraSystem] Monthly tick processed for {_ecologyByChunk.Count} chunks");
        }

        /// <summary>
        /// Called by TimeSystem when season changes.
        /// </summary>
        public void OnSeasonChanged(Season newSeason)
        {
            _dataLock.EnterWriteLock();
            try
            {
                var coords = new List<ChunkCoord>(_ecologyByChunk.Keys);

                foreach (var coord in coords)
                {
                    var ecology = _ecologyByChunk[coord];
                    ecology.CurrentSeason = newSeason;

                    // Recovery factor spikes at first summer rain
                    if (newSeason == Season.Summer && ecology.Moisture > 0.4f)
                    {
                        ecology.RecoveryFactor = 0.8f;
                    }

                    _ecologyByChunk[coord] = ecology;
                    _modifiedChunks.Add(coord);
                }
            }
            finally
            {
                _dataLock.ExitWriteLock();
            }

            GD.Print($"[FloraSystem] Season changed to {newSeason}");
        }

        // ── Event Handlers ────────────────────────────────────────────────

        /// <summary>
        /// Called by fire system when a burn event occurs.
        /// </summary>
        public void ApplyBurnEvent(Vector3 center, float radius)
        {
            Vector2 center2D = new Vector2(center.X, center.Z);
            int chunkRadius = Mathf.CeilToInt(radius / ChunkSizeMeters) + 1;
            ChunkCoord centerChunk = ChunkCoord.FromWorldPosition(center, ChunkSizeMeters);

            _dataLock.EnterWriteLock();
            try
            {
                for (int z = -chunkRadius; z <= chunkRadius; z++)
                {
                    for (int x = -chunkRadius; x <= chunkRadius; x++)
                    {
                        var coord = new ChunkCoord(centerChunk.X + x, centerChunk.Z + z);

                        // Get or create ecology state
                        if (!_ecologyByChunk.TryGetValue(coord, out var ecology))
                        {
                            ecology = ChunkEcologyState.CreateNeutral();
                        }

                        // Check if chunk center is within burn radius
                        Vector3 chunkCenter = coord.GetWorldCenter(ChunkSizeMeters);
                        Vector2 chunkCenter2D = new Vector2(chunkCenter.X, chunkCenter.Z);
                        float dist = chunkCenter2D.DistanceTo(center2D);

                        if (dist <= radius)
                        {
                            // Apply burn
                            ecology.BurnAge = 0f; // Freshly burned
                            ecology.RecoveryFactor = 0.8f; // Strong recovery flush
                            ecology.Moisture *= 0.5f; // Burn dries out soil
                            ecology.InvasivePressure *= 0.3f; // Fire suppresses invasives
                            ecology.ShrubEncroachment *= 0.5f; // Fire reduces shrubs

                            _ecologyByChunk[coord] = ecology;
                            _modifiedChunks.Add(coord);
                        }
                    }
                }
            }
            finally
            {
                _dataLock.ExitWriteLock();
            }

            GD.Print($"[FloraSystem] Burn event applied at {center}, radius {radius}m");
        }

        /// <summary>
        /// Sets grazing pressure for a chunk. Called by GrazingSystem.
        /// </summary>
        public void SetGrazingPressure(ChunkCoord coord, float pressure)
        {
            _dataLock.EnterWriteLock();
            try
            {
                if (!_ecologyByChunk.TryGetValue(coord, out var ecology))
                {
                    ecology = ChunkEcologyState.CreateNeutral();
                }

                ecology.GrazingPressure = Mathf.Clamp(pressure, 0f, 1f);
                _ecologyByChunk[coord] = ecology;
                _modifiedChunks.Add(coord);
            }
            finally
            {
                _dataLock.ExitWriteLock();
            }
        }

        // ── Persistence ────────────────────────────────────────────────────

        /// <summary>
        /// Gets all modified chunk ecology states for save serialization.
        /// </summary>
        public Dictionary<ChunkCoord, ChunkEcologyState> GetModifiedEcologyForSave()
        {
            _dataLock.EnterReadLock();
            try
            {
                var result = new Dictionary<ChunkCoord, ChunkEcologyState>();
                foreach (var coord in _modifiedChunks)
                {
                    if (_ecologyByChunk.TryGetValue(coord, out var ecology))
                    {
                        result[coord] = ecology;
                    }
                }
                return result;
            }
            finally
            {
                _dataLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Loads ecology states from save data.
        /// </summary>
        public void LoadEcologyFromSave(Dictionary<ChunkCoord, ChunkEcologyState> savedEcology)
        {
            _dataLock.EnterWriteLock();
            try
            {
                foreach (var kvp in savedEcology)
                {
                    _ecologyByChunk[kvp.Key] = kvp.Value;
                    _modifiedChunks.Add(kvp.Key);
                }
            }
            finally
            {
                _dataLock.ExitWriteLock();
            }

            GD.Print($"[FloraSystem] Loaded ecology for {savedEcology.Count} chunks from save");
        }

        /// <summary>
        /// Clears all data. Used for world reset or cleanup.
        /// </summary>
        public void ClearAllData()
        {
            _dataLock.EnterWriteLock();
            try
            {
                _structuralByChunk.Clear();
                _patchesByChunk.Clear();
                _ecologyByChunk.Clear();
                _modifiedChunks.Clear();
                TotalStructuralCount = 0;
                TotalPatchCount = 0;
            }
            finally
            {
                _dataLock.ExitWriteLock();
            }

            GD.Print("[FloraSystem] All data cleared");
        }

        // ── Private Methods ────────────────────────────────────────────────

        /// <summary>
        /// Generates flora data for a chunk using FloraGenerator.
        /// </summary>
        private void GenerateChunkData(ChunkCoord coord)
        {
            // Get or create ecology state
            ChunkEcologyState ecology;
            _dataLock.EnterReadLock();
            bool hasEcology = _ecologyByChunk.TryGetValue(coord, out ecology);
            _dataLock.ExitReadLock();

            if (!hasEcology)
            {
                ecology = ChunkEcologyState.CreateNeutral();
            }

            // Generate flora
            var result = FloraGenerator.GenerateChunkFlora(
                coord, ChunkSizeMeters, BiomeRecipe, ecology, WorldSeed);

            // Store results
            _dataLock.EnterWriteLock();
            try
            {
                _structuralByChunk[coord] = result.StructuralEntries;
                _patchesByChunk[coord] = result.Patches;
                _ecologyByChunk[coord] = result.EcologyState;

                TotalStructuralCount += result.StructuralEntries.Count;
                TotalPatchCount += result.Patches.Count;
            }
            finally
            {
                _dataLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Gets water proximity bonus for a chunk center.
        /// TODO: Integrate with WaterSystem when available.
        /// </summary>
        private float GetWaterProximityBonus(Vector3 position)
        {
            // Placeholder - return small random variation
            // In full implementation, query WaterSystem for nearest water source
            return 0f;
        }

        /// <summary>
        /// Gets debug statistics.
        /// </summary>
        public string GetDebugStats()
        {
            _dataLock.EnterReadLock();
            try
            {
                return $"[FloraSystem]\n" +
                       $"  Loaded chunks: {LoadedChunkCount}\n" +
                       $"  Total structural: {TotalStructuralCount}\n" +
                       $"  Total patches: {TotalPatchCount}\n" +
                       $"  Modified chunks: {_modifiedChunks.Count}\n" +
                       $"  Biome: Bushveld";
            }
            finally
            {
                _dataLock.ExitReadLock();
            }
        }
    }
}
