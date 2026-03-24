using System.Collections.Generic;

namespace BasterBoer.Core.Time
{
	/// <summary>
	/// Static calendar defining seasonal events for South African patterns.
	/// </summary>
	public static class SeasonalEventCalendar
	{
		private static readonly Dictionary<int, List<SeasonalEvent>> _eventSchedule = new()
		{
			{ 1, new List<SeasonalEvent>() }, // January: Mid-summer
			{ 2, new List<SeasonalEvent>() }, // February: Late summer
			{ 3, new List<SeasonalEvent>() }, // March: Early autumn
			{ 4, new List<SeasonalEvent> { SeasonalEvent.HuntingSeasonOpen } }, // April: Hunting season opens
			{ 5, new List<SeasonalEvent>() }, // May: Mid-autumn
			{ 6, new List<SeasonalEvent> { SeasonalEvent.DroughtStart } }, // June: Winter drought begins
			{ 7, new List<SeasonalEvent>() }, // July: Mid-winter
			{ 8, new List<SeasonalEvent> { SeasonalEvent.CalvingSeason, SeasonalEvent.FireRisk } }, // August: Late winter
			{ 9, new List<SeasonalEvent> { SeasonalEvent.HuntingSeasonClose } }, // September: Spring begins
			{ 10, new List<SeasonalEvent> { SeasonalEvent.FirstRains } }, // October: First rains expected
			{ 11, new List<SeasonalEvent> { SeasonalEvent.DroughtEnd } }, // November: Drought ends
			{ 12, new List<SeasonalEvent>() } // December: Early summer
		};

		/// <summary>
		/// Returns seasonal events scheduled for the specified month (1-12).
		/// </summary>
		public static List<SeasonalEvent> GetEventsForMonth(int month)
		{
			return _eventSchedule.TryGetValue(month, out var events) 
				? new List<SeasonalEvent>(events) 
				: new List<SeasonalEvent>();
		}

		/// <summary>
		/// Returns true if hunting season is active for the specified month.
		/// </summary>
		public static bool IsHuntingSeasonActive(int month)
		{
			return month >= 4 && month <= 8; // April to August
		}
	}
}
