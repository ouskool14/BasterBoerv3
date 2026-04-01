using Godot;
using System.Collections.Generic;
using LandManagementSim.Simulation;
using LandManagementSim.Terrain;

/// <summary>
/// Reads herd data from AnimalSystem every frame and renders animals
/// using one MultiMeshInstance3D per species. Bridges the pure-data
/// simulation layer to the visual world.
///
/// Attach to a Node3D in your main scene. Drag .glb files into the Inspector slots.
/// </summary>
public partial class AnimalRenderer : Node3D
{
	// ── Inspector: drag your .glb files here ─────────────────────────────

	[Export] public PackedScene KuduScene;
	[Export] public PackedScene ImpalaScene;
	[Export] public PackedScene BuffaloScene;
	[Export] public PackedScene ZebraScene;
	[Export] public PackedScene WildebeestScene;
	[Export] public PackedScene WaterbuckScene;

	[ExportGroup("Render distances")]
	[Export] public float MaxRenderDistance = 800f; // Beyond this, don't render

	[ExportGroup("Test herd")]
	[Export] public bool SpawnTestHerd = true;       // Auto-spawn a Kudu herd for testing
	[Export] public Vector3 TestHerdPosition = new(50f, 0f, 50f);
	[Export] public int TestHerdSeed = 42;

	// ── Internal state ───────────────────────────────────────────────────

	private readonly Dictionary<Species, MultiMeshInstance3D> _meshNodes = new();
	private readonly Dictionary<Species, Mesh> _speciesMeshes = new();
	private Node3D _player;

	// ============================================================================
	// INTERPOLATION STATE CACHE (Gate 1)
	// ============================================================================

	// Keyed by HerdId (whatever identifier HerdBrain exposes — int, Guid, or string)
	private Dictionary<int, HerdRenderState> _herdRenderStates = new();

	// Keyed by AnimalStruct.UniqueId
	private Dictionary<ulong, AnimalRenderState> _animalRenderStates = new();

	// Constant blend duration for individual offset changes
	// At 1.4 m/s walk speed, a 10m offset takes ~7s to walk.
	// 3s gives a natural trot pace for typical offsets within herd spread radius.
	private const float OffsetShuffleDuration = 3.0f;

	// Maximum plausible jump distance before we snap instead of interpolate.
	// If a herd center moves more than this between sim samples, skip the blend.
	// Set to: maxAnimalSpeed (m/s) × longestLODInterval (s) × safetyMultiplier
	// Example: 8 m/s × 1.0 s × 2.5 = 20 m
	private const float SnapThreshold = 20f;

	/// <summary>
	/// Updates the interpolation state for a herd and returns the smoothed state.
	/// This method implements Tier A interpolation (herd center and yaw) with linear blending.
	/// </summary>
	/// <param name="herd">The herd to update interpolation for</param>
	/// <param name="delta">Time since last frame in seconds</param>
	/// <returns>The updated herd render state</returns>
	private HerdRenderState UpdateHerdRenderState(HerdBrain herd, float delta)
	{
		int id = herd.HerdId;

		if (!_herdRenderStates.TryGetValue(id, out HerdRenderState state))
		{
			// First time we have seen this herd — snap to current sim position, no blend
			state = new HerdRenderState
			{
				RenderCenter       = herd.CenterPosition,
				TargetCenter       = herd.CenterPosition,
				PreviousCenter     = herd.CenterPosition,
				CenterBlendElapsed = 1f,
				CenterBlendDuration= 1f,
				RenderYaw          = GetYawFromDirection(herd.MovementDirection),
				TargetYaw          = GetYawFromDirection(herd.MovementDirection),
				YawBlendElapsed    = 1f,
				YawBlendDuration   = 1f,
				Initialised        = true
			};
			_herdRenderStates[id] = state;
			return state;
		}

		// --- Detect sim center change ---
		if (!IsApproxEqual(state.TargetCenter, herd.CenterPosition))
		{
			float jumpDistance = state.TargetCenter.DistanceTo(herd.CenterPosition);

			if (jumpDistance > SnapThreshold)
			{
				// Large jump: teleport / LOD transition / just entered render range
				// Snap immediately — do not interpolate across half the map
				state.RenderCenter       = herd.CenterPosition;
				state.TargetCenter       = herd.CenterPosition;
				state.PreviousCenter     = herd.CenterPosition;
				state.CenterBlendElapsed = 1f;
				state.CenterBlendDuration= 1f;
			}
			else
			{
				// Normal sim update — start a new blend segment
				state.PreviousCenter     = state.RenderCenter; // start from wherever we currently are
				state.TargetCenter       = herd.CenterPosition;
				state.CenterBlendElapsed = 0f;

				// Use the LOD update interval as the blend duration so we finish
				// exactly as the next sim update is expected to arrive.
				state.CenterBlendDuration = BehaviourLODHelper.GetUpdateInterval(herd.CurrentLOD);
				// Clamp to a sensible range in case LOD interval is very short or very long
				state.CenterBlendDuration = Mathf.Clamp(state.CenterBlendDuration, 0.05f, 1.5f);
			}
		}

		// --- Detect yaw change ---
		float simYaw = GetYawFromDirection(herd.MovementDirection);
		if (!Mathf.IsEqualApprox(state.TargetYaw, simYaw, 0.0001f))
		{
			state.TargetYaw       = simYaw;
			state.YawBlendElapsed = 0f;
			state.YawBlendDuration= state.CenterBlendDuration; // keep in sync with center
		}

		// --- Advance blend timers ---
		state.CenterBlendElapsed = Mathf.Min(state.CenterBlendElapsed + delta, state.CenterBlendDuration);
		state.YawBlendElapsed    = Mathf.Min(state.YawBlendElapsed    + delta, state.YawBlendDuration);

		// --- Compute interpolated values with easing (Gate 2) ---
		float centerT = (state.CenterBlendDuration > 0f)
			? state.CenterBlendElapsed / state.CenterBlendDuration
			: 1f;
		float yawT = (state.YawBlendDuration > 0f)
			? state.YawBlendElapsed / state.YawBlendDuration
			: 1f;

		// Apply ease-out smoothing
		float smoothedCenterT = EaseOut(centerT);
		float smoothedYawT    = EaseOut(yawT);

		state.RenderCenter = state.PreviousCenter.Lerp(state.TargetCenter, smoothedCenterT);
		state.RenderYaw    = LerpAngle(state.RenderYaw, state.TargetYaw, smoothedYawT);

		_herdRenderStates[id] = state;
		return state;
	}

	/// <summary>
	/// Helper to extract yaw angle (in radians) from a direction vector.
	/// Returns 0 if direction is too small.
	/// </summary>
	private float GetYawFromDirection(Vector3 direction)
	{
		if (direction.LengthSquared() < 0.0001f)
			return 0f;
		
		Vector3 horizontal = direction;
		horizontal.Y = 0f;
		return Mathf.Atan2(horizontal.X, horizontal.Z);
	}

	/// <summary>
	/// Helper to check if two vectors are approximately equal.
	/// </summary>
	private bool IsApproxEqual(Vector3 a, Vector3 b)
	{
		return (a - b).LengthSquared() < 0.0001f;
	}

	/// <summary>
	/// Smoothstep ease-out. Input t must be in [0, 1].
	/// Makes motion decelerate into the target rather than arriving linearly.
	/// </summary>
	private static float EaseOut(float t)
	{
		t = Mathf.Clamp(t, 0f, 1f);
		return t * (2f - t); // quadratic ease-out
	}

	/// <summary>
	/// Interpolates between two angles (in radians), taking the shortest arc.
	/// t must be in [0, 1].
	/// </summary>
	private static float LerpAngle(float from, float to, float t)
	{
		float diff = Mathf.Wrap(to - from, -Mathf.Pi, Mathf.Pi);
		return from + diff * t;
	}

	/// <summary>
	/// Updates the interpolation state for an individual animal and returns the smoothed state.
	/// This method implements Tier B interpolation (individual offset smoothing).
	/// </summary>
	/// <param name="animal">The animal to update interpolation for</param>
	/// <param name="delta">Time since last frame in seconds</param>
	/// <returns>The updated animal render state</returns>
	private AnimalRenderState UpdateAnimalRenderState(ref AnimalStruct animal, float delta)
	{
		ulong uid = animal.UniqueId;

		if (!_animalRenderStates.TryGetValue(uid, out AnimalRenderState state))
		{
			// First time we have seen this animal — snap to current sim position, no blend
			state = new AnimalRenderState
			{
				RenderOffset       = animal.WorldPosition,
				TargetOffset       = animal.WorldPosition,
				PreviousOffset     = animal.WorldPosition,
				OffsetBlendElapsed = OffsetShuffleDuration,
				OffsetBlendDuration= OffsetShuffleDuration,
				Initialised        = true
			};
			_animalRenderStates[uid] = state;
			return state;
		}

		// Detect offset change (sim assigned a new spread position)
		if (!IsApproxEqual(state.TargetOffset, animal.WorldPosition))
		{
			state.PreviousOffset     = state.RenderOffset;
			state.TargetOffset       = animal.WorldPosition;
			state.OffsetBlendElapsed = 0f;
			// OffsetBlendDuration is constant — always OffsetShuffleDuration
		}

		// Advance
		state.OffsetBlendElapsed = Mathf.Min(
			state.OffsetBlendElapsed + delta,
			state.OffsetBlendDuration
		);

		float t = state.OffsetBlendElapsed / state.OffsetBlendDuration;
		t = EaseOut(t); // Apply easing for smooth deceleration

		state.RenderOffset = state.PreviousOffset.Lerp(state.TargetOffset, t);

		_animalRenderStates[uid] = state;
		return state;
	}

	public override void _Ready()
	{
		// Initialize terrain heightmap (18KB for 2048m at 4m resolution)
		var gameState = GetNodeOrNull<GameState>("/root/GameState");
		float mapX = gameState?.MapSizeX ?? 2048f;
		float mapZ = gameState?.MapSizeZ ?? 2048f;
		TerrainQuery.Initialize(mapX, mapZ);

		// Extract meshes from GLB PackedScenes and map to species
		TryLoadMesh(KuduScene, Species.Kudu);
		TryLoadMesh(ImpalaScene, Species.Impala);
		TryLoadMesh(BuffaloScene, Species.Buffalo);
		TryLoadMesh(ZebraScene, Species.Zebra);
		TryLoadMesh(WildebeestScene, Species.Wildebeest);
		TryLoadMesh(WaterbuckScene, Species.Waterbuck);

		// Create a MultiMeshInstance3D child for each species that has a mesh
		foreach (var kvp in _speciesMeshes)
		{
			var mmi = new MultiMeshInstance3D();
			mmi.Name = $"MMI_{kvp.Key}";
			AddChild(mmi);
			_meshNodes[kvp.Key] = mmi;
		}

		// Find the player node for distance checks
		_player = GetTree().Root.FindChild("Boer", true, false) as Node3D;

		// Spawn test herds if enabled and AnimalSystem has no herds yet
		if (SpawnTestHerd && AnimalSystem.Instance.Herds.Count == 0)
		{
			float mapX = GameState.Instance?.MapSizeX ?? 2048f;
			float mapZ = GameState.Instance?.MapSizeZ ?? 2048f;
			var rng = new System.Random(TestHerdSeed);
			var allSpecies = new Species[] { Species.Kudu, Species.Zebra, Species.Impala, Species.Buffalo, Species.Wildebeest };

			for (int i = 0; i < allSpecies.Length; i++)
			{
				float rx = (float)(rng.NextDouble() * mapX - mapX * 0.5f);
				float rz = (float)(rng.NextDouble() * mapZ - mapZ * 0.5f);
				float ry = TerrainQuery.GetHeight(rx, rz);
				Vector3 pos = new Vector3(rx, ry + 1f, rz);
				AnimalSystem.Instance.CreateHerd(allSpecies[i], pos, TestHerdSeed + i);
				GD.Print($"[AnimalRenderer] Spawned test {allSpecies[i]} herd at {pos}");
			}
		}

		// Warn about species with herds but no mesh
		foreach (var herd in AnimalSystem.Instance.Herds)
		{
			if (!_speciesMeshes.ContainsKey(herd.Species))
				GD.PrintErr($"[AnimalRenderer] WARNING: {herd.Species} herd exists but no mesh loaded. " +
							$"Assign the .glb file to {herd.Species}Scene in the Inspector.");
		}

		GD.Print($"[AnimalRenderer] Ready. {_speciesMeshes.Count} species meshes loaded.");
	}

	/// <summary>
	/// Extracts the first Mesh found inside a GLB PackedScene.
	/// GLB files are imported as scenes — this instantiates temporarily,
	/// finds the MeshInstance3D, grabs its mesh, and frees the temp node.
	/// </summary>
	private void TryLoadMesh(PackedScene scene, Species species)
	{
		if (scene == null) return;

		Node instance = scene.Instantiate();
		Mesh mesh = FindMeshRecursive(instance);

		if (mesh != null)
		{
			_speciesMeshes[species] = mesh;
			GD.Print($"[AnimalRenderer] Loaded mesh for {species}: {mesh.GetType().Name}");
		}
		else
		{
			GD.PrintErr($"[AnimalRenderer] No MeshInstance3D found in {species} scene.");
		}

		instance.QueueFree();
	}

	/// <summary>
	/// Recursively searches a node tree for the first MeshInstance3D and returns its mesh.
	/// </summary>
	private static Mesh FindMeshRecursive(Node node)
	{
		if (node is MeshInstance3D mi && mi.Mesh != null)
			return mi.Mesh;

		foreach (Node child in node.GetChildren())
		{
			Mesh found = FindMeshRecursive(child);
			if (found != null) return found;
		}

		return null;
	}

	public override void _Process(double delta)
	{
		Vector3 playerPos = _player?.GlobalPosition ?? Vector3.Zero;
		float maxDistSq = MaxRenderDistance * MaxRenderDistance;

		// Update the AnimalSystem simulation
		AnimalSystem.Instance.UpdateFrame((float)delta, playerPos);

		// Collect visible animals per species
		var visibleBySpecies = new Dictionary<Species, List<Transform3D>>();
		foreach (var species in _speciesMeshes.Keys)
		{
			visibleBySpecies[species] = new List<Transform3D>();
		}

		// Track visible herds and animals for cleanup
		var visibleHerdIds = new HashSet<int>();
		var visibleAnimalUids = new HashSet<ulong>();

		// Iterate all herds and gather visible animal transforms
		var herds = AnimalSystem.Instance.Herds;
		int herdCount = herds.Count;

		for (int h = 0; h < herdCount; h++)
		{
			HerdBrain herd = herds[h];

			// Skip species we have no mesh for
			if (!_speciesMeshes.ContainsKey(herd.Species))
			{
				if (!visibleHerdIds.Contains(herd.HerdId))
					GD.Print($"[AnimalRenderer] Skipping {herd.Species} herd #{herd.HerdId}: no mesh loaded. Assign the .glb in Inspector.");
				continue;
			}

			// Distance cull entire herd
			float herdDistSq = herd.CenterPosition.DistanceSquaredTo(playerPos);
			if (herdDistSq > maxDistSq)
				continue;

			// Mark this herd as visible
			visibleHerdIds.Add(herd.HerdId);

			// Add each living animal's world transform with interpolation (Gate 1 - Tier A only)
			var animals = herd.Animals;
			int animalCount = animals.Length;
			var transforms = visibleBySpecies[herd.Species];

			// Get smoothed herd center and yaw for this herd (Tier A interpolation)
			HerdRenderState herdState = UpdateHerdRenderState(herd, (float)delta);

			// Build the shared herd-level basis from smoothed yaw
			Basis herdBasis = Basis.Identity;
			if (herdState.RenderYaw != 0f) // Only apply rotation if yaw is non-zero
			{
				herdBasis = herdBasis.Rotated(Vector3.Up, herdState.RenderYaw);
			}

			// Get terrain-aligned basis once per herd (optimization)
			// Note: Animals in a herd are close enough that sharing a basis is visually indistinguishable
			Basis terrainAlignedBasis = GetTerrainAlignedBasis(herdState.RenderCenter.X, herdState.RenderCenter.Z, herdState.RenderYaw);

			for (int i = 0; i < animalCount; i++)
			{
				if (animals[i].Health <= 0f) continue;

				// Get smoothed individual offset for this animal (Tier B interpolation)
				AnimalRenderState animalState = UpdateAnimalRenderState(ref animals[i], (float)delta);

				// Absolute position = smoothed herd center + smoothed animal offset
				Vector3 worldPos = herdState.RenderCenter + animalState.RenderOffset;

				// Snap Y to terrain from interpolated X/Z position
				worldPos.Y = TerrainQuery.GetHeight(worldPos.X, worldPos.Z);

				// Mark this animal as visible
				visibleAnimalUids.Add(animals[i].UniqueId);

				// Use the precomputed terrain-aligned basis for the herd
				Basis finalBasis = terrainAlignedBasis;

				transforms.Add(new Transform3D(finalBasis, worldPos));
			}
		}

		// Clean up interpolation state for herds and animals that are no longer visible
		CleanupRenderStates(visibleHerdIds, visibleAnimalUids);

		// Update each species' MultiMesh with the collected transforms
		foreach (var kvp in _meshNodes)
		{
			Species species = kvp.Key;
			MultiMeshInstance3D mmi = kvp.Value;
			List<Transform3D> transforms = visibleBySpecies[species];

			if (transforms.Count == 0)
			{
				// Nothing to show — clear the multimesh
				if (mmi.Multimesh != null)
					mmi.Multimesh = null;
				continue;
			}

			var mm = mmi.Multimesh;

			// Only rebuild MultiMesh if instance count changed
			if (mm == null || mm.InstanceCount != transforms.Count)
			{
				mm = new MultiMesh();
				mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
				mm.Mesh = _speciesMeshes[species];
				mm.InstanceCount = transforms.Count;
				mmi.Multimesh = mm;
			}

			// Write transforms
			for (int i = 0; i < transforms.Count; i++)
			{
				mm.SetInstanceTransform(i, transforms[i]);
			}
		}
	}

	/// <summary>
	/// Clean up interpolation state for herds and animals that are no longer visible.
	/// </summary>
	private void CleanupRenderStates(HashSet<int> visibleHerdIds, HashSet<ulong> visibleAnimalUids)
	{
		// Remove herd states for herds no longer visible
		var herdIdsToRemove = new List<int>();
		foreach (var kvp in _herdRenderStates)
		{
			if (!visibleHerdIds.Contains(kvp.Key))
			{
				herdIdsToRemove.Add(kvp.Key);
			}
		}
		foreach (var id in herdIdsToRemove)
		{
			_herdRenderStates.Remove(id);
		}

		// Remove animal states for animals no longer visible
		var animalUidsToRemove = new List<ulong>();
		foreach (var kvp in _animalRenderStates)
		{
			if (!visibleAnimalUids.Contains(kvp.Key))
			{
				animalUidsToRemove.Add(kvp.Key);
			}
		}
		foreach (var uid in animalUidsToRemove)
		{
			_animalRenderStates.Remove(uid);
		}
	}

	/// <summary>
	/// Gets a terrain-aligned basis for the given position and yaw.
	/// Samples the heightmap at small offsets to estimate the terrain normal.
	/// </summary>
	/// <param name="x">World X position</param>
	/// <param name="z">World Z position</param>
	/// <param name="yaw">Yaw angle in radians</param>
	/// <returns>Basis aligned with terrain normal and rotated by yaw</returns>
	private Basis GetTerrainAlignedBasis(float x, float z, float yaw)
	{
		const float sampleOffset = 0.5f; // metres

		float hCenter = TerrainQuery.GetHeight(x, z);
		float hRight  = TerrainQuery.GetHeight(x + sampleOffset, z);
		float hForward= TerrainQuery.GetHeight(x, z + sampleOffset);

		// Reconstruct terrain normal from finite differences
		// IMPORTANT: In Godot's right-handed system, Z cross X gives upward normal
		Vector3 tangentX = new Vector3(sampleOffset, hRight - hCenter, 0f);
		Vector3 tangentZ = new Vector3(0f, hForward - hCenter, sampleOffset);
		Vector3 up = tangentZ.Cross(tangentX).Normalized();

		// Build orthonormal basis aligned to terrain — same pattern as FloraPopulator
		Vector3 right = Vector3.Up.Cross(up);
		if (right.LengthSquared() < 0.001f)
			right = Vector3.Right;
		else
			right = right.Normalized();

		Vector3 forward = up.Cross(right).Normalized();

		// Yaw around terrain-aligned up (not world up) so animals rotate correctly on slopes
		Basis terrainBasis = new Basis(right, up, forward);
		Basis yawBasis     = new Basis(up, yaw);

		return terrainBasis * yawBasis;
	}
}

// ============================================================================
// INTERPOLATION STATE STRUCTURES (Gate 1)
// ============================================================================

/// <summary>
/// Render-side interpolation state for a single herd.
/// </summary>
public struct HerdRenderState
{
	// Tier A — herd center
	public Vector3 RenderCenter;          // what we are currently drawing
	public Vector3 TargetCenter;          // where the sim says the herd is
	public Vector3 PreviousCenter;        // where RenderCenter was when TargetCenter last changed
	public float   CenterBlendElapsed;    // seconds since last target change
	public float   CenterBlendDuration;   // how long to take to reach TargetCenter

	// Tier A — movement direction / yaw
	public float   RenderYaw;            // current rendered heading (radians)
	public float   TargetYaw;
	public float   YawBlendElapsed;
	public float   YawBlendDuration;

	// Validity
	public bool    Initialised;          // false until first sim sample received
}

/// <summary>
/// Render-side interpolation state for a single animal.
/// </summary>
public struct AnimalRenderState
{
	// Tier B — individual offset from herd center
	public Vector3 RenderOffset;         // current rendered offset
	public Vector3 TargetOffset;         // what the sim most recently assigned
	public Vector3 PreviousOffset;       // offset at the time the target last changed
	public float   OffsetBlendElapsed;
	public float   OffsetBlendDuration;  // constant: use 0.35f as the default

	public bool    Initialised;
}
