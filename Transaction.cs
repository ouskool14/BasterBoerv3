using BasterBoer.Core.Time;

namespace BasterBoer.Core.Economy
{
	/// <summary>
	/// Categories for financial transactions.
	/// </summary>
	public enum TransactionCategory
	{
		// Revenue streams
		TrophyHunting,
		PhotogTourism,
		LiveGameSales,
		LivestockSales,
		CarbonCredits,
		ResearchPartnership,
		GovernmentGrant,

		// Cost centers
		StaffWages,
		InfrastructureMaintenance,
		VehicleFuel,
		VeterinaryCosts,
		PermitFees,
		LoanRepayment,

		// Other
		InitialCapital,
		Adjustment
	}

	/// <summary>
	/// Immutable record of a financial transaction.
	/// Positive amounts are income, negative amounts are expenses.
	/// </summary>
	public struct Transaction
	{
		/// <summary>Date the transaction occurred.</summary>
		public GameDate Date { get; }

		/// <summary>Amount in ZAR (positive for income, negative for expenses).</summary>
		public float Amount { get; }

		/// <summary>Human-readable description.</summary>
		public string Description { get; }

		/// <summary>Transaction category for reporting.</summary>
		public TransactionCategory Category { get; }

		public Transaction(GameDate date, float amount, string description, TransactionCategory category)
		{
			Date = date;
			Amount = amount;
			Description = description ?? string.Empty;
			Category = category;
		}

		/// <summary>Returns true if this is an income transaction.</summary>
		public bool IsIncome => Amount > 0;

		/// <summary>Returns true if this is an expense transaction.</summary>
		public bool IsExpense => Amount < 0;

		public override string ToString()
		{
			string sign = Amount >= 0 ? "+" : "";
			return $"{Date}: {sign}R{Amount:F2} - {Description} ({Category})";
		}
	}
}
