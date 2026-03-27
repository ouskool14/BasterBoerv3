// ============================================================
//  GrassEcologySystem.cs
//  BasterBoer — Grass Ecology Simulation
//
//  Singleton manager that tracks all GrassSpawner instances,
//  simulates grass biomass / health over time, and communicates
//  state changes to the render layer (GrassSpawner) efficiently.
//
//  Integration points:
//    - TimeSystem.OnDayPassed   → daily simulation tick
//    - TimeSystem.OnSeasonChanged → seasonal moisture update
//    - HerdBrain                → ApplyGrazing()
//
//  Performance contract:
//    - Zero per-frame heavy work
//    - No LINQ in any hot path
//    - No allocations per tick
//    - SetDensity / SetDrought only called when delta > threshold
// ============================================================

using Godot;
using System.Collections.Generic;
using BasterBoer.Core.Time;

namespace BasterBoer.Core.Systems
{
	// --------------------------------------------------------
	//  GrassPatchState
	//  Simulation data per terrain patch (one per GrassSpawner).
	//  Class (not struct) — lives in Dictionary, never copied hot.
	// --------------------------------------------------------
	public sealed class GrassPatchState
	{
		/// <summary>Amount of grass present. 0 = bare, 1 = full coverage.</summary>
		public float Biomass;

		/// <summary>Grass quality. Affects colour / drought shader. 0 = dead, 1 = lush.</summary>
		public float Health;

		/// <summary>Accumulated grazing load since last decay. Drives health penalty.</summary>
		public float GrazingPressure;

		/// <summary>
		/// Local moisture (0–1). Set per-patch or inherited from global season moisture.
		/// Can be overridden per patch for rivers, dry ridges, etc.
		/// </summary>
		public float Moisture;

		// ---- Render-layer dirty tracking (internal) ----

		/// <summary>Last Biomass value pushed to SetDensity(). Used for threshold check.</summary>
		internal float LastRenderedBiomass;

		/// <summary>Last Health value pushed to SetDrought(). Used for threshold check.</summary>
		internal float LastRenderedHealth;

		/// <summary>True if simulation data changed enough to warrant a visual update.</summary>
		internal bool IsDirty;

		public GrassPatchState(float initialBiomass = 1f, float initialMoisture = 0.5f)
		{
			Biomass          = initialBiomass;
			Health           = 1f;
			GrazingPressure  = 0f;
			Moisture         = initialMoisture;
			LastRenderedBiomass = initialBiomass;
			LastRenderedHealth  = 1f;
			IsDirty          = false;
		}
	}

	// --------------------------------------------------------
	//  GrassEcologySystem
	// --------------------------------------------------------
	public partial class GrassEcologySystem : Node
	{
		// ====================================================
		//  Singleton
		// ====================================================

		public static GrassEcologySystem Instance { get; private set; }

		// ====================================================
		//  Exports — tunable from the Godot Inspector
		// ====================================================

		/// <summary>Base rate at which grass regrows per in-game day (scaled by moisture).</summary>
		[Export] public float GrowthRate { get; set; } = 0.05f;

		/// <summary>Rate at which grass health recovers per day when not heavily grazed.</summary>
		[Export] public float HealthRecoveryRate { get; set; } = 0.02f;

		/// <summary>
		/// Daily fraction by which accumulated grazing pressure decays.
		/// 0.1 = 10% decay per day → pressure from a single graze clears in ~10 days.
		/// </summary>
		[Export] public float GrazingPressureDecayRate { get; set; } = 0.10f;

		/// <summary>Health penalty applied each day when moisture is below DroughtThreshold.</summary>
		[Export] public float DroughtHealthPenalty { get; set; } = 0.015f;

		/// <summary>Moisture level below which drought health penalties apply.</summary>
		[Export] public float DroughtThreshold { get; set; } = 0.30f;

		/// <summary>
		/// Minimum biomass change before SetDensity() is called.
		/// Prevents unnecessary MultiMesh rebuilds for trivial deltas.
		/// </summary>
		[Export] public float BiomassRenderThreshold { get; set; } = 0.02f;

		/// <summary>Minimum health change before SetDrought() is called.</summary>
		[Export] public float HealthRenderThreshold { get; set; } = 0.02f;

		/// <summary>Real-time interval (seconds) between visual sync flushes.</summary>
		[Export] public float VisualSyncInterval { get; set; } = 1.0f;

		/// <summary>Print debug info each simulation tick.</summary>
		[Export] public bool DebugMode { get; set; } = false;

		// ====================================================
		//  Events (optional subscribers)
		// ====================================================

		/// <summary>Fired when a patch's biomass drops to (or below) this fraction.</summary>
		public const float DepletionThreshold = 0.10f;

		/// <summary>
		/// Emitted when a grass patch drops below DepletionThreshold.
		/// Signature: (GrassSpawner spawner, GrassPatchState state)
		/// </summary>
		public event System.Action<GrassSpawner, GrassPatchState> OnGrassDepleted;

		/// <summary>
		/// Emitted when a fully-depleted patch recovers above DepletionThreshold.
		/// </summary>
		public event System.Action<GrassSpawner, GrassPatchState> OnGrassRecovered;

		// ====================================================
		//  Private State
		// ====================================================

		// Primary lookup: spawner → state (O(1) access for ApplyGrazing)
		private readonly Dictionary<GrassSpawner, GrassPatchState> _patchStates
			= new Dictionary<GrassSpawner, GrassPatchState>();

		// Parallel list for hot-path iteration (no Dictionary enumerator overhead)
		private readonly List<GrassSpawner> _registeredSpawners
			= new List<GrassSpawner>();

		// Dirty set — only patches needing a visual update
		// Using List instead of HashSet: avoids boxing, faster clear, good for ~50 dirty patches
		private readonly List<GrassSpawner> _dirtyList
			= new List<GrassSpawner>();

		// Re-used temp list for depletion events (avoids allocation inside tick)
		private readonly List<(GrassSpawner, GrassPatchState)> _depletionEvents
			= new List<(GrassSpawner, GrassPatchState)>();

		// Real-time accumulator for visual sync
		private float _visualSyncAccumulator;

		// Global moisture driven by season (South African calendar)
		// Individual patches may override this via their own Moisture field.
		private float _globalSeasonalMoisture = 0.5f;

		// Tracks previously-depleted patches to fire OnGrassRecovered correctly
		private readonly HashSet<GrassSpawner> _depletedSpawners
			= new HashSet<GrassSpawner>();

		// ====================================================
		//  Godot Lifecycle
		// ====================================================

		public override void _Ready()
		{
			// --- Singleton guard ---
			if (Instance != null && Instance != this)
			{
				GD.PushWarning("[GrassEcologySystem] Duplicate instance detected — freeing.");
				QueueFree();
				return;
			}
			Instance = this;

			// --- Hook into TimeSystem ---
			if (TimeSystem.Instance == null)
			{
				GD.PushError("[GrassEcologySystem] TimeSystem not found. " +
							 "Ensure TimeSystem is above GrassEcologySystem in the scene tree.");
				return;
			}

			TimeSystem.Instance.OnDayPassed    += OnDayPassed;
			TimeSystem.Instance.OnSeasonChanged += OnSeasonChanged;

			// Initialise seasonal moisture from current season
			_globalSeasonalMoisture = GetMoistureForSeason(TimeSystem.Instance.CurrentSeason);

			// --- Auto-discover GrassSpawner nodes via Godot group ---
			DiscoverSpawnersFromGroup();

			if (DebugMode)
				GD.Print($"[GrassEcologySystem] Ready. Registered {_registeredSpawners.Count} " +
						 $"spawners. Season moisture: {_globalSeasonalMoisture:F2}");
		}

		public override void _ExitTree()
		{
			if (TimeSystem.Instance != null)
			{
				TimeSystem.Instance.OnDayPassed    -= OnDayPassed;
				TimeSystem.Instance.OnSeasonChanged -= OnSeasonChanged;
			}

			if (Instance == this)
				Instance = null;
		}

		// --------------------------------------------------------
		//  _Process — LIGHTWEIGHT ONLY.
		//  Advances visual sync timer and flushes dirty patches.
		//  No simulation logic here (see OnDayPassed).
		// --------------------------------------------------------
		public override void _Process(double delta)
		{
			_visualSyncAccumulator += (float)delta;

			if (_visualSyncAccumulator >= VisualSyncInterval)
			{
				_visualSyncAccumulator -= VisualSyncInterval;
				FlushDirtyVisuals();
			}
		}

		// ====================================================
		//  Discovery & Registration
		// ====================================================

		/// <summary>
		/// Scans the scene tree for nodes in the "grass_spawner" group
		/// and registers them automatically.
		/// 
		/// Called once at startup. Spawners added at runtime should call
		/// RegisterSpawner() directly from their own _Ready().
		/// </summary>
		private void DiscoverSpawnersFromGroup()
		{
			var nodes = GetTree().GetNodesInGroup("grass_spawner");
			for (int i = 0; i < nodes.Count; i++)
			{
				if (nodes[i] is GrassSpawner spawner)
					RegisterSpawner(spawner);
				else
					GD.PushWarning($"[GrassEcologySystem] Node '{nodes[i].Name}' is in 'grass_spawner' " +
								   "group but is not a GrassSpawner.");
			}
		}

		/// <summary>
		/// Registers a GrassSpawner with the ecology system.
		/// Safe to call multiple times — duplicates are ignored.
		/// </summary>
		public void RegisterSpawner(GrassSpawner spawner)
		{
			if (spawner == null || _patchStates.ContainsKey(spawner))
				return;

			var state = new GrassPatchState(
				initialBiomass:  1.0f,
				initialMoisture: _globalSeasonalMoisture
			);

			_patchStates.Add(spawner, state);
			_registeredSpawners.Add(spawner);

			if (DebugMode)
				GD.Print($"[GrassEcologySystem] Registered spawner '{spawner.Name}'. " +
						 $"Total: {_registeredSpawners.Count}");
		}

		/// <summary>
		/// Unregisters a GrassSpawner (e.g. when a chunk is unloaded).
		/// </summary>
		public void UnregisterSpawner(GrassSpawner spawner)
		{
			if (spawner == null || !_patchStates.ContainsKey(spawner))
				return;

			_patchStates.Remove(spawner);
			_registeredSpawners.Remove(spawner);  // O(n) — rare operation, acceptable
			_dirtyList.Remove(spawner);
			_depletedSpawners.Remove(spawner);

			if (DebugMode)
				GD.Print($"[GrassEcologySystem] Unregistered spawner '{spawner.Name}'. " +
						 $"Total: {_registeredSpawners.Count}");
		}

		// ====================================================
		//  Public API
		// ====================================================

		/// <summary>
		/// Called by HerdBrain (or any grazing system) to apply grazing
		/// pressure to a specific patch.
		/// 
		/// Thread-safety: Call from main thread only (Godot node access).
		/// </summary>
		/// <param name="spawner">The GrassSpawner being grazed.</param>
		/// <param name="amount">
		///   Grazing intensity (0–1). Typical values:
		///   0.01 per animal per tick for background herds,
		///   0.05 for close, active grazing.
		/// </param>
		public void ApplyGrazing(GrassSpawner spawner, float amount)
		{
			if (spawner == null) return;
			if (!_patchStates.TryGetValue(spawner, out GrassPatchState state)) return;

			// Clamp incoming amount
			float grazed = Mathf.Clamp(amount, 0f, state.Biomass);

			state.Biomass         = Mathf.Max(state.Biomass - grazed, 0f);
			state.Health          = Mathf.Max(state.Health - grazed * 0.3f, 0f); // grazing hurts health less than drought
			state.GrazingPressure = Mathf.Min(state.GrazingPressure + grazed, 1f);

			MarkDirty(spawner, state);
		}

		/// <summary>
		/// Returns the current simulation state for a patch.
		/// Returns null if the spawner is not registered.
		/// </summary>
		public GrassPatchState GetState(GrassSpawner spawner)
		{
			if (spawner == null) return null;
			_patchStates.TryGetValue(spawner, out GrassPatchState state);
			return state;
		}

		/// <summary>
		/// Directly override the moisture for a specific patch
		/// (e.g. near a river, or on a dry rocky ridge).
		/// </summary>
		public void SetPatchMoisture(GrassSpawner spawner, float moisture)
		{
			if (!_patchStates.TryGetValue(spawner, out GrassPatchState state)) return;
			state.Moisture = Mathf.Clamp(moisture, 0f, 1f);
		}

		// ====================================================
		//  Daily Simulation Tick  (from TimeSystem.OnDayPassed)
		// ====================================================

		private void OnDayPassed(GameDate date)
		{
			float startTime = DebugMode ? Godot.Time.GetTicksMsec() : 0f;

			SimulateDailyTick();

			if (DebugMode)
			{
				float elapsed = Godot.Time.GetTicksMsec() - startTime;
				PrintDebugSummary(elapsed);
			}

			// Fire depletion / recovery events collected during tick
			FlushEcologyEvents();
		}

		/// <summary>
		/// Core simulation loop. Called once per in-game day.
		/// Iterates _registeredSpawners directly — no LINQ, no allocations.
		/// </summary>
		private void SimulateDailyTick()
		{
			int count = _registeredSpawners.Count;

			for (int i = 0; i < count; i++)
			{
				GrassSpawner spawner = _registeredSpawners[i];
				GrassPatchState state = _patchStates[spawner]; // Known to exist

				bool wasDepleted = state.Biomass <= DepletionThreshold;

				// ---- 1. Grazing pressure decay ----
				// Pressure from animals dissipates over days as they move on.
				state.GrazingPressure = Mathf.Max(
					state.GrazingPressure - GrazingPressureDecayRate, 0f);

				// ---- 2. Biomass regrowth ----
				// Logistic growth: fastest when sparse, slows as it fills.
				// Moisture and grazing pressure both gate regrowth.
				float moistureEffect  = state.Moisture;
				float pressureScaling = 1f - state.GrazingPressure * 0.7f; // heavy grazing halves regrowth
				float biomassGrowth   = GrowthRate * moistureEffect * pressureScaling
										* (1f - state.Biomass);
				state.Biomass = Mathf.Min(state.Biomass + biomassGrowth, 1f);

				// ---- 3. Health update ----
				if (state.Moisture < DroughtThreshold)
				{
					// Drought degrades health
					float droughtSeverity = 1f - (state.Moisture / DroughtThreshold);
					state.Health = Mathf.Max(
						state.Health - DroughtHealthPenalty * droughtSeverity, 0f);
				}
				else if (state.GrazingPressure < 0.2f)
				{
					// Low pressure and adequate moisture → health recovers
					state.Health = Mathf.Min(state.Health + HealthRecoveryRate, 1f);
				}
				// If grazing pressure is high but moisture OK, health holds steady (no net change)

				// ---- 4. Dirty check ----
				MarkDirty(spawner, state);

				// ---- 5. Depletion / recovery event tracking ----
				bool isDepleted = state.Biomass <= DepletionThreshold;
				if (isDepleted && !wasDepleted)
					_depletionEvents.Add((spawner, state)); // newly depleted
				else if (!isDepleted && _depletedSpawners.Contains(spawner))
					_depletionEvents.Add((spawner, null));  // null signals recovery
			}
		}

		// ====================================================
		//  Seasonal Moisture  (from TimeSystem.OnSeasonChanged)
		// ====================================================

		private void OnSeasonChanged(Season season, GameDate date)
		{
			float newMoisture = GetMoistureForSeason(season);
			float delta = newMoisture - _globalSeasonalMoisture;
			_globalSeasonalMoisture = newMoisture;

			// Nudge all patches toward the new season moisture.
			// Patches with custom moisture (rivers, ridges) are only partially affected.
			int count = _registeredSpawners.Count;
			for (int i = 0; i < count; i++)
			{
				GrassPatchState state = _patchStates[_registeredSpawners[i]];
				// Blend: patches track the season but retain ~30% of their local character.
				state.Moisture = Mathf.Clamp(state.Moisture + delta * 0.7f, 0f, 1f);
			}

			if (DebugMode)
				GD.Print($"[GrassEcologySystem] Season changed → {season}. " +
						 $"Global moisture: {_globalSeasonalMoisture:F2}");
		}

		/// <summary>
		/// Maps South African seasons to moisture values.
		/// Summer (Dec–Feb) is the rainy season; Winter (Jun–Aug) is dry.
		/// </summary>
		private static float GetMoistureForSeason(Season season)
		{
			return season switch
			{
				Season.Summer => 0.80f,  // Highveld rainy season — lush
				Season.Autumn => 0.50f,  // Cooling, rain tapering
				Season.Winter => 0.20f,  // Dry, frost in places
				Season.Spring => 0.60f,  // Pre-rain growth flush
				_             => 0.50f
			};
		}

		// ====================================================
		//  Visual Sync  — render layer bridge
		// ====================================================

		/// <summary>
		/// Marks a patch as needing a visual update if its values
		/// changed beyond the render threshold.
		/// Avoids redundant SetDensity / SetDrought calls.
		/// </summary>
		private void MarkDirty(GrassSpawner spawner, GrassPatchState state)
		{
			bool biomassDirty = Mathf.Abs(state.Biomass - state.LastRenderedBiomass)
								>= BiomassRenderThreshold;
			bool healthDirty  = Mathf.Abs(state.Health  - state.LastRenderedHealth)
								>= HealthRenderThreshold;

			if ((biomassDirty || healthDirty) && !state.IsDirty)
			{
				state.IsDirty = true;
				_dirtyList.Add(spawner);
			}
		}

		/// <summary>
		/// Pushes pending visual updates to the GrassSpawner render layer.
		/// Called on the 1-second real-time timer (lightweight pass).
		/// 
		/// Only patches in _dirtyList are touched.
		/// SetDensity / SetDrought are only called when the delta exceeds threshold.
		/// Spawn() is never called here (mesh already exists).
		/// </summary>
		private void FlushDirtyVisuals()
		{
			int count = _dirtyList.Count;
			if (count == 0) return;

			for (int i = 0; i < count; i++)
			{
				GrassSpawner spawner = _dirtyList[i];

				// Spawner may have been freed between dirty-mark and flush
				if (!IsInstanceValid(spawner) || !_patchStates.ContainsKey(spawner))
					continue;

				GrassPatchState state = _patchStates[spawner];

				// Biomass → density
				if (Mathf.Abs(state.Biomass - state.LastRenderedBiomass) >= BiomassRenderThreshold)
				{
					spawner.SetDensity(state.Biomass);
					state.LastRenderedBiomass = state.Biomass;
				}

				// Health → drought shader (inverted: low health = high drought)
				if (Mathf.Abs(state.Health - state.LastRenderedHealth) >= HealthRenderThreshold)
				{
					spawner.SetDrought(1f - state.Health);
					state.LastRenderedHealth = state.Health;
				}

				state.IsDirty = false;
			}

			_dirtyList.Clear();
		}

		// ====================================================
		//  Ecology Events
		// ====================================================

		private void FlushEcologyEvents()
		{
			int count = _depletionEvents.Count;
			if (count == 0) return;

			for (int i = 0; i < count; i++)
			{
				var (spawner, state) = _depletionEvents[i];

				if (state != null)
				{
					// Newly depleted
					_depletedSpawners.Add(spawner);
					OnGrassDepleted?.Invoke(spawner, state);
				}
				else
				{
					// Recovered
					_depletedSpawners.Remove(spawner);
					GrassPatchState recovered = _patchStates.ContainsKey(spawner)
						? _patchStates[spawner] : null;
					if (recovered != null)
						OnGrassRecovered?.Invoke(spawner, recovered);
				}
			}

			_depletionEvents.Clear();
		}

		// ====================================================
		//  Debug Utilities
		// ====================================================

		private void PrintDebugSummary(float elapsedMs)
		{
			int   count        = _registeredSpawners.Count;
			float totalBiomass = 0f;
			float totalHealth  = 0f;

			// Direct array iteration — no LINQ
			for (int i = 0; i < count; i++)
			{
				GrassPatchState s = _patchStates[_registeredSpawners[i]];
				totalBiomass += s.Biomass;
				totalHealth  += s.Health;
			}

			float avgBiomass = count > 0 ? totalBiomass / count : 0f;
			float avgHealth  = count > 0 ? totalHealth  / count : 0f;

			GD.Print($"[GrassEcologySystem] Tick complete | " +
					 $"Patches: {count} | " +
					 $"Avg Biomass: {avgBiomass:F3} | " +
					 $"Avg Health: {avgHealth:F3} | " +
					 $"Dirty: {_dirtyList.Count} | " +
					 $"Time: {elapsedMs:F2}ms");
		}

		/// <summary>
		/// Prints a detailed breakdown of all patch states.
		/// Heavy — call manually from editor or debug console only.
		/// </summary>
		public void PrintAllPatchStates()
		{
			GD.Print("[GrassEcologySystem] === Patch State Dump ===");
			for (int i = 0; i < _registeredSpawners.Count; i++)
			{
				GrassSpawner    spawner = _registeredSpawners[i];
				GrassPatchState state   = _patchStates[spawner];
				GD.Print($"  [{i:D4}] {spawner.Name,-30} " +
						 $"Biomass={state.Biomass:F2}  " +
						 $"Health={state.Health:F2}  " +
						 $"Pressure={state.GrazingPressure:F2}  " +
						 $"Moisture={state.Moisture:F2}  " +
						 $"Dirty={state.IsDirty}");
			}
			GD.Print($"[GrassEcologySystem] ======================== " +
					 $"Total: {_registeredSpawners.Count}");
		}
	}
}
