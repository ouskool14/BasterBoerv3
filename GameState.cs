using System.Collections.Generic;
using System.Linq;
using Godot;
using BasterBoer.Core.Economy;

namespace BasterBoer.Core
{
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
	}
}
