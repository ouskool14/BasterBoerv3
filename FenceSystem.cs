using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using WorldStreaming;

public partial class FenceSystem : Node3D
{
	[ExportCategory("Fence Dimensions")]
	[Export] public float MapInsetPercent = 0.95f;
	[Export] public float BaseStep = 3.0f;
	[Export] public int MinSticks = 4;
	[Export] public int MaxSticks = 6;
	[Export] public float[] WireHeights = new float[] { 0.3f, 0.6f, 0.9f };

	[ExportCategory("Assets — assign .glb files directly")]
	/// <summary>Assign the .glb file containing your fence pole mesh.</summary>
	[Export] public PackedScene PoleMeshScene;
	/// <summary>Assign the .glb file containing your dropper/stick mesh.</summary>
	[Export] public PackedScene StickMeshScene;
	/// <summary>Assign the .glb file containing your barbed wire segment mesh.</summary>
	[Export] public PackedScene WireMeshScene;

	// Resolved at runtime from the packed scenes above
	public Mesh PoleMesh { get; private set; }
	public Mesh StickMesh { get; private set; }
	public Mesh WireMesh { get; private set; }

	[ExportCategory("Chunk Settings")]
	[Export] public float ChunkSize = 256f;

	[ExportCategory("Debug")]
	/// <summary>Keep true during development so fence chunks are always visible regardless of streamer state.</summary>
	[Export] public bool DebugForceVisible = true;
	[Export] public NodePath DebugPlayerPath; // Optional: assign your player node to enable F9 teleport

	private static FenceSystem _instance;
	public static FenceSystem Instance => _instance;

	private Thread _generationThread;

	private Dictionary<ChunkCoord, FenceSegmentData> _chunkFenceData = new();
	private Dictionary<ChunkCoord, Node3D> _chunkNodes = new();

	private float _mapX;
	private float _mapZ;
	private int _worldSeed;

	private float _invChunkSize;

	private StandardMaterial3D _poleMat;
	private StandardMaterial3D _stickMat;
	private StandardMaterial3D _wireMat;

	public override void _Ready()
	{
		GD.Print("[FenceSystem] _Ready() called.");

		if (_instance != null && _instance != this)
		{
			GD.PrintErr("[FenceSystem] Duplicate instance detected — this node will be freed.");
			QueueFree();
			return;
		}
		_instance = this;

		// Defer initialization so ALL other nodes (including GameState) have
		// had their _Ready() called first, regardless of scene tree order.
		CallDeferred(MethodName.Initialize);
	}

	private void Initialize()
	{
		GD.Print("[FenceSystem] Initialize() called (deferred, all nodes are ready).");

		// --- GameState check (with fallback defaults) ---
		var gameState = GameState.Instance;
		if (gameState == null)
		{
			GD.PrintErr("[FenceSystem] WARNING: GameState.Instance is still null after deferred init. " +
						"GameState node may not be in the scene. " +
						"Using default map size 4000x4000 and seed 12345.");
			_mapX = 4000f;
			_mapZ = 4000f;
			_worldSeed = 12345;
		}
		else
		{
			_mapX = gameState.MapSizeX;
			_mapZ = gameState.MapSizeZ;
			_worldSeed = gameState.WorldSeed;
			GD.Print($"[FenceSystem] GameState found. MapSize=({_mapX}, {_mapZ}) Seed={_worldSeed}");
		}

		_invChunkSize = 1.0f / ChunkSize;

		// --- Inspector assignment check ---
		GD.Print($"[FenceSystem] Scene slots: Pole={PoleMeshScene?.ResourcePath ?? "NULL"}, " +
				 $"Stick={StickMeshScene?.ResourcePath ?? "NULL"}, " +
				 $"Wire={WireMeshScene?.ResourcePath ?? "NULL"}");

		// --- Materials ---
		_poleMat  = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.25f, 0.15f), Roughness = 0.8f };
		_stickMat = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.35f, 0.25f), Roughness = 0.9f };
		_wireMat  = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.2f, 0.2f), Metallic = 0.8f, Roughness = 0.4f };

		// --- Mesh extraction ---
		PoleMesh  = ExtractMeshFromScene(PoleMeshScene,  "PoleMesh");
		StickMesh = ExtractMeshFromScene(StickMeshScene, "StickMesh");
		WireMesh  = ExtractMeshFromScene(WireMeshScene,  "WireMesh");

		GD.Print($"[FenceSystem] Meshes ready: Pole={PoleMesh != null}, Stick={StickMesh != null}, Wire={WireMesh != null}");

		if (PoleMesh  != null) ApplyMaterialToMesh(PoleMesh,  _poleMat);
		if (StickMesh != null) ApplyMaterialToMesh(StickMesh, _stickMat);
		if (WireMesh  != null) ApplyMaterialToMesh(WireMesh,  _wireMat);

		if (PoleMesh == null && StickMesh == null && WireMesh == null)
		{
			GD.PrintErr("[FenceSystem] WARNING: All three meshes are null. " +
						"Fence data will generate but NO geometry will be visible.");
		}

		_generationThread = new Thread(GenerateFenceDataThreaded);
		_generationThread.IsBackground = true;
		_generationThread.Start();
		GD.Print("[FenceSystem] Background generation thread started.");
	}

	/// <summary>
	/// Instantiates a PackedScene (.glb), locates the first MeshInstance3D descendant,
	/// extracts its mesh, and re-generates tangents so shaders work correctly.
	/// </summary>
	private Mesh ExtractMeshFromScene(PackedScene scene, string label)
	{
		if (scene == null)
		{
			GD.PrintErr($"[FenceSystem] {label}: PackedScene slot is empty in the Inspector.");
			return null;
		}

		Node root = scene.Instantiate<Node>();
		if (root == null)
		{
			GD.PrintErr($"[FenceSystem] {label}: Instantiate() returned null.");
			return null;
		}

		// Print the full node hierarchy so we can see what's inside the .glb
		GD.Print($"[FenceSystem] {label} GLB hierarchy:");
		PrintNodeTree(root, "  ");

		MeshInstance3D mi = FindFirstMeshInstance(root);
		if (mi == null)
		{
			GD.PrintErr($"[FenceSystem] {label}: Found no MeshInstance3D node inside the PackedScene. " +
						"Check the hierarchy printed above.");
			root.Free();
			return null;
		}
		if (mi.Mesh == null)
		{
			GD.PrintErr($"[FenceSystem] {label}: MeshInstance3D '{mi.Name}' has no Mesh resource assigned.");
			root.Free();
			return null;
		}

		GD.Print($"[FenceSystem] {label}: using MeshInstance3D '{mi.Name}', surfaces={mi.Mesh.GetSurfaceCount()}");

		// Re-build every surface with tangents
		Mesh sourceMesh = mi.Mesh;
		ArrayMesh withTangents = null;

		for (int s = 0; s < sourceMesh.GetSurfaceCount(); s++)
		{
			var st = new SurfaceTool();
			st.CreateFrom(sourceMesh, s);
			st.GenerateTangents();
			if (s == 0)
				withTangents = st.Commit();
			else
				st.Commit(withTangents);
		}

		root.Free();

		if (withTangents == null)
		{
			GD.PrintErr($"[FenceSystem] {label}: SurfaceTool produced null mesh (source had 0 surfaces?).");
			return null;
		}

		GD.Print($"[FenceSystem] {label}: tangents generated OK. Final surfaces={withTangents.GetSurfaceCount()}");
		return withTangents;
	}

	private void PrintNodeTree(Node node, string indent)
	{
		GD.Print($"{indent}{node.GetType().Name}: '{node.Name}'");
		foreach (Node child in node.GetChildren())
			PrintNodeTree(child, indent + "  ");
	}

	/// <summary>Depth-first search for the first MeshInstance3D in a node tree.</summary>
	private MeshInstance3D FindFirstMeshInstance(Node node)
	{
		if (node is MeshInstance3D mi)
			return mi;
		foreach (Node child in node.GetChildren())
		{
			var found = FindFirstMeshInstance(child);
			if (found != null) return found;
		}
		return null;
	}

	/// <summary>
	/// Dynamic visibility control for world streaming.
	/// Called by WorldChunkStreamer when active chunks update.
	/// </summary>
	public void UpdateVisibleChunks(IEnumerable<ChunkCoord> visibleCoords)
	{
		var visibleSet = new HashSet<ChunkCoord>(visibleCoords);

		foreach (var kvp in _chunkNodes)
		{
			kvp.Value.Visible = visibleSet.Contains(kvp.Key);
		}
	}

	private void ApplyMaterialToMesh(Mesh mesh, Material mat)
	{
		// Force standard material on all surfaces
		for (int i = 0; i < mesh.GetSurfaceCount(); i++)
		{
			mesh.SurfaceSetMaterial(i, mat);
		}
	}

	private void GenerateFenceDataThreaded()
	{
		var localData = new Dictionary<ChunkCoord, FenceSegmentData>();
		System.Random rng = new System.Random(_worldSeed);

		float insetX = _mapX * MapInsetPercent;
		float insetZ = _mapZ * MapInsetPercent;
		float halfX = insetX / 2f;
		float halfZ = insetZ / 2f;

		GD.Print($"[FenceSystem] Generating fence perimeter. " +
				 $"Map=({_mapX},{_mapZ}), Inset={MapInsetPercent*100:F0}%, " +
				 $"Fence corners at ±({halfX:F0}, {halfZ:F0})");

		Vector3[] corners = {
			new Vector3(-halfX, 0, -halfZ),
			new Vector3( halfX, 0, -halfZ),
			new Vector3( halfX, 0,  halfZ),
			new Vector3(-halfX, 0,  halfZ)
		};

		for (int i = 0; i < 4; i++)
			GenerateEdge(corners[i], corners[(i + 1) % 4], rng, localData);

		int totalPoles = 0, totalSticks = 0, totalWires = 0;
		foreach (var seg in localData.Values)
		{
			totalPoles  += seg.Poles.Count;
			totalSticks += seg.Sticks.Count;
			totalWires  += seg.Wires.Count;
		}
		GD.Print($"[FenceSystem] Generation done: {localData.Count} chunks, " +
				 $"{totalPoles} poles, {totalSticks} sticks, {totalWires} wire segments.");

		_chunkFenceData = localData;
		CallDeferred(MethodName.BuildVisuals);
	}

	private void GenerateEdge(Vector3 edgeStart, Vector3 edgeEnd, Random rng,
		Dictionary<ChunkCoord, FenceSegmentData> data)
	{
		Vector3 direction = (edgeEnd - edgeStart).Normalized();
		float totalLength = edgeStart.DistanceTo(edgeEnd);
		float currentDist = 0f;

		Vector3? previousPost = null;

		while (currentDist < totalLength)
		{
			bool isLastStep = (currentDist + BaseStep > totalLength);
			
			// If it's the very start or the very end of an edge, place a Pole
			// Otherwise randomness determines if it's a dropper or a pole occasionally
			bool forcePole = (currentDist == 0f || isLastStep);
			
			Vector3 flatPos = edgeStart + direction * currentDist;
			float y = GetTerrainHeight(flatPos.X, flatPos.Z);
			Vector3 pos = new Vector3(flatPos.X, y, flatPos.Z);

			var chunkId = ChunkCoord.FromWorldPosition(pos, ChunkSize);

			if (!data.TryGetValue(chunkId, out var segment))
			{
				segment = new FenceSegmentData(chunkId);
				data[chunkId] = segment;
			}

			if (forcePole) segment.Poles.Add(pos);
			else segment.Sticks.Add(pos);

			if (previousPost.HasValue)
			{
				GenerateWires(previousPost.Value, pos, segment);
			}

			previousPost = pos;
			
			if (isLastStep) break;
			
			// If we just placed a pole, we usually have several sticks before the next pole
			if (forcePole)
			{
				int stickCount = rng.Next(MinSticks, MaxSticks + 1);
				float segmentLength = BaseStep * (stickCount + 1);
				float actualStep = BaseStep;
				
				// Ensure we don't overshoot
				if (currentDist + segmentLength > totalLength)
				{
					segmentLength = totalLength - currentDist;
					actualStep = segmentLength / (stickCount + 1);
				}

				for (int j = 1; j <= stickCount; j++)
				{
					float dropperDist = currentDist + (j * actualStep);
					Vector3 dFlatPos = edgeStart + direction * dropperDist;
					float dy = GetTerrainHeight(dFlatPos.X, dFlatPos.Z);
					Vector3 dPos = new Vector3(dFlatPos.X, dy, dFlatPos.Z);
					
					var dChunkId = ChunkCoord.FromWorldPosition(dPos, ChunkSize);
					if (!data.TryGetValue(dChunkId, out var dSegment))
					{
						dSegment = new FenceSegmentData(dChunkId);
						data[dChunkId] = dSegment;
					}

					dSegment.Sticks.Add(dPos);
					GenerateWires(previousPost.Value, dPos, dSegment);
					previousPost = dPos;
				}
				
				currentDist += segmentLength;
			}
			else
			{
				currentDist += BaseStep;
			}
		}
	}

	private float GetTerrainHeight(float x, float z)
	{
		return TerrainGenerator.GetTerrainHeight(x, z);
	}

	private void GenerateWires(Vector3 start, Vector3 end, FenceSegmentData segment)
	{
		Vector3 dir = (end - start);
		float distance = dir.Length();
		dir = dir.Normalized();

		float pitch = Mathf.Atan2(end.Y - start.Y,
			new Vector2(end.X, end.Z).DistanceTo(new Vector2(start.X, start.Z)));

		int tiles = Mathf.CeilToInt(distance);
		float segmentLength = distance / tiles;

		foreach (float h in WireHeights)
		{
			// Adjusted yOffset to be more realistic for SA farm fences
			// Poles are usually ~1.5m tall above ground
			float yOffset = 1.0f * h + 0.2f; // Offset from ground base

			for (int t = 0; t < tiles; t++)
			{
				float offset = (t * segmentLength) + segmentLength * 0.5f;
				Vector3 pos = start + dir * offset;
				pos.Y += yOffset;

				float yaw = Mathf.Atan2(dir.X, dir.Z);

				segment.Wires.Add(new WireInstance
				{
					Position = pos,
					Yaw = yaw,
					Pitch = pitch,
					LengthScale = segmentLength
				});
			}
		}
	}

	private void BuildVisuals()
	{
		_generationThread.Join();
		GD.Print($"[FenceSystem] BuildVisuals() started. Chunks to build: {_chunkFenceData.Count}");

		int builtWithGeometry = 0;

		foreach (var kvp in _chunkFenceData)
		{
			Node3D chunkNode = new Node3D();
			chunkNode.Name = $"FenceChunk_{kvp.Key.X}_{kvp.Key.Z}";
			chunkNode.Visible = DebugForceVisible; // Visible immediately if debug flag is set
			AddChild(chunkNode);
			_chunkNodes[kvp.Key] = chunkNode;

			var data = kvp.Value;
			bool hasAny = false;

			if (PoleMesh != null && data.Poles.Count > 0)
			{ chunkNode.AddChild(CreateMultiMesh(PoleMesh, data.Poles)); hasAny = true; }

			if (StickMesh != null && data.Sticks.Count > 0)
			{ chunkNode.AddChild(CreateMultiMesh(StickMesh, data.Sticks)); hasAny = true; }

			if (WireMesh != null && data.Wires.Count > 0)
			{ chunkNode.AddChild(CreateWireMultiMesh(WireMesh, data.Wires)); hasAny = true; }

			if (hasAny) builtWithGeometry++;
		}

		GD.Print($"[FenceSystem] BuildVisuals() done. " +
				 $"{_chunkNodes.Count} chunk nodes created, {builtWithGeometry} have visible geometry. " +
				 $"DebugForceVisible={DebugForceVisible}");

		if (builtWithGeometry == 0)
		{
			GD.PrintErr("[FenceSystem] WARNING: No chunk has any geometry! " +
						"Either all meshes are null or the perimeter has no posts. " +
						"Check the Output log above for mesh extraction results.");
		}

		if (!DebugForceVisible)
		{
			// Non-debug: sync with streamer
			var streamer = WorldStreaming.WorldChunkStreamer.Instance;
			if (streamer != null)
			{
				foreach (var kvp in _chunkNodes) kvp.Value.Visible = true;
				GD.Print("[FenceSystem] Streamer found — all chunks set visible for initial sync.");
			}
		}

		// Always print where the fence is so we know where to look
		float halfX = (_mapX * MapInsetPercent) / 2f;
		float halfZ = (_mapZ * MapInsetPercent) / 2f;
		GD.Print($"[FenceSystem] *** FENCE IS AT perimeter ±({halfX:F0}, {halfZ:F0}) world units from origin. " +
				 $"Player must travel to the MAP EDGE to see it! ***");
	}

	private MultiMeshInstance3D CreateMultiMesh(Mesh mesh, List<Vector3> positions)
	{
		MultiMesh mm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			InstanceCount = positions.Count,
			Mesh = mesh
		};

		for (int i = 0; i < positions.Count; i++)
		{
			mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, positions[i]));
		}

		var mmi = new MultiMeshInstance3D { Multimesh = mm };
		mmi.Name = mesh.ResourceName + "_Instancer";
		return mmi;
	}

	private MultiMeshInstance3D CreateWireMultiMesh(Mesh mesh, List<WireInstance> wires)
	{
		MultiMesh mm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			InstanceCount = wires.Count,
			Mesh = mesh
		};

		for (int i = 0; i < wires.Count; i++)
		{
			var w = wires[i];

			Basis basis = Basis.Identity;
			basis = basis.Rotated(Vector3.Up, w.Yaw);
			basis = basis.Rotated(Vector3.Right, -w.Pitch); // Flipped pitch as Godot rotation usually expects negative for upward look
			basis = basis.Scaled(new Vector3(1, 1, w.LengthScale));

			mm.SetInstanceTransform(i, new Transform3D(basis, w.Position));
		}

		var mmi = new MultiMeshInstance3D { Multimesh = mm };
		mmi.Name = "Wire_Instancer";
		return mmi;
	}
}

public class FenceSegmentData
{
	public ChunkCoord ChunkId;

	public List<Vector3> Poles = new();
	public List<Vector3> Sticks = new();
	public List<WireInstance> Wires = new();

	public FenceSegmentData(ChunkCoord id)
	{
		ChunkId = id;
	}
}

public struct WireInstance
{
	public Vector3 Position;
	public float Yaw;
	public float Pitch;
	public float LengthScale;
}
