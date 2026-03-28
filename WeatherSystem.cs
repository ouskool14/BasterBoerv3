using Godot;
using System;
using System.Linq;
using BasterBoer.Core.Time;
using BasterBoer.Core.Systems;

/// <summary>
/// Manages weather state transitions based on South African seasonal patterns.
/// Integrates with existing TimeSystem for daily weather evaluation.
/// </summary>
public partial class WeatherSystem : Node3D
{
	public static WeatherSystem Instance { get; private set; }
	
	[Export] public bool EnableWeatherChanges = true;
	[Export] public float WeatherChangeChance = 0.3f;
	[Export] public int MinWeatherDuration = 1;
	[Export] public int MaxWeatherDuration = 3;
	
	[Export] public GpuParticles3D RainParticles;
	[Export] public AudioStreamPlayer3D ThunderAudio;
	
	private RandomNumberGenerator _rng = new RandomNumberGenerator();
	private int _daysUntilWeatherChange;
	private int _consecutiveRainDays = 0;
	private int _consecutiveDryDays = 0;
	
	// South African seasonal weather probabilities
	private readonly float[,] SeasonalWeatherWeights = new float[4, 6] {
		// Summer (wet season): Clear, Cloudy, Overcast, Rain, Storm, Drought
		{ 0.3f, 0.2f, 0.1f, 0.25f, 0.15f, 0.0f },
		// Autumn (transitional): 
		{ 0.5f, 0.25f, 0.15f, 0.08f, 0.02f, 0.0f },
		// Winter (dry season):
		{ 0.7f, 0.2f, 0.05f, 0.02f, 0.01f, 0.02f },
		// Spring (transitional):
		{ 0.45f, 0.3f, 0.15f, 0.08f, 0.02f, 0.0f }
	};
	
	public override void _EnterTree()
	{
		Instance = this;
	}
	
	public override void _Ready()
	{
		_rng.Randomize();
		_daysUntilWeatherChange = _rng.RandiRange(MinWeatherDuration, MaxWeatherDuration);
		
		// Connect to GameState or other systems if needed
		UpdateWeatherEffects();
	}
	
	/// <summary>
	/// External trigger for day ticks, called from TimeSystem
	/// </summary>
	public void OnDayTicked()
	{
		if (!EnableWeatherChanges) return;
		
		_daysUntilWeatherChange--;
		
		if (_daysUntilWeatherChange <= 0)
		{
			EvaluateWeatherChange();
			_daysUntilWeatherChange = _rng.RandiRange(MinWeatherDuration, MaxWeatherDuration);
		}
		
		TrackWeatherPatterns();
		UpdateWeatherEffects();
	}
	
	private void EvaluateWeatherChange()
	{
		if (_rng.Randf() > WeatherChangeChance) return;
		
		Season currentSeason = TimeSystem.Instance.CurrentSeason;
		int seasonIndex = (int)currentSeason;
		
		// Get seasonal probabilities and adjust based on recent weather
		float[] weights = new float[6];
		for (int i = 0; i < 6; i++)
		{
			weights[i] = SeasonalWeatherWeights[seasonIndex, i];
		}
		
		// Adjust probabilities based on recent patterns
		AdjustWeatherProbabilities(weights);
		
		// Select new weather using weighted random
		WeatherState newWeather = SelectWeightedWeather(weights);
		GameState.Instance.UpdateWeather(newWeather);
		
		GD.Print($"Weather changed to: {newWeather}");
	}
	
	private void AdjustWeatherProbabilities(float[] weights)
	{
		// Reduce rain chance after consecutive rainy days
		if (_consecutiveRainDays > 2)
		{
			weights[(int)WeatherState.Rain] *= 0.3f;
			weights[(int)WeatherState.Storm] *= 0.3f;
			weights[(int)WeatherState.Clear] *= 1.5f;
		}
		
		// Increase rain chance after long dry spells (except in winter)
		if (_consecutiveDryDays > 7 && TimeSystem.Instance.CurrentSeason != Season.Winter)
		{
			weights[(int)WeatherState.Rain] *= 1.5f;
			weights[(int)WeatherState.Cloudy] *= 1.3f;
		}
	}
	
	private WeatherState SelectWeightedWeather(float[] weights)
	{
		float totalWeight = weights.Sum();
		if (totalWeight <= 0) return WeatherState.Clear;

		float randomValue = _rng.Randf() * totalWeight;
		float cumulativeWeight = 0f;
		
		for (int i = 0; i < weights.Length; i++)
		{
			cumulativeWeight += weights[i];
			if (randomValue <= cumulativeWeight)
				return (WeatherState)i;
		}
		
		return WeatherState.Clear;
	}
	
	private void TrackWeatherPatterns()
	{
		if (GameState.Instance.IsRaining)
		{
			_consecutiveRainDays++;
			_consecutiveDryDays = 0;
		}
		else
		{
			_consecutiveDryDays++;
			_consecutiveRainDays = 0;
		}
	}
	
	private void UpdateWeatherEffects()
	{
		bool shouldRain = GameState.Instance.IsRaining;
		
		if (RainParticles != null)
		{
			RainParticles.Emitting = shouldRain;
			
			// Adjust intensity for storms
			if (shouldRain)
			{
				RainParticles.Amount = GameState.Instance.CurrentWeather == WeatherState.Storm ? 2000 : 1000;
			}
		}
		
		// Handle storm effects
		if (GameState.Instance.CurrentWeather == WeatherState.Storm)
		{
			HandleStormEffects();
		}
	}
	
	private Timer _lightningTimer;
	
	private void HandleStormEffects()
	{
		if (_lightningTimer == null)
		{
			_lightningTimer = new Timer();
			AddChild(_lightningTimer);
			_lightningTimer.WaitTime = _rng.RandfRange(5f, 12f);
			_lightningTimer.Timeout += TriggerLightning;
			_lightningTimer.Start();
		}
	}
	
	private void TriggerLightning()
	{
		GD.Print("Lightning strike!");
		
		// Brief light flash effect
		if (GetTree().Root.FindChild("DirectionalLight3D", true, false) is DirectionalLight3D sunLight)
		{
			Tween flashTween = CreateTween();
			float originalEnergy = sunLight.LightEnergy;
			flashTween.TweenProperty(sunLight, "light_energy", originalEnergy + 2.0f, 0.1f);
			flashTween.TweenProperty(sunLight, "light_energy", originalEnergy, 0.4f);
		}
		
		// Play thunder (when audio is available)
		if (ThunderAudio != null && !ThunderAudio.Playing)
		{
			ThunderAudio.Play();
		}
		
		// Reset timer for next lightning
		_lightningTimer.WaitTime = _rng.RandfRange(5f, 12f);
	}
	
	public void ForceWeather(WeatherState weather)
	{
		GameState.Instance.UpdateWeather(weather);
		UpdateWeatherEffects();
	}
}
