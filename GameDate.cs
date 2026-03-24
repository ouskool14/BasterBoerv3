using System;

namespace BasterBoer.Core.Time
{
	/// <summary>
	/// Represents an in-game calendar date with South African seasonal awareness.
	/// Uses simplified 30-day months for consistent simulation cycles.
	/// </summary>
	public struct GameDate : IEquatable<GameDate>, IComparable<GameDate>
	{
		/// <summary>The calendar year (e.g., 2024).</summary>
		public int Year { get; }

		/// <summary>The month of the year (1-12).</summary>
		public int Month { get; }

		/// <summary>The day of the month (1-30).</summary>
		public int Day { get; }

		/// <summary>
		/// Returns the South African season for this date.
		/// Summer: Dec-Feb, Autumn: Mar-May, Winter: Jun-Aug, Spring: Sep-Nov
		/// </summary>
		public Season Season
		{
			get
			{
				return Month switch
				{
					12 or 1 or 2 => Season.Summer,
					3 or 4 or 5 => Season.Autumn,
					6 or 7 or 8 => Season.Winter,
					9 or 10 or 11 => Season.Spring,
					_ => Season.Summer
				};
			}
		}

		public GameDate(int year, int month, int day)
		{
			if (month < 1 || month > 12)
				throw new ArgumentOutOfRangeException(nameof(month), "Month must be between 1 and 12.");
			if (day < 1 || day > 30)
				throw new ArgumentOutOfRangeException(nameof(day), "Day must be between 1 and 30.");

			Year = year;
			Month = month;
			Day = day;
		}

		/// <summary>
		/// Adds the specified number of days, handling month and year rollovers.
		/// </summary>
		public GameDate AddDays(int daysToAdd)
		{
			if (daysToAdd == 0) return this;

			// Convert to total days since epoch for easier calculation
			int totalDays = (Year * 360) + ((Month - 1) * 30) + (Day - 1);
			totalDays += daysToAdd;

			if (totalDays < 0)
				throw new ArgumentOutOfRangeException(nameof(daysToAdd), "Resulting date would be negative.");

			int newYear = totalDays / 360;
			int remainder = totalDays % 360;
			int newMonth = (remainder / 30) + 1;
			int newDay = (remainder % 30) + 1;

			return new GameDate(newYear, newMonth, newDay);
		}

		/// <summary>
		/// Adds the specified number of months, preserving day where possible.
		/// </summary>
		public GameDate AddMonths(int monthsToAdd)
		{
			if (monthsToAdd == 0) return this;

			int totalMonths = (Year * 12) + (Month - 1) + monthsToAdd;
			if (totalMonths < 0)
				throw new ArgumentOutOfRangeException(nameof(monthsToAdd), "Resulting date would be negative.");

			int newYear = totalMonths / 12;
			int newMonth = (totalMonths % 12) + 1;
			int newDay = Math.Min(Day, 30);

			return new GameDate(newYear, newMonth, newDay);
		}

		/// <summary>
		/// Returns true if this date is in the same month and year as another date.
		/// </summary>
		public bool IsSameMonth(GameDate other)
		{
			return Year == other.Year && Month == other.Month;
		}

		public override string ToString()
		{
			return $"{Day:D2}/{Month:D2}/{Year}";
		}

		/// <summary>
		/// Returns a display-friendly date string with season information.
		/// </summary>
		public string ToDisplayString()
		{
			string[] monthNames = { "", "January", "February", "March", "April", "May", "June",
								   "July", "August", "September", "October", "November", "December" };
			return $"{Day} {monthNames[Month]} {Year} ({Season})";
		}

		public bool Equals(GameDate other)
		{
			return Year == other.Year && Month == other.Month && Day == other.Day;
		}

		public override bool Equals(object obj)
		{
			return obj is GameDate other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(Year, Month, Day);
		}

		public int CompareTo(GameDate other)
		{
			if (Year != other.Year) return Year.CompareTo(other.Year);
			if (Month != other.Month) return Month.CompareTo(other.Month);
			return Day.CompareTo(other.Day);
		}

		public static bool operator ==(GameDate left, GameDate right) => left.Equals(right);
		public static bool operator !=(GameDate left, GameDate right) => !left.Equals(right);
		public static bool operator <(GameDate left, GameDate right) => left.CompareTo(right) < 0;
		public static bool operator >(GameDate left, GameDate right) => left.CompareTo(right) > 0;
		public static bool operator <=(GameDate left, GameDate right) => left.CompareTo(right) <= 0;
		public static bool operator >=(GameDate left, GameDate right) => left.CompareTo(right) >= 0;
	}
}
