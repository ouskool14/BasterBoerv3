using Godot;
using BasterBoer.Core.Time;

namespace BasterBoer.Core.Systems
{
	/// <summary>
	/// Central time management system. The heartbeat of all simulation.
	/// Manages game clock progression and emits temporal signals for all systems.
	/// </summary>
	public partial class TimeSystem : Node
	{
		/// <summary>Singleton instance for global access.</summary>
		public static TimeSystem Instance { get; private set; }

		/// <summary>Emitted when a new in-game day begins.</summary>
		public event System.Action<GameDate> OnDayPassed;

		/// <summary>Emitted when a new in-game month begins. Triggers major simulation systems.</summary>
		public event System.Action<GameDate> OnMonthPassed;

		/// <summary>Emitted when the season changes.</summary>
		public event System.Action<Season, GameDate> OnSeasonChanged;

		/// <summary>Emitted when a new in-game year begins.</summary>
		public event System.Action<int, GameDate> OnYearPassed;

		/// <summary>Emitted when seasonal events occur (drought, hunting season, etc.).</summary>
		public event System.Action<SeasonalEvent, GameDate> OnSeasonalEvent;

		/// <summary>Current in-game date.</summary>
		public GameDate CurrentDate { get; private set; }

		/// <summary>Current season derived from the date.</summary>
		public Season CurrentSeason => CurrentDate.Season;

		/// <summary>Real seconds per in-game day at 1x speed.</summary>
		[Export]
		public float SecondsPerDay { get; set; } = 60f;

		/// <summary>Current time scale multiplier (1x, 2x, 3x).</summary>
		public float TimeScale { get; private set; } = 1f;

		/// <summary>Whether time progression is paused.</summary>
		public bool IsPaused { get; private set; }

		private float _accumulatedTime;
		private Season _lastKnownSeason;

		public override void _Ready()
		{
			if (Instance != null && Instance != this)
			{
				QueueFree();
				return;
			}
			Instance = this;

			CurrentDate = new GameDate(2024, 1, 1);
			_lastKnownSeason = CurrentDate.Season;

			GD.Print($"[TimeSystem] Initialized. Starting date: {CurrentDate.ToDisplayString()}");
		}

		public override void _Process(double delta)
		{
			if (IsPaused || TimeScale <= 0f) return;

			_accumulatedTime += (float)delta * TimeScale;

			if (_accumulatedTime >= SecondsPerDay)
			{
				_accumulatedTime -= SecondsPerDay;
				AdvanceDay();
			}
		}

		/// <summary>
		/// Advances the game by one day and emits appropriate signals.
		/// </summary>
		private void AdvanceDay()
		{
			GameDate previousDate = CurrentDate;
			Season previousSeason = _lastKnownSeason;

			CurrentDate = CurrentDate.AddDays(1);
			OnDayPassed?.Invoke(CurrentDate);

			// Check for month change (triggers heavy simulation)
			if (CurrentDate.Month != previousDate.Month)
			{
				OnMonthPassed?.Invoke(CurrentDate);
				ProcessSeasonalEvents();

				// Check for year change
				if (CurrentDate.Year != previousDate.Year)
				{
					OnYearPassed?.Invoke(CurrentDate.Year, CurrentDate);
				}
			}

			// Check for season change
			if (CurrentDate.Season != previousSeason)
			{
				_lastKnownSeason = CurrentDate.Season;
				OnSeasonChanged?.Invoke(CurrentDate.Season, CurrentDate);
			}
		}

		/// <summary>
		/// Processes seasonal events for the current month.
		/// </summary>
		private void ProcessSeasonalEvents()
		{
			var events = SeasonalEventCalendar.GetEventsForMonth(CurrentDate.Month);
			foreach (var eventType in events)
			{
				OnSeasonalEvent?.Invoke(eventType, CurrentDate);
			}
		}

		/// <summary>Sets the time scale (1x, 2x, 3x supported).</summary>
		public void SetTimeScale(float scale)
		{
			TimeScale = Mathf.Clamp(scale, 0.5f, 3f);
		}

		/// <summary>Pauses time progression.</summary>
		public void Pause()
		{
			IsPaused = true;
		}

		/// <summary>Resumes time progression.</summary>
		public void Resume()
		{
			IsPaused = false;
		}

		/// <summary>Returns progress through current day (0.0 to 1.0).</summary>
		public float GetDayProgress()
		{
			return SecondsPerDay > 0 ? Mathf.Clamp(_accumulatedTime / SecondsPerDay, 0f, 1f) : 0f;
		}
	}
}
