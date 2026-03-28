using Godot;
using LandManagementSim.Terrain;

namespace LandManagementSim.Simulation
{
	/// <summary>
	/// Terrain-aware movement helpers for the simulation layer.
	/// No allocations, no LINQ, no raycasts — only TerrainQuery lookups.
	///
	/// Usage:
	///   - Call ValidateMove() before applying herd movement
	///   - Call SnapToTerrain() to correct Y position after movement
	///   - Call ComputeAnimalWorldPos() to get per-animal terrain-snapped positions
	/// </summary>
	public static class AnimalMovement
	{
		/// <summary>
		/// Checks whether moving from currentPos to newPos is valid given terrain.
		/// Returns false if destination is unwalkable, slope too steep, or drop too deep.
		/// </summary>
		public static bool ValidateMove(Vector3 currentPos, Vector3 newPos)
		{
			return TerrainQuery.CanMove(currentPos, newPos);
		}

		/// <summary>
		/// Tries to find a valid move toward target. If the direct path is blocked,
		/// attempts perpendicular offsets. Returns false if all paths blocked.
		/// </summary>
		/// <param name="currentPos">Current world position.</param>
		/// <param name="desiredPos">Desired destination.</param>
		/// <param name="validatedPos">Output: a valid position (snapped to terrain Y).</param>
		/// <returns>True if a valid move was found.</returns>
		public static bool TryValidateMove(Vector3 currentPos, Vector3 desiredPos, out Vector3 validatedPos)
		{
			// Try direct path
			if (ValidateMove(currentPos, desiredPos))
			{
				validatedPos = desiredPos;
				validatedPos.Y = TerrainQuery.GetHeight(desiredPos.X, desiredPos.Z);
				return true;
			}

			// Try perpendicular offsets (slide along obstacle)
			Vector3 delta = desiredPos - currentPos;
			delta.Y = 0f;
			float dist = delta.Length();
			if (dist < 0.01f)
			{
				validatedPos = currentPos;
				return false;
			}

			Vector3 forward = delta / dist;
			Vector3 right = new Vector3(-forward.Z, 0f, forward.X);

			// Try 45-degree offsets
			for (int i = 0; i < 2; i++)
			{
				Vector3 side = i == 0 ? right : -right;
				Vector3 offset = (forward + side).Normalized() * dist;
				Vector3 testPos = currentPos + offset;

				if (ValidateMove(currentPos, testPos))
				{
					validatedPos = testPos;
					validatedPos.Y = TerrainQuery.GetHeight(testPos.X, testPos.Z);
					return true;
				}
			}

			// All paths blocked — stay in place
			validatedPos = currentPos;
			validatedPos.Y = TerrainQuery.GetHeight(currentPos.X, currentPos.Z);
			return false;
		}

		/// <summary>
		/// Snaps a position's Y coordinate to terrain height.
		/// Call after any direct position modification.
		/// </summary>
		public static void SnapToTerrain(ref Vector3 position)
		{
			position.Y = TerrainQuery.GetHeight(position.X, position.Z);
		}

		/// <summary>
		/// Computes an animal's absolute world position from herd center + offset,
		/// with Y snapped to terrain at the animal's actual X/Z position.
		/// This is the per-animal query — use sparingly or stagger.
		/// </summary>
		public static Vector3 ComputeAnimalWorldPos(Vector3 herdCenter, Vector3 animalOffset)
		{
			Vector3 worldPos = herdCenter + animalOffset;
			worldPos.Y = TerrainQuery.GetHeight(worldPos.X, worldPos.Z);
			return worldPos;
		}

		/// <summary>
		/// Computes an animal's world position using a pre-sampled terrain height.
		/// Use this when you've already called GetHeight() for the herd center
		/// and want to avoid per-animal queries.
		/// </summary>
		public static Vector3 ComputeAnimalWorldPos(Vector3 herdCenter, Vector3 animalOffset, float preSampledHeight)
		{
			Vector3 worldPos = herdCenter + animalOffset;
			worldPos.Y = preSampledHeight;
			return worldPos;
		}
	}
}
