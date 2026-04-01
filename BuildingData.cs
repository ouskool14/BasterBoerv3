using System;
using Godot;
using WorldStreaming;

namespace Basterboer.Buildings
{
	/// <summary>
	/// Enum defining all building types available in the game
	/// </summary>
	public enum BuildingType
	{
		Wall,
		Roof,
		Floor,
		Door,
		Window,
		Stoep,
		BraaiArea,
		WaterTrough,
		FeedingStation,
		StaffQuarters,
		Boma,
		Hide
	}

	/// <summary>
	/// Pure data class representing a single building instance.
	/// Contains no scene nodes - only simulation data for performance.
	/// </summary>
	public class BuildingData
	{
		public Guid Id { get; set; }
		public BuildingType Type { get; set; }
		public Vector3 Position { get; set; }
		public float Rotation { get; set; } // Y-axis rotation in radians
		public ChunkCoord ChunkCoord { get; set; }
		public float Condition { get; set; } // 0.0 to 1.0 for future degradation system
		public DateTime PlacedAt { get; set; }

		public BuildingData()
		{
			Id = Guid.NewGuid();
			Condition = 1.0f;
			PlacedAt = DateTime.UtcNow;
		}

		public BuildingData(BuildingType type, Vector3 position, float rotation, ChunkCoord chunkCoord)
		{
			Id = Guid.NewGuid();
			Type = type;
			Position = position;
			Rotation = rotation;
			ChunkCoord = chunkCoord;
			Condition = 1.0f;
			PlacedAt = DateTime.UtcNow;
		}

		/// <summary>
		/// Creates a deep copy for serialization purposes
		/// </summary>
		public BuildingData Clone()
		{
			return new BuildingData
			{
				Id = this.Id,
				Type = this.Type,
				Position = this.Position,
				Rotation = this.Rotation,
				ChunkCoord = this.ChunkCoord,
				Condition = this.Condition,
				PlacedAt = this.PlacedAt
			};
		}
	}
}
