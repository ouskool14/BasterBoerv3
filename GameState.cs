using System.Collections.Generic;
using System.Linq;
using Godot;
using BasterBoer.Core.Economy;

public enum WeatherState
{
	Clear,
	Cloudy, 
	Overcast,
	Rain,
	Storm,
	Drought
}

/// <summary>
/// Central game state singleton. This partial class contains financial state.
/// </summary>
public partial class GameState : Node
{
	private static GameState _instance;
	public static GameState Instance => _instance;

	/// <summary>Current cash balance in ZAR.</summary>
	public float CashBalance { get; set; } = 2500000f; // Starting capital: R2.5M

	/// <summary>Latest monthly burn rate (expenses).</summary>
	public float MonthlyBurnRate { get; private set; }

	/// <summary>Latest monthly revenue.</summary>
	public float MonthlyRevenue { get; private set; }

	/// <summary>Transaction history (last 24 months).</summary>
	public List<Transaction> TransactionHistory { get; } = new List<Transaction>();

	/// <summary>Active loans requiring payment.</summary>
	public List<Loan> ActiveLoans { get; } = new List<Loan>();

	// New Time and Weather Properties
	public float TimeOfDay { get; set; } = 8.0f; // 0.0 to 24.0 hours
	public WeatherState CurrentWeather { get; set; } = WeatherState.Clear;

	[Export] public float MapSizeX { get; set; } = 2048f;
	[Export] public float MapSizeZ { get; set; } = 2048f;
	[Export] public int WorldSeed { get; set; } = 12345;
	[Signal]
	public delegate void WeatherChangedEventHandler(WeatherState newWeather, WeatherState oldWeather);
	
	public override void _EnterTree()
	{
		if (_instance != null && _instance != this)
		{
			QueueFree();
			return;
		}
		_instance = this;
	}
	
	public override void _Ready()
	{
		if (_instance != null && _instance != this)
		{
			QueueFree();
			return;
		}
		_instance = this;

		GD.Print($"[GameState] Initialized. Starting balance: R{CashBalance:F2}");
	}

	/// <summary>
	/// Updates monthly financial metrics.
	/// </summary>
	public void UpdateMonthlyFinancials(float revenue, float expenses)
	{
		MonthlyRevenue = revenue;
		MonthlyBurnRate = expenses;
	}

	/// <summary>
	/// Adds a transaction to history and trims old entries.
	/// </summary>
	public void AddTransaction(Transaction transaction)
	{
		TransactionHistory.Add(transaction);

		// Trim to last 24 months (approximately 720 transactions)
		if (TransactionHistory.Count > 1000)
		{
			TransactionHistory.RemoveRange(0, TransactionHistory.Count - 720);
		}
	}

	/// <summary>
	/// Returns total outstanding loan debt.
	/// </summary>
	public float GetTotalLoanDebt()
	{
		return ActiveLoans.Sum(loan => loan.OutstandingBalance);
	}

	/// <summary>
	/// Returns net worth (cash minus debt).
	/// </summary>
	public float GetNetWorth()
	{
		return CashBalance - GetTotalLoanDebt();
	}

	/// <summary>
	/// Returns months of runway at current burn rate.
	/// </summary>
	public float GetMonthsOfRunway()
	{
		return MonthlyBurnRate > 0 ? CashBalance / MonthlyBurnRate : -1f;
	}

	/// <summary>
	/// Returns transactions for a specific month.
	/// </summary>
	public List<Transaction> GetTransactionsForMonth(int year, int month)
	{
		return TransactionHistory
			.Where(t => t.Date.Year == year && t.Date.Month == month)
			.ToList();
	}

	/// <summary>
	/// Update time of day (called by TimeSystem)
	/// </summary>
	public void UpdateTimeOfDay(float newTime)
	{
		TimeOfDay = Mathf.Wrap(newTime, 0f, 24f);
	}

	/// <summary>
	/// Update weather state and emit signal
	/// </summary>
	public void UpdateWeather(WeatherState newWeather)
	{
		if (CurrentWeather != newWeather)
		{
			WeatherState oldWeather = CurrentWeather;
			CurrentWeather = newWeather;
			EmitSignal(SignalName.WeatherChanged, (int)newWeather, (int)oldWeather);
		}
	}

	/// <summary>
	/// Check if it's currently raining
	/// </summary>
	public bool IsRaining => CurrentWeather == WeatherState.Rain || CurrentWeather == WeatherState.Storm;
}
