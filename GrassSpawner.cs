using Godot;

/// <summary>
/// GrassSpawner — cluster-distributed grass using MultiMeshInstance3D.
///
/// PERFORMANCE FIXES vs previous version:
///   • VisibilityRangeEnd set on the MMI node — blades beyond LodFadeEnd are not rendered at all.
///   • Per-instance colours stored in _colours[] — SetDrought() never re-runs the position RNG.
///   • Raycasts run in a deferred call after the physics world is ready, preventing main-thread hitches.
///
/// DISTRIBUTION:
///   • Cluster-based sampling. N cluster centres are scattered across the patch.
///   • Each blade is placed near a random cluster using Gaussian falloff (Box-Muller).
///   • ClusterRadius controls tightness. ClusterStrength blends between clustered and uniform.
///   • Result: dense tufts with open bare ground between them — realistic African veld.
/// </summary>
public partial class GrassSpawner : Node3D
{
	// ── Inspector ───────────────────────────────────────────────────────────

	[Export] public MultiMeshInstance3D MultiMeshNode;
	[Export] public Mesh GrassMesh;

	[ExportGroup("Patch shape")]
	[Export] public Vector2 PatchSize     = new(40f, 40f);
	[Export] public int     InstanceCount = 800;
	[Export] public float   Density       = 1.0f;           // 0–1: overall thinning

	[ExportGroup("Cluster distribution")]
	[Export] public int   ClusterCount    = 7;              // Number of dense tufts in the patch
	[Export] public float ClusterRadius   = 3.5f;           // Std-dev of Gaussian spread (metres)
	[Export(PropertyHint.Range, "0,1")]
	public float ClusterStrength          = 0.85f;          // 0 = uniform random, 1 = fully clustered
	[Export] public bool  EdgeFalloff     = true;           // Thin out blades near patch boundary

	[ExportGroup("Blade variation")]
	[Export(PropertyHint.Range, "0,1")] public float ScaleVariation  = 0.4f;
	[Export(PropertyHint.Range, "0,1")] public float HeightVariation = 0.5f;
	[Export(PropertyHint.Range, "0,1")] public float LeanVariation   = 0.25f;
	[Export] public float MinScale = 0.4f;
	[Export] public float MaxScale = 1.6f;

	[ExportGroup("Colour variation")]
	[Export] public Color BaseColour    = new(0.42f, 0.52f, 0.18f);
	[Export] public Color DroughtTint   = new(0.68f, 0.55f, 0.18f);
	[Export(PropertyHint.Range, "0,1")] public float DroughtAmount   = 0.0f;
	[Export(PropertyHint.Range, "0,1")] public float ColourVariation = 0.3f;

	[ExportGroup("Terrain snap")]
	[Export] public bool  SnapToTerrain = true;
	[Export] public float RaycastFrom   = 10f;
	[Export] public uint  TerrainLayer  = 1;

	[ExportGroup("LOD / Performance")]
	[Export] public float LodFadeStart  = 35f;   // Blades begin to thin at this distance from camera
	[Export] public float LodFadeEnd    = 65f;   // Fully invisible beyond this — set to match chunk unload range

	[ExportGroup("Seed")]
	[Export] public int Seed = 1337;

	// ── Private ─────────────────────────────────────────────────────────────

	private RandomNumberGenerator _rng = new();
	private Vector2[]             _clusterCentres;
	private Color[]               _colours;          // Stored separately — drought update never touches positions

	// ── Lifecycle ───────────────────────────────────────────────────────────

	public override void _Ready()
	{
		if (MultiMeshNode == null || GrassMesh == null)
		{
			GD.PrintErr("GrassSpawner: assign MultiMeshNode and GrassMesh in the Inspector.");
			return;
		}

		// Apply LOD distances directly to the node.
		// Without this, Godot renders every instance at every distance — the #1 framerate killer.
		MultiMeshNode.VisibilityRangeEnd      = LodFadeEnd;
		MultiMeshNode.VisibilityRangeBegin    = 0f;
		MultiMeshNode.VisibilityRangeEndMargin = LodFadeEnd - LodFadeStart;

		// Generate cluster centres before spawning
		GenerateClusterCentres();

		if (SnapToTerrain)
			// Defer so physics world is initialised — avoids "server not active" errors on load
			CallDeferred(MethodName.Spawn);
		else
			Spawn();
	}

	// ── Public API ──────────────────────────────────────────────────────────

	/// <summary>Full rebuild. Call when a chunk loads or density changes.</summary>
	public void Spawn()
	{
		_rng.Seed = (ulong)Seed;

		// Pre-build cluster centres with this seed (so they're consistent)
		GenerateClusterCentres();
		
		var mm = new MultiMesh
		{
			Mesh            = GrassMesh,
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			UseColors       = true,
			UseCustomData   = false,
			InstanceCount   = InstanceCount
		};
		_colours = new Color[InstanceCount];

		int placed = 0;

		for (int i = 0; i < InstanceCount; i++)
		{
			if (_rng.Randf() > Density)
			{
				mm.SetInstanceTransform(i, ZeroTransform());
				mm.SetInstanceColor(i, Colors.Transparent);
				_colours[i] = Colors.Transparent;
				continue;
			}

			Vector3 pos = SampleClusteredPosition();

			// Edge falloff: thin the patch near the boundary for a natural fringe
			if (EdgeFalloff && !PassesEdgeFalloff(pos))
			{
				mm.SetInstanceTransform(i, ZeroTransform());
				mm.SetInstanceColor(i, Colors.Transparent);
				_colours[i] = Colors.Transparent;
				continue;
			}

			if (SnapToTerrain)
				pos = SnapToGround(pos);

			Color c = BuildColour();
			_colours[i] = c;

			mm.SetInstanceTransform(i, BuildTransform(pos));
			mm.SetInstanceColor(i, c);
			placed++;
		}

		MultiMeshNode.Multimesh = mm;
		GD.Print($"GrassSpawner [{Name}]: {placed}/{InstanceCount} blades. " +
				 $"Clusters={ClusterCount}, Radius={ClusterRadius}, Seed={Seed}");
	}

	/// <summary>
	/// Update drought tint without rebuilding geometry.
	/// Runs in O(N) colour updates only — does NOT re-run position RNG.
	/// Call from EcologySystem when grass health changes.
	/// </summary>
	public void SetDrought(float amount)
	{
		DroughtAmount = Mathf.Clamp(amount, 0f, 1f);

		var mm = MultiMeshNode?.Multimesh;
		if (mm == null || _colours == null) return;

		// We only need a fresh RNG for per-blade colour noise.
		// Positions are untouched — stored in the transform, not re-calculated.
		var colourRng = new RandomNumberGenerator();
		colourRng.Seed = (ulong)(Seed + 999); // Different sub-seed so noise differs from position noise

		for (int i = 0; i < mm.InstanceCount; i++)
		{
			if (_colours[i] == Colors.Transparent) continue;

			Color tinted  = BaseColour.Lerp(DroughtTint, DroughtAmount);
			float noise   = (colourRng.Randf() - 0.5f) * ColourVariation;
			tinted.R = Mathf.Clamp(tinted.R + noise * 0.3f, 0f, 1f);
			tinted.G = Mathf.Clamp(tinted.G + noise * 0.5f, 0f, 1f);
			tinted.B = Mathf.Clamp(tinted.B + noise * 0.1f, 0f, 1f);

			_colours[i] = tinted;
			mm.SetInstanceColor(i, tinted);
		}
	}

	/// <summary>
	/// Thin the patch — simulates overgrazing or seasonal die-off.
	/// density 1.0 = full, 0.0 = bare ground.
	/// Triggers a full Spawn() because density affects transform layout.
	/// </summary>
	public void SetDensity(float density)
	{
		Density = Mathf.Clamp(density, 0f, 1f);
		Spawn();
	}

	/// <summary>
	/// Regenerate cluster centres with a new seed — useful when a neighbouring
	/// spawner uses the same patch size and you want visual variety.
	/// </summary>
	public void Reseed(int newSeed)
	{
		Seed = newSeed;
		GenerateClusterCentres();
		Spawn();
	}

	// ── Cluster helpers ─────────────────────────────────────────────────────

	private void GenerateClusterCentres()
	{
		// Use a separate sub-seed so cluster positions are stable even if blade seed changes
		var clusterRng = new RandomNumberGenerator();
		clusterRng.Seed = (ulong)(Seed * 7 + 13);

		_clusterCentres = new Vector2[Mathf.Max(1, ClusterCount)];

		for (int i = 0; i < _clusterCentres.Length; i++)
		{
			float x = (clusterRng.Randf() - 0.5f) * PatchSize.X * 0.85f; // Keep centres inside patch
			float z = (clusterRng.Randf() - 0.5f) * PatchSize.Y * 0.85f;
			_clusterCentres[i] = new Vector2(x, z);
		}
	}

	private Vector3 SampleClusteredPosition()
	{
		Vector2 localPos;

		if (_rng.Randf() < ClusterStrength)
		{
			// Clustered: pick a random centre, then Gaussian offset (Box-Muller transform)
			int    idx    = _rng.RandiRange(0, _clusterCentres.Length - 1);
			Vector2 ctr   = _clusterCentres[idx];

			// Box-Muller: two uniform randoms → one Gaussian sample
			float u1    = Mathf.Max(_rng.Randf(), 0.00001f); // Avoid log(0)
			float u2    = _rng.Randf();
			float gauss = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(Mathf.Tau * u2);

			float x = Mathf.Clamp(ctr.X + gauss * ClusterRadius, -PatchSize.X * 0.5f, PatchSize.X * 0.5f);

			// Second Gaussian sample for Z (reuse u1, shift u2 phase)
			float gauss2 = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(Mathf.Tau * u2);
			float z = Mathf.Clamp(ctr.Y + gauss2 * ClusterRadius, -PatchSize.Y * 0.5f, PatchSize.Y * 0.5f);

			localPos = new Vector2(x, z);
		}
		else
		{
			// Uniform fallback: scattered blades in the open areas between clusters
			localPos = new Vector2(
				(_rng.Randf() - 0.5f) * PatchSize.X,
				(_rng.Randf() - 0.5f) * PatchSize.Y
			);
		}

		return GlobalPosition + new Vector3(localPos.X, 0f, localPos.Y);
	}

	/// <summary>
	/// Edge falloff: blades near the boundary of the patch are probabilistically culled.
	/// Creates a natural fringe rather than a hard geometric edge.
	/// </summary>
	private bool PassesEdgeFalloff(Vector3 worldPos)
	{
		Vector3 local = worldPos - GlobalPosition;
		float   nx    = Mathf.Abs(local.X) / (PatchSize.X * 0.5f); // 0 = centre, 1 = edge
		float   nz    = Mathf.Abs(local.Z) / (PatchSize.Y * 0.5f);
		float   edge  = Mathf.Max(nx, nz);

		if (edge < 0.7f) return true;                        // Well inside — always pass
		float falloffChance = Mathf.InverseLerp(0.7f, 1.0f, edge); // 0 at 70%, 1 at 100%
		return _rng.Randf() > falloffChance;
	}

	// ── Transform / colour helpers ──────────────────────────────────────────

	private Vector3 SnapToGround(Vector3 pos)
	{
		var spaceState = GetWorld3D().DirectSpaceState;
		var origin     = pos + Vector3.Up * RaycastFrom;
		var target     = pos + Vector3.Down * RaycastFrom;
		var query      = PhysicsRayQueryParameters3D.Create(origin, target, TerrainLayer);
		var result     = spaceState.IntersectRay(query);
		return result.Count > 0 ? (Vector3)result["position"] : pos;
	}

	private Transform3D BuildTransform(Vector3 pos)
	{
		float uniformScale = Mathf.Clamp(
			1.0f + (_rng.Randf() - 0.5f) * ScaleVariation * 2f,
			MinScale, MaxScale);
		float heightScale = Mathf.Clamp(
			1.0f + (_rng.Randf() - 0.5f) * HeightVariation * 2f,
			0.3f, 2.0f);

		float yRot      = _rng.Randf() * Mathf.Tau;
		float leanAmount = (_rng.Randf() - 0.5f) * LeanVariation * Mathf.Pi * 0.5f;

		var basis = Basis.Identity;
		basis = basis.Rotated(Vector3.Up, yRot);
		basis = basis.Rotated(basis.X, leanAmount);
		basis.X *= uniformScale;
		basis.Z *= uniformScale;
		basis.Y *= uniformScale * heightScale;

		return new Transform3D(basis, pos);
	}

	private Color BuildColour()
	{
		Color tinted = BaseColour.Lerp(DroughtTint, DroughtAmount);
		float noise  = (_rng.Randf() - 0.5f) * ColourVariation;
		tinted.R = Mathf.Clamp(tinted.R + noise * 0.3f, 0f, 1f);
		tinted.G = Mathf.Clamp(tinted.G + noise * 0.5f, 0f, 1f);
		tinted.B = Mathf.Clamp(tinted.B + noise * 0.1f, 0f, 1f);
		return tinted;
	}

	private static Transform3D ZeroTransform() =>
		new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero);
}
