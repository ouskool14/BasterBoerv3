using Godot;
using BasterBoer.Core.Systems;

/// <summary>
/// Drives sun rotation, light colour, ambient light, and sky horizon colour
/// by sampling hand-tuned keyframes against GameState.TimeOfDay.
/// Southern Hemisphere bushveld: sun is due NORTH at noon, rises NE, sets NW.
/// </summary>
public partial class DayNightCycle : Node
{
	[Export] public DirectionalLight3D SunLight;
	[Export] public WorldEnvironment WorldEnv;

	private struct LightKeyframe
	{
		public float Time;
		public float SunAltitude;
		public float SunAzimuth;
		public Color SunColor;
		public float SunEnergy;
		public Color AmbientColor;
		public float AmbientEnergy;
		public Color SkyHorizon;
		public Color SkyZenith;
	}

	private LightKeyframe[] _keyframes;

	public override void _Ready()
	{
		BuildKeyframes();
	}

	private void BuildKeyframes()
	{
		_keyframes = new LightKeyframe[]
		{
			// 00:00 — Deep night
			new() { Time = 0f,    SunAltitude = -40f, SunAzimuth = 0f,   SunColor = new Color(0.05f, 0.05f, 0.15f), SunEnergy = 0f,    AmbientColor = new Color(0.03f, 0.04f, 0.10f), AmbientEnergy = 0.15f, SkyHorizon = new Color(0.02f, 0.03f, 0.12f), SkyZenith = new Color(0.01f, 0.01f, 0.06f) },
			// 05:00 — Pre-dawn
			new() { Time = 5f,    SunAltitude = -10f, SunAzimuth = 90f,  SunColor = new Color(0.15f, 0.08f, 0.20f), SunEnergy = 0f,    AmbientColor = new Color(0.10f, 0.08f, 0.18f), AmbientEnergy = 0.25f, SkyHorizon = new Color(0.25f, 0.10f, 0.20f), SkyZenith = new Color(0.05f, 0.05f, 0.18f) },
			// 05:50 — Civil twilight
			new() { Time = 5.8f,  SunAltitude = -2f,  SunAzimuth = 80f,  SunColor = new Color(1.0f, 0.45f, 0.10f),  SunEnergy = 0.3f,  AmbientColor = new Color(0.30f, 0.20f, 0.25f), AmbientEnergy = 0.4f,  SkyHorizon = new Color(0.90f, 0.45f, 0.15f), SkyZenith = new Color(0.12f, 0.15f, 0.40f) },
			// 06:10 — Sunrise
			new() { Time = 6.1f,  SunAltitude = 4f,   SunAzimuth = 75f,  SunColor = new Color(1.0f, 0.38f, 0.05f),  SunEnergy = 0.8f,  AmbientColor = new Color(0.55f, 0.30f, 0.15f), AmbientEnergy = 0.5f,  SkyHorizon = new Color(1.0f, 0.55f, 0.15f),  SkyZenith = new Color(0.25f, 0.30f, 0.65f) },
			// 07:00 — Low morning sun
			new() { Time = 7f,    SunAltitude = 15f,  SunAzimuth = 65f,  SunColor = new Color(1.0f, 0.72f, 0.35f),  SunEnergy = 1.2f,  AmbientColor = new Color(0.65f, 0.50f, 0.30f), AmbientEnergy = 0.6f,  SkyHorizon = new Color(0.85f, 0.65f, 0.35f),  SkyZenith = new Color(0.35f, 0.50f, 0.85f) },
			// 08:30 — Morning proper
			new() { Time = 8.5f,  SunAltitude = 30f,  SunAzimuth = 50f,  SunColor = new Color(1.0f, 0.88f, 0.60f),  SunEnergy = 1.5f,  AmbientColor = new Color(0.70f, 0.65f, 0.50f), AmbientEnergy = 0.7f,  SkyHorizon = new Color(0.65f, 0.72f, 0.90f),  SkyZenith = new Color(0.30f, 0.50f, 0.95f) },
			// 11:00 — Approaching midday
			new() { Time = 11f,   SunAltitude = 60f,  SunAzimuth = 20f,  SunColor = new Color(1.0f, 0.97f, 0.88f),  SunEnergy = 1.9f,  AmbientColor = new Color(0.75f, 0.75f, 0.70f), AmbientEnergy = 0.8f,  SkyHorizon = new Color(0.72f, 0.80f, 0.95f),  SkyZenith = new Color(0.28f, 0.48f, 0.98f) },
			// 13:00 — Midday (sun due NORTH in Southern Hemisphere)
			new() { Time = 13f,   SunAltitude = 75f,  SunAzimuth = 0f,   SunColor = new Color(1.0f, 0.99f, 0.95f),  SunEnergy = 2.0f,  AmbientColor = new Color(0.78f, 0.78f, 0.75f), AmbientEnergy = 0.85f, SkyHorizon = new Color(0.75f, 0.82f, 0.95f),  SkyZenith = new Color(0.25f, 0.46f, 1.0f)  },
			// 15:00 — Afternoon
			new() { Time = 15f,   SunAltitude = 55f,  SunAzimuth = 330f, SunColor = new Color(1.0f, 0.93f, 0.70f),  SunEnergy = 1.8f,  AmbientColor = new Color(0.75f, 0.70f, 0.55f), AmbientEnergy = 0.75f, SkyHorizon = new Color(0.80f, 0.78f, 0.72f),  SkyZenith = new Color(0.28f, 0.46f, 0.95f) },
			// 16:30 — Golden hour
			new() { Time = 16.5f, SunAltitude = 30f,  SunAzimuth = 300f, SunColor = new Color(1.0f, 0.72f, 0.25f),  SunEnergy = 1.4f,  AmbientColor = new Color(0.80f, 0.60f, 0.35f), AmbientEnergy = 0.65f, SkyHorizon = new Color(0.95f, 0.70f, 0.30f),  SkyZenith = new Color(0.30f, 0.42f, 0.80f) },
			// 17:30 — Sunset
			new() { Time = 17.5f, SunAltitude = 8f,   SunAzimuth = 285f, SunColor = new Color(1.0f, 0.40f, 0.05f),  SunEnergy = 0.9f,  AmbientColor = new Color(0.70f, 0.38f, 0.18f), AmbientEnergy = 0.55f, SkyHorizon = new Color(1.0f, 0.48f, 0.10f),  SkyZenith = new Color(0.25f, 0.20f, 0.55f) },
			// 18:00 — Last light
			new() { Time = 18f,   SunAltitude = 0f,   SunAzimuth = 275f, SunColor = new Color(0.90f, 0.25f, 0.05f),  SunEnergy = 0.3f,  AmbientColor = new Color(0.40f, 0.22f, 0.20f), AmbientEnergy = 0.40f, SkyHorizon = new Color(0.80f, 0.30f, 0.12f),  SkyZenith = new Color(0.15f, 0.10f, 0.35f) },
			// 18:30 — Dusk
			new() { Time = 18.5f, SunAltitude = -8f,  SunAzimuth = 270f, SunColor = new Color(0.30f, 0.10f, 0.20f),  SunEnergy = 0f,    AmbientColor = new Color(0.18f, 0.12f, 0.22f), AmbientEnergy = 0.30f, SkyHorizon = new Color(0.35f, 0.15f, 0.30f),  SkyZenith = new Color(0.06f, 0.05f, 0.20f) },
			// 20:00 — Full dark
			new() { Time = 20f,   SunAltitude = -30f, SunAzimuth = 180f, SunColor = new Color(0.05f, 0.05f, 0.15f),  SunEnergy = 0f,    AmbientColor = new Color(0.03f, 0.04f, 0.10f), AmbientEnergy = 0.15f, SkyHorizon = new Color(0.02f, 0.03f, 0.12f),  SkyZenith = new Color(0.01f, 0.01f, 0.06f) },
			// 24:00 — Wrap (same as midnight)
			new() { Time = 24f,   SunAltitude = -40f, SunAzimuth = 0f,   SunColor = new Color(0.05f, 0.05f, 0.15f),  SunEnergy = 0f,    AmbientColor = new Color(0.03f, 0.04f, 0.10f), AmbientEnergy = 0.15f, SkyHorizon = new Color(0.02f, 0.03f, 0.12f),  SkyZenith = new Color(0.01f, 0.01f, 0.06f) },
		};
	}

	public override void _Process(double delta)
	{
		if (TimeSystem.Instance == null || SunLight == null || WorldEnv == null) return;

		float t = GameState.Instance.TimeOfDay;
		LightKeyframe current = SampleKeyframes(t);
		ApplyToScene(current);
	}

	private LightKeyframe SampleKeyframes(float time)
	{
		int next = 0;
		for (int i = 0; i < _keyframes.Length; i++)
		{
			if (_keyframes[i].Time > time) { next = i; break; }
			if (i == _keyframes.Length - 1) next = i;
		}
		int prev = Mathf.Max(0, next - 1);

		LightKeyframe a = _keyframes[prev];
		LightKeyframe b = _keyframes[next];

		float range = b.Time - a.Time;
		float blend = range > 0f ? (time - a.Time) / range : 0f;
		blend = Mathf.Clamp(blend, 0f, 1f);

		return new LightKeyframe
		{
			Time          = time,
			SunAltitude   = Mathf.Lerp(a.SunAltitude, b.SunAltitude, blend),
			SunAzimuth    = LerpAngle(a.SunAzimuth, b.SunAzimuth, blend),
			SunColor      = a.SunColor.Lerp(b.SunColor, blend),
			SunEnergy     = Mathf.Lerp(a.SunEnergy, b.SunEnergy, blend),
			AmbientColor  = a.AmbientColor.Lerp(b.AmbientColor, blend),
			AmbientEnergy = Mathf.Lerp(a.AmbientEnergy, b.AmbientEnergy, blend),
			SkyHorizon    = a.SkyHorizon.Lerp(b.SkyHorizon, blend),
			SkyZenith     = a.SkyZenith.Lerp(b.SkyZenith, blend),
		};
	}

	private void ApplyToScene(LightKeyframe kf)
	{
		// Sun rotation: altitude on X axis, azimuth on Y axis
		SunLight.RotationDegrees = new Vector3(
			-kf.SunAltitude,
			kf.SunAzimuth,
			0f
		);

		SunLight.LightColor = kf.SunColor;
		SunLight.LightEnergy = kf.SunEnergy;
		SunLight.Visible = kf.SunEnergy > 0.01f;

		// Sky ambient
		var env = WorldEnv.Environment;
		if (env == null) return;

		env.AmbientLightColor = kf.AmbientColor;
		env.AmbientLightEnergy = kf.AmbientEnergy;

		// ProceduralSky colours
		if (env.Sky?.SkyMaterial is ProceduralSkyMaterial sky)
		{
			sky.SkyHorizonColor = kf.SkyHorizon;
			sky.SkyTopColor = kf.SkyZenith;
			sky.GroundHorizonColor = kf.SkyHorizon.Darkened(0.3f);
		}
	}

	private static float LerpAngle(float from, float to, float t)
	{
		float diff = ((to - from + 540f) % 360f) - 180f;
		return from + diff * t;
	}
}
