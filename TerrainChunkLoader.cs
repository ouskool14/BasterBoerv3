using Godot;

namespace WorldStreaming
{
	/// <summary>
	/// Generates a single terrain chunk with mesh + collision from TerrainGenerator.
	/// This is the ONLY terrain surface in the scene — no GDScript terrain.
	/// All height queries (animals, spawning, navigation) go through TerrainGenerator.cs.
	/// </summary>
	public partial class TerrainChunkLoader : Node3D
	{
		[Export] public float ChunkSize { get; set; } = 256f;
		[Export] public int ChunkCoordX { get; set; } = 0;
		[Export] public int ChunkCoordZ { get; set; } = 0;

		public override void _Ready()
		{
			// Sample noise from a region centered on world origin
			// Mesh vertices are local-space centered (range: -128 to +128)
			// Node position at (0,0,0) means terrain is centered on the origin
			var worldOrigin = new Vector3(-ChunkSize * 0.5f, 0f, -ChunkSize * 0.5f);

			GD.Print($"[TerrainChunkLoader] Generating terrain centered at origin. Noise origin: {worldOrigin}");

			ArrayMesh mesh = TerrainGenerator.GenerateTerrainMesh(worldOrigin, ChunkSize);
			if (mesh == null)
			{
				GD.PushError("[TerrainChunkLoader] GenerateTerrainMesh returned null!");
				return;
			}

			// Visual mesh
			var meshInstance = new MeshInstance3D
			{
				Mesh = mesh,
				Name = "Terrain"
			};
			AddChild(meshInstance);
			GD.Print($"[TerrainChunkLoader] Mesh added. Surface count: {mesh.GetSurfaceCount()}");

			// Collision — from same mesh, same heights
			var concaveShape = mesh.CreateTrimeshShape();
			if (concaveShape != null)
			{
				var collisionShape = new CollisionShape3D
				{
					Shape = concaveShape,
					Name = "TerrainCollision"
				};
				var staticBody = new StaticBody3D { Name = "TerrainBody" };
				staticBody.AddChild(collisionShape);
				AddChild(staticBody);
				GD.Print($"[TerrainChunkLoader] Collision added.");
			}
			else
			{
				GD.PushError("[TerrainChunkLoader] CreateTrimeshShape returned null — no collision!");
			}

			GD.Print($"[TerrainChunkLoader] Chunk ({ChunkCoordX},{ChunkCoordZ}) ready at {Position}");
		}
	}
}
