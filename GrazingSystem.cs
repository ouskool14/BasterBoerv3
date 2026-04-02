using System;
using System.Collections.Generic;
using Godot;
using WorldStreaming;
using WorldStreaming.Flora;

namespace LandManagementSim.Simulation
{
	/// <summary>
	/// Lightweight interface expected by the grazing system.
	/// One instance can represent an individual animal, a herd anchor, or a feeding subgroup.
	/// </summary>
	public interface IGrazingAgent
	{
		ulong GrazingId { get; }
		Vector3 Position { get; }
		float Hunger { get; }       // Expected range: 0..1
		float GrazingRate { get; }  // Biomass units consumed per second
		bool CanGraze { get; }      // False if fleeing, sleeping, dead, etc.
	}

	/// <summary>
	/// Minimal normalized patch state returned by the ecology layer.
	/// Map your actual GrassEcologySystem state into this struct.
	/// </summary>
	public readonly struct GrassPatchState
	{
		public readonly float Biomass;
		public readonly bool IsGrazeable;
		public readonly bool IsDepleted;

		public GrassPatchState(float biomass, bool isGrazeable = true, bool isDepleted = false)
		{
			Biomass = biomass;
			IsGrazeable = isGrazeable;
			IsDepleted = isDepleted;
		}
	}

	/// <summary>
	/// Adapter interface for existing grass ecology code.
	/// Implement this once and keep GrazingSystem decoupled.
	/// </summary>
	public interface IGrassEcologyProvider
	{
		GrassPatchState GetState(GrassSpawner spawner);
		void ApplyGrazing(GrassSpawner spawner, float amount);
	}

	/// <summary>
	/// Per-agent grazing memory/state.
	/// </summary>
	public sealed class GrazingState
	{
		public GrassSpawner TargetPatch;
		public float TimeSinceLastMove;
		public Vector3 SuggestedMoveTarget;

		internal int TargetPatchIndex = -1;
		internal uint RandomState;
	}

	public readonly struct GrazingDebugLine
	{
		public readonly Vector3 From;
		public readonly Vector3 To;

		public GrazingDebugLine(Vector3 from, Vector3 to)
		{
			From = from;
			To = to;
		}
	}

	public struct GrazingDebugStats
	{
		public int RegisteredAgents;
		public int RegisteredPatches;
		public int ActiveGrazers;
		public int TargetedPatches;
		public float AverageTargetersPerUsedPatch;
	}

	/// <summary>
	/// High-performance grazing coordinator.
	/// - Does NOT move animals directly.
	/// - Suggests movement targets.
	/// - Applies grazing when animals are in range.
	/// - Uses spatial buckets for patch lookup.
	/// - Staggers patch selection decisions.
	/// </summary>
	public sealed class GrazingSystem
	{
		private static GrazingSystem _instance;

		public static GrazingSystem Instance => _instance ??= new GrazingSystem();

		private struct AgentEntry
		{
			public IGrazingAgent Agent;
			public GrazingState State;
		}

		private struct PatchEntry
		{
			public GrassSpawner Spawner;
			public Vector3 Position;
			public long BucketKey;
			public int TargeterCount;
		}

		private readonly List<AgentEntry> _agents;
		private readonly Dictionary<ulong, int> _agentIndexById;

		private readonly List<PatchEntry> _patches;
		private readonly Dictionary<GrassSpawner, int> _patchIndexBySpawner;
		private readonly Dictionary<long, List<int>> _patchBuckets;

		private float _decisionAccumulator;
		private float _grazingAccumulator;
		private float _debugLogAccumulator;

		private int _decisionCursor;
		private int _activeGrazersLastTick;

		private IGrassEcologyProvider _ecology;

		// ---- Tunables ----
		public float SearchRadiusMeters { get; set; } = 72f;
		public float BucketSizeMeters { get; set; } = 48f;
		public float GrazeRangeMeters { get; set; } = 4f;
		public float PatchSpreadRadiusMeters { get; set; } = 5f;

		/// <summary>
		/// How often the system runs patch-selection logic.
		/// With DecisionBatchFraction 0.5, each agent is reevaluated about every 2 seconds.
		/// </summary>
		public float DecisionTickSeconds { get; set; } = 1f;

		/// <summary>
		/// Fraction of agents reevaluated each decision tick.
		/// 0.5 = 50% per second => full pass roughly every 2 seconds.
		/// </summary>
		public float DecisionBatchFraction { get; set; } = 0.5f;

		/// <summary>
		/// How often grazing consumption is applied.
		/// </summary>
		public float GrazingTickSeconds { get; set; } = 0.25f;

		public float MinimumUsefulBiomass { get; set; } = 0.05f;
		public float CompetitionPenalty { get; set; } = 0.15f;
		public float CurrentPatchStickiness { get; set; } = 1.10f;
		public float NaturalMoveChancePerDecision { get; set; } = 0.10f;
		public float MinimumMoveIntervalSeconds { get; set; } = 6f;

		public bool EnableDebugLogging { get; set; } = false;

		private GrazingSystem()
		{
			_agents = new List<AgentEntry>(2048);
			_agentIndexById = new Dictionary<ulong, int>(2048);

			_patches = new List<PatchEntry>(512);
			_patchIndexBySpawner = new Dictionary<GrassSpawner, int>(512);
			_patchBuckets = new Dictionary<long, List<int>>(512);
		}

		public void SetEcologyProvider(IGrassEcologyProvider ecology)
		{
			_ecology = ecology;
		}

		public int RegisteredAgentCount => _agents.Count;
		public int RegisteredPatchCount => _patches.Count;

		public void Clear()
		{
			_agents.Clear();
			_agentIndexById.Clear();

			_patches.Clear();
			_patchIndexBySpawner.Clear();
			_patchBuckets.Clear();

			_decisionAccumulator = 0f;
			_grazingAccumulator = 0f;
			_debugLogAccumulator = 0f;
			_decisionCursor = 0;
			_activeGrazersLastTick = 0;
		}

		// ---------------------------------------------------------------------
		// Agent registration
		// ---------------------------------------------------------------------

		public GrazingState RegisterAgent(IGrazingAgent agent)
		{
			if (agent == null)
				throw new ArgumentNullException(nameof(agent));

			if (_agentIndexById.TryGetValue(agent.GrazingId, out int existingIndex))
			{
				AgentEntry existing = _agents[existingIndex];
				existing.Agent = agent;
				_agents[existingIndex] = existing;
				return existing.State;
			}

			GrazingState state = new GrazingState
			{
				TargetPatch = null,
				TimeSinceLastMove = 0f,
				SuggestedMoveTarget = agent.Position,
				TargetPatchIndex = -1,
				RandomState = SeedFromId(agent.GrazingId)
			};

			int index = _agents.Count;
			_agents.Add(new AgentEntry
			{
				Agent = agent,
				State = state
			});
			_agentIndexById.Add(agent.GrazingId, index);

			return state;
		}

		public bool UnregisterAgent(ulong grazingId)
		{
			if (!_agentIndexById.TryGetValue(grazingId, out int index))
				return false;

			ClearTarget(index);

			int lastIndex = _agents.Count - 1;
			if (index != lastIndex)
			{
				AgentEntry moved = _agents[lastIndex];
				_agents[index] = moved;
				_agentIndexById[moved.Agent.GrazingId] = index;
			}

			_agents.RemoveAt(lastIndex);
			_agentIndexById.Remove(grazingId);

			if (_decisionCursor > _agents.Count)
				_decisionCursor = 0;

			return true;
		}

		public bool TryGetState(ulong grazingId, out GrazingState state)
		{
			if (_agentIndexById.TryGetValue(grazingId, out int index))
			{
				state = _agents[index].State;
				return true;
			}

			state = null;
			return false;
		}

		public bool TryGetSuggestedTarget(ulong grazingId, out Vector3 targetPosition)
		{
			if (_agentIndexById.TryGetValue(grazingId, out int index))
			{
				targetPosition = _agents[index].State.SuggestedMoveTarget;
				return true;
			}

			targetPosition = default;
			return false;
		}

		// ---------------------------------------------------------------------
		// Patch registration
		// ---------------------------------------------------------------------

		public bool RegisterPatch(GrassSpawner spawner)
		{
			if (spawner == null)
				return false;

			if (_patchIndexBySpawner.ContainsKey(spawner))
				return false;

			Vector3 position = GetPatchPosition(spawner);
			long bucketKey = GetBucketKey(position);

			int index = _patches.Count;
			_patches.Add(new PatchEntry
			{
				Spawner = spawner,
				Position = position,
				BucketKey = bucketKey,
				TargeterCount = 0
			});

			_patchIndexBySpawner.Add(spawner, index);
			AddPatchIndexToBucket(bucketKey, index);
			return true;
		}

		public bool UnregisterPatch(GrassSpawner spawner)
		{
			if (spawner == null)
				return false;

			if (!_patchIndexBySpawner.TryGetValue(spawner, out int removeIndex))
				return false;

			// Clear agents targeting this patch.
			for (int i = 0; i < _agents.Count; i++)
			{
				GrazingState state = _agents[i].State;
				if (state.TargetPatch == spawner)
				{
					ClearTarget(i);
				}
			}

			int lastIndex = _patches.Count - 1;
			PatchEntry removed = _patches[removeIndex];

			RemovePatchIndexFromBucket(removed.BucketKey, removeIndex);
			_patchIndexBySpawner.Remove(removed.Spawner);

			if (removeIndex != lastIndex)
			{
				PatchEntry moved = _patches[lastIndex];
				_patches[removeIndex] = moved;
				_patchIndexBySpawner[moved.Spawner] = removeIndex;

				ReplacePatchIndexInBucket(moved.BucketKey, lastIndex, removeIndex);

				// Fix agent state references pointing at the moved patch.
				for (int i = 0; i < _agents.Count; i++)
				{
					GrazingState state = _agents[i].State;
					if (state.TargetPatchIndex == lastIndex)
					{
						state.TargetPatchIndex = removeIndex;
						state.TargetPatch = moved.Spawner;
					}
				}
			}

			_patches.RemoveAt(lastIndex);
			return true;
		}

		/// <summary>
		/// Call if your grass spawners can move.
		/// Usually unnecessary for static world patches.
		/// </summary>
		public bool RefreshPatch(GrassSpawner spawner)
		{
			if (spawner == null)
				return false;

			if (!_patchIndexBySpawner.TryGetValue(spawner, out int index))
				return false;

			PatchEntry entry = _patches[index];
			RemovePatchIndexFromBucket(entry.BucketKey, index);

			entry.Position = GetPatchPosition(spawner);
			entry.BucketKey = GetBucketKey(entry.Position);
			_patches[index] = entry;

			AddPatchIndexToBucket(entry.BucketKey, index);
			return true;
		}

		// ---------------------------------------------------------------------
		// Main update
		// ---------------------------------------------------------------------

		/// <summary>
		/// Call from your simulation heartbeat / TimeSystem bridge.
		/// Decision logic is staggered. Grazing application is lightweight.
		/// </summary>
		public void Update(float deltaTime)
		{
			if (_ecology == null)
				return;

			if (_agents.Count == 0 || _patches.Count == 0)
				return;

			_decisionAccumulator += deltaTime;
			_grazingAccumulator += deltaTime;

			if (_decisionAccumulator >= DecisionTickSeconds)
			{
				float dt = _decisionAccumulator;
				_decisionAccumulator = 0f;
				ProcessDecisionBatch(dt);
			}

			if (_grazingAccumulator >= GrazingTickSeconds)
			{
				float dt = _grazingAccumulator;
				_grazingAccumulator = 0f;
				ProcessGrazingTick(dt);
			}

			if (EnableDebugLogging)
			{
				_debugLogAccumulator += deltaTime;
				if (_debugLogAccumulator >= 5f)
				{
					_debugLogAccumulator = 0f;
					GrazingDebugStats stats = GetDebugStats();
					GD.Print(
						$"[GrazingSystem] Agents={stats.RegisteredAgents}, Patches={stats.RegisteredPatches}, " +
						$"ActiveGrazers={stats.ActiveGrazers}, TargetedPatches={stats.TargetedPatches}, " +
						$"AvgPatchUsage={stats.AverageTargetersPerUsedPatch:0.00}");
				}
			}
		}

		private void ProcessDecisionBatch(float deltaTime)
		{
			int agentCount = _agents.Count;
			if (agentCount == 0)
				return;

			int batchSize = Mathf.CeilToInt(agentCount * DecisionBatchFraction);
			if (batchSize < 1)
				batchSize = 1;
			if (batchSize > agentCount)
				batchSize = agentCount;

			for (int n = 0; n < batchSize; n++)
			{
				if (_decisionCursor >= _agents.Count)
					_decisionCursor = 0;

				int agentIndex = _decisionCursor++;
				AgentEntry entry = _agents[agentIndex];
				IGrazingAgent agent = entry.Agent;
				GrazingState state = entry.State;

				if (agent == null || !agent.CanGraze)
				{
					ClearTarget(agentIndex);
					continue;
				}

				state.TimeSinceLastMove += deltaTime;

				if (state.TargetPatchIndex >= _patches.Count)
				{
					ClearTarget(agentIndex);
				}

				bool forceMove =
					state.TimeSinceLastMove >= MinimumMoveIntervalSeconds &&
					NextFloat01(ref state.RandomState) < NaturalMoveChancePerDecision;

				int bestPatchIndex = SelectBestPatch(agentIndex, agent.Position, agent.Hunger, forceMove);

				if (bestPatchIndex != state.TargetPatchIndex)
				{
					SetTarget(agentIndex, bestPatchIndex);

					if (_agents[agentIndex].State.TargetPatchIndex >= 0)
					{
						_agents[agentIndex].State.TimeSinceLastMove = 0f;
					}
				}
				else if (bestPatchIndex >= 0)
				{
					state.SuggestedMoveTarget = GetPatchApproachPoint(bestPatchIndex, agent.GrazingId);
				}
			}
		}

		private void ProcessGrazingTick(float deltaTime)
		{
			_activeGrazersLastTick = 0;

			float grazeRange = GrazeRangeMeters + PatchSpreadRadiusMeters;
			float grazeRangeSquared = grazeRange * grazeRange;

			// Track grazing pressure per chunk for FloraSystem integration
			Dictionary<ChunkCoord, float> chunkGrazingPressure = null;
			var floraSystem = FloraSystem.Instance;
			if (floraSystem != null)
			{
				chunkGrazingPressure = new Dictionary<ChunkCoord, float>();
			}

			for (int i = 0; i < _agents.Count; i++)
			{
				AgentEntry entry = _agents[i];
				IGrazingAgent agent = entry.Agent;
				GrazingState state = entry.State;

				if (agent == null || !agent.CanGraze)
					continue;

				int patchIndex = state.TargetPatchIndex;
				if (patchIndex < 0 || patchIndex >= _patches.Count)
					continue;

				PatchEntry patch = _patches[patchIndex];
				GrassPatchState patchState = _ecology.GetState(patch.Spawner);

				if (!patchState.IsGrazeable || patchState.IsDepleted || patchState.Biomass <= MinimumUsefulBiomass)
				{
					ClearTarget(i);
					continue;
				}

				// Keep movement suggestion fresh.
				state.SuggestedMoveTarget = GetPatchApproachPoint(patchIndex, agent.GrazingId);

				float distSqToPatch = HorizontalDistanceSquared(agent.Position, patch.Position);
				if (distSqToPatch <= grazeRangeSquared)
				{
					float amount = agent.GrazingRate * deltaTime;
					if (amount > 0f)
					{
						_ecology.ApplyGrazing(patch.Spawner, amount);
						_activeGrazersLastTick++;

						// Accumulate grazing pressure for the chunk this patch belongs to
						if (chunkGrazingPressure != null)
						{
							var coord = ChunkCoord.FromWorldPosition(patch.Position, floraSystem.ChunkSizeMeters);
							chunkGrazingPressure.TryGetValue(coord, out float existing);
							chunkGrazingPressure[coord] = existing + amount;
						}
					}
				}
			}

			// Forward accumulated grazing pressure to FloraSystem
			if (chunkGrazingPressure != null)
			{
				foreach (var kvp in chunkGrazingPressure)
				{
					// Normalize pressure: scale raw biomass-consumed into 0..1 range
					// A rough heuristic: 1.0 pressure ≈ heavy sustained grazing
					float normalizedPressure = Mathf.Clamp(kvp.Value * 2f, 0f, 1f);
					floraSystem.SetGrazingPressure(kvp.Key, normalizedPressure);
				}
			}
		}

		// ---------------------------------------------------------------------
		// Patch selection
		// ---------------------------------------------------------------------

		private int SelectBestPatch(int agentIndex, Vector3 agentPosition, float hunger, bool forceMove)
		{
			GrazingState state = _agents[agentIndex].State;
			int currentIndex = state.TargetPatchIndex;

			float hungerClamped = Mathf.Clamp(hunger, 0f, 1f);
			float searchRadius = Mathf.Lerp(SearchRadiusMeters * 0.85f, SearchRadiusMeters * 1.35f, hungerClamped);
			float searchRadiusSq = searchRadius * searchRadius;

			int centerX = WorldToCell(agentPosition.X);
			int centerZ = WorldToCell(agentPosition.Z);
			int cellRadius = Mathf.CeilToInt(searchRadius / BucketSizeMeters);

			float bestScore = float.NegativeInfinity;
			int bestIndex = -1;

			for (int dz = -cellRadius; dz <= cellRadius; dz++)
			{
				int z = centerZ + dz;

				for (int dx = -cellRadius; dx <= cellRadius; dx++)
				{
					int x = centerX + dx;
					long key = PackCell(x, z);

					if (!_patchBuckets.TryGetValue(key, out List<int> bucket))
						continue;

					int bucketCount = bucket.Count;
					for (int i = 0; i < bucketCount; i++)
					{
						int patchIndex = bucket[i];
						PatchEntry patch = _patches[patchIndex];

						float distSq = HorizontalDistanceSquared(agentPosition, patch.Position);
						if (distSq > searchRadiusSq)
							continue;

						GrassPatchState patchState = _ecology.GetState(patch.Spawner);
						if (!patchState.IsGrazeable || patchState.IsDepleted || patchState.Biomass <= MinimumUsefulBiomass)
							continue;

						bool isCurrent = patchIndex == currentIndex;
						float score = ScorePatch(
							patchState.Biomass,
							distSq,
							patch.TargeterCount - (isCurrent ? 1 : 0),
							hungerClamped,
							isCurrent);

						if (forceMove && isCurrent)
						{
							score *= 0.75f;
						}

						if (score > bestScore)
						{
							bestScore = score;
							bestIndex = patchIndex;
						}
					}
				}
			}

			// Hysteresis: avoid oscillating between similarly good patches.
			if (!forceMove && currentIndex >= 0 && currentIndex < _patches.Count && bestIndex >= 0 && bestIndex != currentIndex)
			{
				PatchEntry currentPatch = _patches[currentIndex];
				GrassPatchState currentPatchState = _ecology.GetState(currentPatch.Spawner);

				if (currentPatchState.IsGrazeable && !currentPatchState.IsDepleted && currentPatchState.Biomass > MinimumUsefulBiomass)
				{
					float currentDistSq = HorizontalDistanceSquared(agentPosition, currentPatch.Position);
					float currentScore = ScorePatch(
						currentPatchState.Biomass,
						currentDistSq,
						currentPatch.TargeterCount - 1,
						hungerClamped,
						true);

					if (currentScore * CurrentPatchStickiness >= bestScore)
					{
						bestIndex = currentIndex;
					}
				}
			}

			return bestIndex;
		}

		private float ScorePatch(float biomass, float distanceSquared, int competitionCount, float hunger, bool isCurrent)
		{
			float distance = Mathf.Sqrt(distanceSquared) + 1f;
			float biomassWeight = Mathf.Lerp(0.80f, 1.45f, hunger);

			float score = (biomass * biomassWeight) / distance;
			score -= Mathf.Max(0, competitionCount) * CompetitionPenalty;

			if (isCurrent)
				score *= 1.03f;

			return score;
		}

		// ---------------------------------------------------------------------
		// Target management
		// ---------------------------------------------------------------------

		private void SetTarget(int agentIndex, int newPatchIndex)
		{
			AgentEntry entry = _agents[agentIndex];
			GrazingState state = entry.State;

			int oldPatchIndex = state.TargetPatchIndex;
			if (oldPatchIndex == newPatchIndex)
				return;

			if (oldPatchIndex >= 0 && oldPatchIndex < _patches.Count)
			{
				PatchEntry oldPatch = _patches[oldPatchIndex];
				if (oldPatch.TargeterCount > 0)
					oldPatch.TargeterCount--;
				_patches[oldPatchIndex] = oldPatch;
			}

			state.TargetPatchIndex = newPatchIndex;

			if (newPatchIndex >= 0 && newPatchIndex < _patches.Count)
			{
				PatchEntry newPatch = _patches[newPatchIndex];
				newPatch.TargeterCount++;
				_patches[newPatchIndex] = newPatch;

				state.TargetPatch = newPatch.Spawner;
				state.SuggestedMoveTarget = GetPatchApproachPoint(newPatchIndex, entry.Agent.GrazingId);
			}
			else
			{
				state.TargetPatch = null;
				state.SuggestedMoveTarget = entry.Agent != null ? entry.Agent.Position : Vector3.Zero;
			}
		}

		private void ClearTarget(int agentIndex)
		{
			SetTarget(agentIndex, -1);
		}

		// ---------------------------------------------------------------------
		// Spatial indexing
		// ---------------------------------------------------------------------

		private void AddPatchIndexToBucket(long bucketKey, int patchIndex)
		{
			if (!_patchBuckets.TryGetValue(bucketKey, out List<int> bucket))
			{
				bucket = new List<int>(8);
				_patchBuckets.Add(bucketKey, bucket);
			}

			bucket.Add(patchIndex);
		}

		private void RemovePatchIndexFromBucket(long bucketKey, int patchIndex)
		{
			if (!_patchBuckets.TryGetValue(bucketKey, out List<int> bucket))
				return;

			for (int i = 0; i < bucket.Count; i++)
			{
				if (bucket[i] == patchIndex)
				{
					int last = bucket.Count - 1;
					bucket[i] = bucket[last];
					bucket.RemoveAt(last);
					break;
				}
			}

			if (bucket.Count == 0)
			{
				_patchBuckets.Remove(bucketKey);
			}
		}

		private void ReplacePatchIndexInBucket(long bucketKey, int oldIndex, int newIndex)
		{
			if (!_patchBuckets.TryGetValue(bucketKey, out List<int> bucket))
				return;

			for (int i = 0; i < bucket.Count; i++)
			{
				if (bucket[i] == oldIndex)
				{
					bucket[i] = newIndex;
					return;
				}
			}
		}

		// ---------------------------------------------------------------------
		// Debug helpers
		// ---------------------------------------------------------------------

		public GrazingDebugStats GetDebugStats()
		{
			GrazingDebugStats stats = new GrazingDebugStats
			{
				RegisteredAgents = _agents.Count,
				RegisteredPatches = _patches.Count,
				ActiveGrazers = _activeGrazersLastTick,
				TargetedPatches = 0,
				AverageTargetersPerUsedPatch = 0f
			};

			int targetedPatchCount = 0;
			int totalTargeters = 0;

			for (int i = 0; i < _patches.Count; i++)
			{
				int targeters = _patches[i].TargeterCount;
				if (targeters > 0)
				{
					targetedPatchCount++;
					totalTargeters += targeters;
				}
			}

			stats.TargetedPatches = targetedPatchCount;
			if (targetedPatchCount > 0)
			{
				stats.AverageTargetersPerUsedPatch = (float)totalTargeters / targetedPatchCount;
			}

			return stats;
		}

		/// <summary>
		/// Simulation-only debug output.
		/// Your render/debug layer can draw these lines however it wants.
		/// </summary>
		public void GetDebugLines(List<GrazingDebugLine> output, int maxLines = 128)
		{
			if (output == null)
				return;

			output.Clear();

			int added = 0;
			for (int i = 0; i < _agents.Count; i++)
			{
				if (added >= maxLines)
					break;

				AgentEntry entry = _agents[i];
				GrazingState state = entry.State;

				if (entry.Agent == null || state.TargetPatchIndex < 0)
					continue;

				output.Add(new GrazingDebugLine(entry.Agent.Position, state.SuggestedMoveTarget));
				added++;
			}
		}

		// ---------------------------------------------------------------------
		// Utility
		// ---------------------------------------------------------------------

		private Vector3 GetPatchApproachPoint(int patchIndex, ulong agentId)
		{
			PatchEntry patch = _patches[patchIndex];
			Vector3 center = patch.Position;

			uint seed = SeedFromId(agentId ^ (ulong)(patchIndex * 2654435761u));
			float angle = NextFloat01(ref seed) * (Mathf.Pi * 2f);
			float radius = 1.25f + NextFloat01(ref seed) * PatchSpreadRadiusMeters;

			return new Vector3(
				center.X + Mathf.Cos(angle) * radius,
				center.Y,
				center.Z + Mathf.Sin(angle) * radius
			);
		}

		private static float HorizontalDistanceSquared(in Vector3 a, in Vector3 b)
		{
			float dx = a.X - b.X;
			float dz = a.Z - b.Z;
			return (dx * dx) + (dz * dz);
		}

		private int WorldToCell(float coord)
		{
			return Mathf.FloorToInt(coord / BucketSizeMeters);
		}

		private long GetBucketKey(Vector3 worldPosition)
		{
			return PackCell(WorldToCell(worldPosition.X), WorldToCell(worldPosition.Z));
		}

		private static long PackCell(int x, int z)
		{
			return ((long)x << 32) ^ (uint)z;
		}

		private static Vector3 GetPatchPosition(GrassSpawner spawner)
		{
			// Adjust if your GrassSpawner exposes a different center/anchor point.
			return spawner.GlobalPosition;
		}

		private static uint SeedFromId(ulong id)
		{
			unchecked
			{
				uint x = (uint)(id ^ (id >> 32));
				x ^= 0x9E3779B9u;
				x *= 0x85EBCA6Bu;
				x ^= x >> 13;
				x *= 0xC2B2AE35u;
				x ^= x >> 16;
				return x == 0 ? 1u : x;
			}
		}

		private static uint NextRandom(ref uint state)
		{
			unchecked
			{
				state = (state * 1664525u) + 1013904223u;
				return state;
			}
		}

		private static float NextFloat01(ref uint state)
		{
			uint value = NextRandom(ref state) & 0x00FFFFFFu;
			return value / 16777215f;
		}
	}
}
