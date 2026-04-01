using Godot;

namespace LandManagementSim.Simulation
{
	/// <summary>
	/// Bridges BasterBoer.Core.Systems.GrassEcologySystem to the IGrassEcologyProvider
	/// interface expected by GrazingSystem. Maps the full simulation GrassPatchState
	/// (class with Biomass, Health, Moisture, etc.) to the lightweight readonly struct
	/// (Biomass, IsGrazeable, IsDepleted) used by the animal grazing layer.
	/// </summary>
	public sealed class GrassEcologyAdapter : IGrassEcologyProvider
	{
		private readonly BasterBoer.Core.Systems.GrassEcologySystem _ecology;

		public GrassEcologyAdapter(BasterBoer.Core.Systems.GrassEcologySystem ecology)
		{
			_ecology = ecology;
		}

		/// <inheritdoc />
		public GrassPatchState GetState(GrassSpawner spawner)
		{
			var state = _ecology.GetState(spawner);
			if (state == null)
				return new GrassPatchState(0f, false, true);

			return new GrassPatchState(
				biomass: state.Biomass,
				isGrazeable: state.Biomass > 0.05f,
				isDepleted: state.Biomass <= BasterBoer.Core.Systems.GrassEcologySystem.DepletionThreshold
			);
		}

		/// <inheritdoc />
		public void ApplyGrazing(GrassSpawner spawner, float amount)
		{
			_ecology.ApplyGrazing(spawner, amount);
		}
	}
}
