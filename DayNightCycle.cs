using Godot;
using System;

/// <summary>
/// Manages day/night cycle by controlling sun position, lighting, and sky colors.
/// Optimized for South African bushveld environment with warm tones.
/// </summary>
public partial class DayNightCycle : DirectionalLight3D
{
	[Export] public NodePath WorldEnvironmentPath;
	[Export] public float TransitionSpeed = 2.0f;
	[Export] public bool EnableSmoothTransitions = true;
	
	private WorldEnvironment _worldEnvironment;
	private ProceduralSkyMaterial _skyMaterial;
	private Godot.Environment _environment;
	
	// South African color palette - warm tones
	private readonly Color DawnLight = new Color(1.0f, 0.75f, 0.55f);
	private readonly Color DayLight = new Color(1.0f, 0.95f, 0.85f);
	private readonly Color GoldenLight = new Color(1.0f, 0.85f, 0.6f);
	private readonly Color DuskLight = new Color(0.9f, 0.6f, 0.4f);
	private readonly Color NightLight = new Color(0.3f, 0.35f, 0.5f);
	
	private readonly Color DaySky = new Color(0.4f, 0.6f, 0.85f);
	private readonly Color DawnSky = new Color(0.7f, 0.5f, 0.4f);
	private readonly Color NightSky = new Color(0.02f, 0.02f, 0.08f);
	
	// Performance optimization - cache last update time
	private float _lastTimeOfDay = -1f;
	
	public override void _Ready()
	{
		// Get WorldEnvironment reference
		if (!WorldEnvironmentPath.IsEmpty)
		{
			_worldEnvironment = GetNode<WorldEnvironment>(WorldEnvironmentPath);
		}
		else
		{
			_worldEnvironment = GetTree().Root.FindChild("WorldEnvironment", true, false) as WorldEnvironment;
		}
		
		if (_worldEnvironment?.Environment?.Sky?.SkyMaterial is ProceduralSkyMaterial skyMat)
		{
			_skyMaterial = skyMat;
			_environment = _worldEnvironment.Environment;
		}
		
		// Initialize with current time
		UpdateCycle(GameState.Instance.TimeOfDay, 0f);
	}
	
	public override void _Process(double delta)
	{
		float currentTime = GameState.Instance.TimeOfDay;
		
		// Performance optimization - only update when time changes significantly
		if (Mathf.Abs(currentTime - _lastTimeOfDay) > 0.01f)
		{
			UpdateCycle(currentTime, (float)delta);
			_lastTimeOfDay = currentTime;
		}
	}
	
	private void UpdateCycle(float timeOfDay, float deltaTime)
	{
		// Calculate sun rotation using realistic arc
		UpdateSunRotation(timeOfDay);
		
		// Get lighting parameters for current time
		var (lightColor, lightEnergy, skyColor, ambientEnergy) = GetLightingParameters(timeOfDay);
		
		// Apply weather modifiers
		ApplyWeatherEffects(ref lightColor, ref lightEnergy, ref skyColor, ref ambientEnergy);
		
		// Apply changes with smooth transitions
		if (EnableSmoothTransitions && deltaTime > 0)
		{
			float lerpFactor = Mathf.Clamp(TransitionSpeed * deltaTime, 0f, 1f);
			LightColor = LightColor.Lerp(lightColor, lerpFactor);
			LightEnergy = Mathf.Lerp(LightEnergy, lightEnergy, lerpFactor);
			
			if (_skyMaterial != null)
			{
				_skyMaterial.SkyTopColor = _skyMaterial.SkyTopColor.Lerp(skyColor, lerpFactor);
			}
			
			if (_environment != null)
			{
				_environment.AmbientLightEnergy = Mathf.Lerp(_environment.AmbientLightEnergy, ambientEnergy, lerpFactor);
			}
		}
		else
		{
			LightColor = lightColor;
			LightEnergy = lightEnergy;
			if (_skyMaterial != null) _skyMaterial.SkyTopColor = skyColor;
			if (_environment != null) _environment.AmbientLightEnergy = ambientEnergy;
		}
	}
	
	private void UpdateSunRotation(float timeOfDay)
	{
		// Calculate sun angle - realistic arc from east to west
		// Dawn (5:30) to Dusk (18:30) = 13 hours of daylight
		float sunriseTime = 5.5f;
		float sunsetTime = 18.5f;
		
		float sunAngle;
		if (timeOfDay >= sunriseTime && timeOfDay <= sunsetTime)
		{
			// Daytime - sun above horizon
			float dayProgress = (timeOfDay - sunriseTime) / (sunsetTime - sunriseTime);
			// Use sine curve for realistic sun arc
			sunAngle = Mathf.Lerp(-10f, -170f, dayProgress);
		}
		else
		{
			// Nighttime - sun below horizon (moon position)
			sunAngle = -90f;
		}
		
		RotationDegrees = new Vector3(sunAngle, 45f, 0f); // 45° Y offset for angled shadows
	}
	
	private (Color lightColor, float lightEnergy, Color skyColor, float ambientEnergy) GetLightingParameters(float timeOfDay)
	{
		Color lightColor;
		float lightEnergy, ambientEnergy;
		Color skyColor;
		
		if (timeOfDay >= 5.0f && timeOfDay < 7.0f) // Dawn
		{
			float t = (timeOfDay - 5.0f) / 2.0f;
			lightColor = NightLight.Lerp(DawnLight, t);
			lightEnergy = Mathf.Lerp(0.1f, 0.8f, t);
			skyColor = NightSky.Lerp(DawnSky, t);
			ambientEnergy = Mathf.Lerp(0.1f, 0.3f, t);
		}
		else if (timeOfDay >= 7.0f && timeOfDay < 17.0f) // Day
		{
			float t = Mathf.Clamp((timeOfDay - 7.0f) / 10.0f, 0f, 1f);
			lightColor = DawnLight.Lerp(DayLight, t);
			lightEnergy = Mathf.Lerp(0.8f, 1.2f, Mathf.Sin(t * Mathf.Pi)); // Peak at noon
			skyColor = DawnSky.Lerp(DaySky, t);
			ambientEnergy = Mathf.Lerp(0.3f, 0.5f, t);
		}
		else if (timeOfDay >= 17.0f && timeOfDay < 19.0f) // Golden Hour to Dusk
		{
			float t = (timeOfDay - 17.0f) / 2.0f;
			lightColor = DayLight.Lerp(DuskLight, t);
			lightEnergy = Mathf.Lerp(1.0f, 0.3f, t);
			skyColor = DaySky.Lerp(DawnSky, t);
			ambientEnergy = Mathf.Lerp(0.5f, 0.2f, t);
		}
		else // Night
		{
			lightColor = NightLight;
			lightEnergy = 0.1f;
			skyColor = NightSky;
			ambientEnergy = 0.1f;
		}
		
		return (lightColor, lightEnergy, skyColor, ambientEnergy);
	}
	
	private void ApplyWeatherEffects(ref Color lightColor, ref float lightEnergy, ref Color skyColor, ref float ambientEnergy)
	{
		WeatherState weather = GameState.Instance.CurrentWeather;
		
		switch (weather)
		{
			case WeatherState.Cloudy:
				lightEnergy *= 0.85f;
				ambientEnergy *= 1.1f;
				break;
				
			case WeatherState.Overcast:
				lightColor = lightColor.Lerp(new Color(0.8f, 0.8f, 0.85f), 0.4f);
				lightEnergy *= 0.6f;
				skyColor = skyColor.Lerp(new Color(0.5f, 0.5f, 0.55f), 0.5f);
				ambientEnergy *= 1.3f;
				break;
				
			case WeatherState.Rain:
			case WeatherState.Storm:
				lightColor = lightColor.Lerp(new Color(0.7f, 0.75f, 0.8f), 0.6f);
				lightEnergy *= 0.5f;
				skyColor = skyColor.Lerp(new Color(0.4f, 0.4f, 0.45f), 0.7f);
				ambientEnergy *= 1.4f;
				break;
		}
	}
}
