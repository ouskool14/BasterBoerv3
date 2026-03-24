using Godot;
using System;

namespace WorldStreaming.Flora
{
	/// <summary>
	/// South African flora species found in bushveld and savanna ecosystems.
	/// </summary>
	public enum FloraType
	{
		// Indigenous Trees
		AcaciaThorn,
		MarulaMpopona,
		BuffaloThorn,
		Tamboti,
		KnobtornAcacia,
		LeadwoodHardekool,
		SausageTree,
		AppleleafTree,
		SilverClusterleaf,
		ShepherdTree,
		
		// Shrubs and Bushes
		MagicGuarana,
		SicklebushDichrostachys,
		
		// Grasses
		RedGrass,
		PanicGrass,
		
		// Invasive Species (require management)
		InvasiveLantana,
		InvasiveBugweed
	}

	/// <summary>
	/// Pure simulation data for a single plant in the world.
	/// No Godot nodes - purely C# data structure.
	/// </summary>
	[Serializable]
	public struct FloraEntry
	{
		/// <summary>
		/// 2D world position (X, Z plane). Y derived from terrain height.
		/// </summary>
		public Vector2 WorldPosition2D;

		/// <summary>
		/// Species type of this plant.
		/// </summary>
		public FloraType Type;

		/// <summary>
		/// Health from 0 (dead) to 1 (thriving). Affects visual scale and color.
		/// </summary>
		public float Health;

		/// <summary>
		/// Age in simulation years. Influences scale and model selection.
		/// </summary>
		public float Age;

		/// <summary>
		/// Whether this plant is invasive and requires ecological management.
		/// </summary>
		public bool IsInvasive;

		/// <summary>
		/// Consistent rotation around Y axis (0-360 degrees).
		/// Stored to maintain visual consistency between chunk loads.
		/// </summary>
		public float RotationY;

		/// <summary>
		/// Scale multiplier based on age, health, and species variation.
		/// </summary>
		public float ScaleMultiplier;

		public FloraEntry(Vector2 worldPosition2D, FloraType type, float health = 1f, float age = 5f)
		{
			WorldPosition2D = worldPosition2D;
			Type = type;
			Health = Mathf.Clamp(health, 0f, 1f);
			Age = Mathf.Max(0f, age);
			IsInvasive = type == FloraType.InvasiveLantana || type == FloraType.InvasiveBugweed;
			
			// Deterministic rotation based on position for consistency
			uint hash = (uint)(worldPosition2D.X * 73856093) ^ (uint)(worldPosition2D.Y * 19349663);
			RotationY = (hash % 360);
			
			// Scale calculation: age factor * health factor * species variation
			float ageScale = Mathf.Clamp(age / 10f, 0.3f, 1.2f);
			float healthScale = Mathf.Lerp(0.6f, 1f, health);
			float speciesVariation = 0.8f + ((hash % 100) / 100f * 0.4f); // 0.8 to 1.2
			ScaleMultiplier = ageScale * healthScale * speciesVariation;
		}

		/// <summary>
		/// Returns true if this flora type is a tree (vs shrub or grass).
		/// </summary>
		public bool IsTree()
		{
			return Type switch
			{
				FloraType.AcaciaThorn or FloraType.MarulaMpopona or FloraType.BuffaloThorn or 
				FloraType.Tamboti or FloraType.KnobtornAcacia or FloraType.LeadwoodHardekool or
				FloraType.SausageTree or FloraType.AppleleafTree or FloraType.SilverClusterleaf or 
				FloraType.ShepherdTree => true,
				_ => false
			};
		}

		/// <summary>
		/// Gets the visual radius for culling and LOD calculations.
		/// </summary>
		public float GetVisualRadius()
		{
			float baseRadius = Type switch
			{
				FloraType.AcaciaThorn => 8f,
				FloraType.MarulaMpopona => 6f,
				FloraType.BuffaloThorn => 5f,
				FloraType.Tamboti => 7f,
				FloraType.SausageTree => 9f,
				FloraType.MagicGuarana => 2f,
				FloraType.SicklebushDichrostachys => 2.5f,
				FloraType.RedGrass or FloraType.PanicGrass => 0.5f,
				_ => 4f
			};
			
			return baseRadius * ScaleMultiplier;
		}
	}
}
