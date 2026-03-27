using Godot;
using System.Collections.Generic;
using LandManagementSim.Simulation;

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

	public override void _Ready()
	{
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
			// Herd 1: Kudu
			AnimalSystem.Instance.CreateHerd(Species.Kudu, TestHerdPosition, TestHerdSeed);
			GD.Print($"[AnimalRenderer] Spawned test Kudu herd at {TestHerdPosition}");

			// Herd 2: Zebra (offset by 40m)
			Vector3 zebraPos = TestHerdPosition + new Vector3(40f, 0f, 40f);
			AnimalSystem.Instance.CreateHerd(Species.Zebra, zebraPos, TestHerdSeed + 1);
			GD.Print($"[AnimalRenderer] Spawned test Zebra herd at {zebraPos}");
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

		// Iterate all herds and gather visible animal transforms
		var herds = AnimalSystem.Instance.Herds;
		int herdCount = herds.Count;

		for (int h = 0; h < herdCount; h++)
		{
			HerdBrain herd = herds[h];

			// Skip species we have no mesh for
			if (!_speciesMeshes.ContainsKey(herd.Species))
				continue;

			// Distance cull entire herd
			float herdDistSq = herd.CenterPosition.DistanceSquaredTo(playerPos);
			if (herdDistSq > maxDistSq)
				continue;

			// Add each living animal's world transform
			var animals = herd.Animals;
			int animalCount = animals.Length;
			var transforms = visibleBySpecies[herd.Species];

			for (int i = 0; i < animalCount; i++)
			{
				if (animals[i].Health <= 0f) continue;

				// Absolute position = herd center + animal offset
				Vector3 worldPos = herd.CenterPosition + animals[i].WorldPosition;

				// Face the herd's movement direction (or forward if stationary)
				Basis basis = Basis.Identity;
				if (herd.MovementDirection.LengthSquared() > 0.01f)
				{
					Vector3 forward = herd.MovementDirection;
					forward.Y = 0f;
					float angle = Mathf.Atan2(forward.X, forward.Z);
					basis = basis.Rotated(Vector3.Up, angle);
				}

				transforms.Add(new Transform3D(basis, worldPos));
			}
		}

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
}
