using Godot;

/// <summary>
/// GrassSpawner — places a single grass mesh as thousands of instances.
/// Attach to a Node3D in your scene. Assign a MultiMeshInstance3D and a grass Mesh in the Inspector.
///
/// Every visual variation (size, height, lean, colour) is per-instance data — zero extra draw calls.
/// The seed makes the field identical every time the chunk loads.
/// </summary>
public partial class GrassSpawner : Node3D
{
	// ── Inspector fields ────────────────────────────────────────────────────

	[Export] public MultiMeshInstance3D MultiMeshNode;   // Drag your MultiMeshInstance3D here
	[Export] public Mesh GrassMesh;                      // Drag your grass .glb mesh here

	[ExportGroup("Patch shape")]
	[Export] public Vector2 PatchSize   = new(40f, 40f); // Width/depth of the grass area in metres
	[Export] public int     InstanceCount = 1000;         // Total blades in the patch
	[Export] public float   Density     = 1.0f;          // 0–1: thins the patch (random culling)

	[ExportGroup("Blade variation")]
	[Export(PropertyHint.Range, "0,1")] public float ScaleVariation  = 0.4f;  // Overall size spread
	[Export(PropertyHint.Range, "0,1")] public float HeightVariation = 0.5f;  // Y-stretch spread
	[Export(PropertyHint.Range, "0,1")] public float LeanVariation   = 0.25f; // How much blades tilt
	[Export] public float MinScale = 0.4f;                                    // Never smaller than this
	[Export] public float MaxScale = 1.6f;                                    // Never larger than this

	[ExportGroup("Colour variation")]
	[Export] public Color BaseColour   = new(0.42f, 0.52f, 0.18f); // Healthy green
	[Export] public Color DroughtTint  = new(0.68f, 0.55f, 0.18f); // Dry/yellow — lerp target
	[Export(PropertyHint.Range, "0,1")] public float DroughtAmount = 0.0f;   // 0 = lush, 1 = parched
	[Export(PropertyHint.Range, "0,1")] public float ColourVariation = 0.3f; // Per-blade colour noise

	[ExportGroup("Terrain")]
	[Export] public bool SnapToTerrain  = true;   // Raycast each blade down onto terrain
	[Export] public float RaycastFrom   = 10f;    // Height above ground to cast from
	[Export] public uint  TerrainLayer  = 1;      // Physics layer your terrain is on

	[ExportGroup("Seed")]
	[Export] public int Seed = 1337; // Change to get a different field. Same seed = identical result.

	// ── Private ────────────────────────────────────────────────────────────

	private RandomNumberGenerator _rng;

	// ── Lifecycle ──────────────────────────────────────────────────────────

	public override void _Ready()
	{
		if (MultiMeshNode == null || GrassMesh == null)
		{
			GD.PrintErr("GrassSpawner: assign MultiMeshNode and GrassMesh in the Inspector.");
			return;
		}

		Spawn();
	}

	// ── Public API — call these from FloraSystem or chunk loader ───────────

	/// <summary>Rebuild the entire patch. Call when chunk loads or season changes.</summary>
	public void Spawn()
	{
		_rng = new RandomNumberGenerator();
		_rng.Seed = (ulong)Seed;

		var mm = new MultiMesh();
		mm.Mesh             = GrassMesh;
		mm.TransformFormat  = MultiMesh.TransformFormatEnum.Transform3D;
		mm.UseColors        = true;   // Per-instance colour tint
		mm.UseCustomData    = false;  // Reserve for future wind phase offset if needed
		mm.InstanceCount    = InstanceCount;

		int placed = 0;

		for (int i = 0; i < InstanceCount; i++)
		{
			// Density culling — skip this blade at random based on density setting
			if (_rng.Randf() > Density)
			{
				// Set a zero-scale transform so the slot exists but renders nothing
				mm.SetInstanceTransform(i, ZeroTransform());
				mm.SetInstanceColor(i, Colors.Transparent);
				continue;
			}

			Vector3 pos = SamplePosition();

			if (SnapToTerrain)
				pos = SnapToGround(pos);

			Transform3D t = BuildTransform(pos);
			Color        c = BuildColour();

			mm.SetInstanceTransform(i, t);
			mm.SetInstanceColor(i, c);
			placed++;
		}

		MultiMeshNode.Multimesh = mm;
		GD.Print($"GrassSpawner: placed {placed}/{InstanceCount} blades. Seed={Seed}");
	}

	/// <summary>
	/// Update drought tint without rebuilding geometry.
	/// Call from EcologySystem when grass health changes.
	/// </summary>
	public void SetDrought(float amount)
	{
		DroughtAmount = Mathf.Clamp(amount, 0f, 1f);

		var mm = MultiMeshNode.Multimesh;
		if (mm == null) return;

		_rng = new RandomNumberGenerator();
		_rng.Seed = (ulong)Seed; // Same seed = same noise pattern, only tint changes

		for (int i = 0; i < mm.InstanceCount; i++)
		{
			_rng.Randf(); // consume position randoms to stay in sync
			_rng.Randf();
			mm.SetInstanceColor(i, BuildColour());
		}
	}

	/// <summary>
	/// Thin the patch to simulate overgrazing or seasonal die-off.
	/// density 1.0 = full, 0.0 = bare ground.
	/// </summary>
	public void SetDensity(float density)
	{
		Density = Mathf.Clamp(density, 0f, 1f);
		Spawn(); // Rebuild — density changes transform layout, not just colour
	}

	// ── Private helpers ────────────────────────────────────────────────────

	private Vector3 SamplePosition()
	{
		float x = (_rng.Randf() - 0.5f) * PatchSize.X;
		float z = (_rng.Randf() - 0.5f) * PatchSize.Y;
		return GlobalPosition + new Vector3(x, 0f, z);
	}

	private Vector3 SnapToGround(Vector3 pos)
	{
		var spaceState = GetWorld3D().DirectSpaceState;
		var origin     = pos + Vector3.Up * RaycastFrom;
		var target     = pos + Vector3.Down * RaycastFrom;

		var query = PhysicsRayQueryParameters3D.Create(origin, target, TerrainLayer);
		var result = spaceState.IntersectRay(query);

		if (result.Count > 0)
			return (Vector3)result["position"];

		return pos; // Fallback if raycast misses
	}

	private Transform3D BuildTransform(Vector3 pos)
	{
		// Overall scale — uniform XZ, Y stretched separately for height variation
		float uniformScale = Mathf.Clamp(
			1.0f + (_rng.Randf() - 0.5f) * ScaleVariation * 2f,
			MinScale, MaxScale
		);
		float heightScale = Mathf.Clamp(
			1.0f + (_rng.Randf() - 0.5f) * HeightVariation * 2f,
			0.3f, 2.0f
		);

		// Random Y rotation so blades face all directions
		float yRot = _rng.Randf() * Mathf.Tau;

		// Lean — tilt on X axis, direction driven by Y rotation for natural look
		float leanAmount = (_rng.Randf() - 0.5f) * LeanVariation * Mathf.Pi * 0.5f;

		var basis = Basis.Identity;
		basis = basis.Rotated(Vector3.Up, yRot);
		basis = basis.Rotated(basis.X, leanAmount);

		// Apply scale: uniform on XZ, height on Y
		basis.X *= uniformScale;
		basis.Z *= uniformScale;
		basis.Y *= uniformScale * heightScale;

		return new Transform3D(basis, pos);
	}

	private Color BuildColour()
	{
		// Lerp between healthy green and drought yellow, then add per-blade noise
		Color tinted = BaseColour.Lerp(DroughtTint, DroughtAmount);

		float noise = (_rng.Randf() - 0.5f) * ColourVariation;
		tinted.R = Mathf.Clamp(tinted.R + noise * 0.3f, 0f, 1f);
		tinted.G = Mathf.Clamp(tinted.G + noise * 0.5f, 0f, 1f);
		tinted.B = Mathf.Clamp(tinted.B + noise * 0.1f, 0f, 1f);

		return tinted;
	}

	private static Transform3D ZeroTransform()
	{
		// A transform scaled to zero — occupies a slot but draws nothing
		return new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero);
	}
}
