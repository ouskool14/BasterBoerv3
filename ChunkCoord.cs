using System;
using Godot;

namespace WorldStreaming
{
	/// <summary>
	/// Represents discrete chunk coordinates in the world grid.
	/// Each chunk covers 256m x 256m of South African landscape.
	/// </summary>
	public readonly struct ChunkCoord : IEquatable<ChunkCoord>
	{
		public readonly int X;
		public readonly int Z;

		public ChunkCoord(int x, int z)
		{
			X = x;
			Z = z;
		}

		/// <summary>
		/// Converts world position to the chunk coordinate containing that position.
		/// </summary>
		public static ChunkCoord FromWorldPosition(Vector3 worldPos, float chunkSize = 256f)
		{
			int x = Mathf.FloorToInt(worldPos.X / chunkSize);
			int z = Mathf.FloorToInt(worldPos.Z / chunkSize);
			return new ChunkCoord(x, z);
		}

		/// <summary>
		/// Returns the world-space origin (southwest corner) of this chunk.
		/// </summary>
		public Vector3 GetWorldOrigin(float chunkSize = 256f)
		{
			return new Vector3(X * chunkSize, 0f, Z * chunkSize);
		}

		/// <summary>
		/// Returns the center point of this chunk in world space.
		/// </summary>
		public Vector3 GetWorldCenter(float chunkSize = 256f)
		{
			float halfSize = chunkSize * 0.5f;
			return new Vector3((X * chunkSize) + halfSize, 0f, (Z * chunkSize) + halfSize);
		}

		/// <summary>
		/// Gets the 3x3 grid of chunks centered on this coordinate.
		/// Used by WorldChunkStreamer for the active chunk grid.
		/// </summary>
		public ChunkCoord[] Get3x3Grid()
		{
			ChunkCoord[] grid = new ChunkCoord[9];
			int index = 0;
			
			for (int zOffset = -1; zOffset <= 1; zOffset++)
			{
				for (int xOffset = -1; xOffset <= 1; xOffset++)
				{
					grid[index++] = new ChunkCoord(X + xOffset, Z + zOffset);
				}
			}
			
			return grid;
		}

		/// <summary>
		/// Manhattan distance between two chunk coordinates (for load prioritization).
		/// </summary>
		public int ManhattanDistance(ChunkCoord other)
		{
			return Math.Abs(X - other.X) + Math.Abs(Z - other.Z);
		}

		public bool Equals(ChunkCoord other) => X == other.X && Z == other.Z;
		public override bool Equals(object obj) => obj is ChunkCoord other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(X, Z);
		public static bool operator ==(ChunkCoord left, ChunkCoord right) => left.Equals(right);
		public static bool operator !=(ChunkCoord left, ChunkCoord right) => !left.Equals(right);
		public override string ToString() => $"Chunk({X}, {Z})";
	}
}
