using Godot;
using System;
using System.Collections.Generic;

public partial class FenceSystem : Node3D
{
	[ExportCategory("Fence Dimensions")]
	[Export] public float MapInsetPercent = 0.95f;
	[Export] public float BaseStep = 3.0f;
	[Export] public int MinSticks = 4;
	[Export] public int MaxSticks = 6;
	[Export] public float[] WireHeights = new float[] { 0.3f, 0.6f, 0.9f };

	[ExportCategory("Assets")]
	[Export] public Mesh PoleMesh;
	[Export] public Mesh StickMesh;
	[Export] public Mesh WireMesh;

	[ExportCategory("Chunk Settings")]
	[Export] public float ChunkSize = 256f;

	private static FenceSystem _instance;
	public static FenceSystem Instance => _instance;

	private Thread _generationThread;

	private Dictionary<WorldStreaming.ChunkCoord, FenceSegmentData> _chunkFenceData = new();
	private Dictionary<WorldStreaming.ChunkCoord, Node3D> _chunkNodes = new();

	private float _mapX;
	private float _mapZ;
	private int _worldSeed;

	private float _invChunkSize;

	private StandardMaterial3D _poleMat;
	private StandardMaterial3D _stickMat;
	private StandardMaterial3D _wireMat;

	public override void _Ready()
	{
		if (_instance != null && _instance != this)
		{
			QueueFree();
			return;
		}
		_instance = this;

		var gameState = BasterBoer.Core.GameState.Instance;
		if (gameState == null)
		{
			GD.PrintErr("[FenceSystem] GameState instance not found!");
			return;
		}

		_mapX = gameState.MapSizeX;
		_mapZ = gameState.MapSizeZ;
		_worldSeed = gameState.WorldSeed;

		_invChunkSize = 1.0f / ChunkSize;

		// Setup materials for South African bushveld look
		_poleMat = new StandardMaterial3D {
			AlbedoColor = new Color(0.35f, 0.25f, 0.15f), // Rich dark wood
			Roughness = 0.8f
		};
		_stickMat = new StandardMaterial3D {
			AlbedoColor = new Color(0.45f, 0.35f, 0.25f), // Lighter "dropper" wood
			Roughness = 0.9f
		};
		_wireMat = new StandardMaterial3D {
			AlbedoColor = new Color(0.2f, 0.2f, 0.2f), // Dark oxidized wire
			Metallic = 0.8f,
			Roughness = 0.4f
		};

		// Fix materials if needed (as per user request)
		if (PoleMesh != null) ApplyMaterialToMesh(PoleMesh, _poleMat);
		if (StickMesh != null) ApplyMaterialToMesh(StickMesh, _stickMat);
		if (WireMesh != null) ApplyMaterialToMesh(WireMesh, _wireMat);

		_generationThread = new Thread();
		_generationThread.Start(Callable.From(GenerateFenceDataThreaded));
	}

	/// <summary>
	/// Dynamic visibility control for world streaming.
	/// Called by WorldChunkStreamer when active chunks update.
	/// </summary>
	public void UpdateVisibleChunks(IEnumerable<WorldStreaming.ChunkCoord> visibleCoords)
	{
		var visibleSet = new HashSet<WorldStreaming.ChunkCoord>(visibleCoords);

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
		var localData = new Dictionary<WorldStreaming.ChunkCoord, FenceSegmentData>();
		System.Random rng = new System.Random(_worldSeed);

		float insetX = _mapX * MapInsetPercent;
		float insetZ = _mapZ * MapInsetPercent;
		float halfX = insetX / 2f;
		float halfZ = insetZ / 2f;

		Vector3[] corners = {
			new Vector3(-halfX, 0, -halfZ),
			new Vector3(halfX, 0, -halfZ),
			new Vector3(halfX, 0, halfZ),
			new Vector3(-halfX, 0, halfZ)
		};

		for (int i = 0; i < 4; i++)
		{
			GenerateEdge(corners[i], corners[(i + 1) % 4], rng, localData);
		}

		_chunkFenceData = localData;

		CallDeferred(MethodName.BuildVisuals);
	}

	private void GenerateEdge(Vector3 edgeStart, Vector3 edgeEnd, Random rng,
		Dictionary<WorldStreaming.ChunkCoord, FenceSegmentData> data)
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

			var chunkId = WorldStreaming.ChunkCoord.FromWorldPosition(pos, ChunkSize);

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
					
					var dChunkId = WorldStreaming.ChunkCoord.FromWorldPosition(dPos, ChunkSize);
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
		return WorldStreaming.TerrainGenerator.GetTerrainHeight(x, z);
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
		_generationThread.WaitToFinish();

		foreach (var kvp in _chunkFenceData)
		{
			Node3D chunkNode = new Node3D();
			chunkNode.Name = $"FenceChunk_{kvp.Key.X}_{kvp.Key.Z}";
			chunkNode.Visible = false; // Initially hidden, streamer will reveal
			AddChild(chunkNode);
			_chunkNodes[kvp.Key] = chunkNode;

			var data = kvp.Value;

			if (PoleMesh != null && data.Poles.Count > 0)
				chunkNode.AddChild(CreateMultiMesh(PoleMesh, data.Poles));

			if (StickMesh != null && data.Sticks.Count > 0)
				chunkNode.AddChild(CreateMultiMesh(StickMesh, data.Sticks));

			if (WireMesh != null && data.Wires.Count > 0)
				chunkNode.AddChild(CreateWireMultiMesh(WireMesh, data.Wires));
		}
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
	public Vector2I ChunkId;

	public List<Vector3> Poles = new();
	public List<Vector3> Sticks = new();
	public List<WireInstance> Wires = new();

	public FenceSegmentData(Vector2I id)
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
