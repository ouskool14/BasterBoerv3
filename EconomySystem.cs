using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using BasterBoer.Core.Time;
using BasterBoer.Core.Systems;

namespace BasterBoer.Core.Economy
{
	/// <summary>
	/// Central financial management system. Processes all revenue streams and costs.
	/// Never touches Godot scene nodes - pure data processing.
	/// </summary>
	public partial class EconomySystem : Node
	{
		/// <summary>Singleton instance.</summary>
		public static EconomySystem Instance { get; private set; }

		/// <summary>Emitted after monthly financial processing is complete.</summary>
		public event System.Action<GameDate> OnMonthlyStatementReady;

		/// <summary>Emitted when cash balance changes significantly.</summary>
		public event System.Action<float> OnCashBalanceChanged;

		#region Public API — Direct spend/balance queries

		/// <summary>Returns true if the current balance can cover the given amount.</summary>
		/// <param name="amount">Cost in ZAR to check.</param>
		public bool CanAfford(float amount) => GetBalance() >= amount;

		/// <summary>
		/// Deducts <paramref name="amount"/> from the cash balance if funds are available.
		/// </summary>
		/// <param name="amount">Cost in ZAR.</param>
		/// <param name="description">Optional transaction description.</param>
		/// <returns>True if the transaction succeeded.</returns>
		public bool SpendMoney(float amount, string description = "")
		{
			if (!CanAfford(amount)) return false;

			var gameState = GameState.Instance;
			if (gameState == null) return false;

			gameState.CashBalance -= amount;
			OnCashBalanceChanged?.Invoke(gameState.CashBalance);
			GD.Print($"[Economy] Spent R{amount:F2}: {description}. Balance: R{gameState.CashBalance:F2}");
			return true;
		}

		/// <summary>Returns the current cash balance in ZAR.</summary>
		public float GetBalance() => GameState.Instance?.CashBalance ?? 0f;

		#endregion

		/// <summary>Current month's revenue accumulator.</summary>
		public float MonthlyRevenue { get; private set; }

		/// <summary>Current month's expense accumulator.</summary>
		public float MonthlyExpenses { get; private set; }

		/// <summary>Net income for current month.</summary>
		public float MonthlyNetIncome => MonthlyRevenue - MonthlyExpenses;

		// Seasonal state flags
		private bool _isHuntingSeasonOpen;
		private bool _isDroughtActive;
		private bool _isFireRiskHigh;

		public override void _Ready()
		{
			if (Instance != null && Instance != this)
			{
				QueueFree();
				return;
			}
			Instance = this;

			// Connect to TimeSystem signals
			if (TimeSystem.Instance != null)
			{
				TimeSystem.Instance.OnMonthPassed += HandleMonthPassed;
				TimeSystem.Instance.OnSeasonalEvent += HandleSeasonalEvent;
			}

			GD.Print("[EconomySystem] Initialized and connected to TimeSystem.");
		}

		/// <summary>
		/// Handles monthly financial tick - the main economic simulation.
		/// </summary>
		private void HandleMonthPassed(GameDate date)
		{
			GD.Print($"[EconomySystem] Processing monthly tick for {date.ToDisplayString()}");

			// Reset monthly accumulators
			MonthlyRevenue = 0f;
			MonthlyExpenses = 0f;

			// Process all revenue streams and cost centers
			ProcessRevenueStreams(date);
			ProcessCostCenters(date);
			ProcessLoanRepayments(date);

			// Update GameState
			var gameState = GameState.Instance;
			if (gameState != null)
			{
				gameState.UpdateMonthlyFinancials(MonthlyRevenue, MonthlyExpenses);
				gameState.CashBalance += MonthlyNetIncome;
				
				OnCashBalanceChanged?.Invoke(gameState.CashBalance);
			}

			OnMonthlyStatementReady?.Invoke(date);
			
			GD.Print($"[EconomySystem] Monthly summary - Revenue: R{MonthlyRevenue:F2}, " +
					$"Expenses: R{MonthlyExpenses:F2}, Net: R{MonthlyNetIncome:F2}");
		}

		/// <summary>
		/// Processes all revenue streams for the current month.
		/// </summary>
		private void ProcessRevenueStreams(GameDate date)
		{
			// Trophy Hunting (seasonal)
			if (_isHuntingSeasonOpen)
			{
				// TODO: Integrate with TrophyHuntingSystem for actual bookings
				float huntingRevenue = CalculateTrophyHuntingRevenue(date);
				if (huntingRevenue > 0)
					RecordTransaction(date, huntingRevenue, "Trophy hunting fees", TransactionCategory.TrophyHunting);
			}

			// Photographic Tourism (year-round)
			// TODO: Integrate with TourismSystem for lodge occupancy
			float tourismRevenue = CalculatePhotographicTourismRevenue(date);
			if (tourismRevenue > 0)
				RecordTransaction(date, tourismRevenue, "Lodge accommodation", TransactionCategory.PhotogTourism);

			// Live Game Sales (market-driven)
			// TODO: Integrate with AnimalMarketSystem
			float gameSalesRevenue = CalculateLiveGameSalesRevenue(date);
			if (gameSalesRevenue > 0)
				RecordTransaction(date, gameSalesRevenue, "Live game sales", TransactionCategory.LiveGameSales);

			// Carbon Credits (quarterly)
			if (date.Month % 3 == 0) // March, June, September, December
			{
				// TODO: Integrate with ConservationSystem for hectares
				float carbonRevenue = CalculateCarbonCreditsRevenue(date);
				if (carbonRevenue > 0)
					RecordTransaction(date, carbonRevenue, "Carbon credit payment", TransactionCategory.CarbonCredits);
			}

			// Research Partnership (annual in January)
			if (date.Month == 1)
			{
				// TODO: Integrate with ResearchSystem for infrastructure requirements
				float researchGrant = CalculateResearchPartnershipGrant(date);
				if (researchGrant > 0)
					RecordTransaction(date, researchGrant, "Research partnership grant", TransactionCategory.ResearchPartnership);
			}
		}

		/// <summary>
		/// Processes all cost centers for the current month.
		/// </summary>
		private void ProcessCostCenters(GameDate date)
		{
			// Staff Wages (monthly)
			// TODO: Integrate with StaffSystem for payroll
			float staffCosts = CalculateStaffWages(date);
			if (staffCosts > 0)
				RecordTransaction(date, -staffCosts, "Staff wages", TransactionCategory.StaffWages);

			// Infrastructure Maintenance (monthly)
			// TODO: Integrate with InfrastructureSystem
			float maintenanceCosts = CalculateInfrastructureMaintenance(date);
			if (maintenanceCosts > 0)
				RecordTransaction(date, -maintenanceCosts, "Infrastructure maintenance", TransactionCategory.InfrastructureMaintenance);

			// Vehicle Fuel (monthly, scales with drought)
			// TODO: Integrate with VehicleSystem
			float fuelCosts = CalculateVehicleFuelCosts(date);
			if (fuelCosts > 0)
				RecordTransaction(date, -fuelCosts, "Vehicle fuel", TransactionCategory.VehicleFuel);

			// Permit Fees (annual in January)
			if (date.Month == 1)
			{
				// TODO: Integrate with ComplianceSystem
				float permitFees = CalculateAnnualPermitFees(date);
				if (permitFees > 0)
					RecordTransaction(date, -permitFees, "Annual permit renewals", TransactionCategory.PermitFees);
			}
		}

		/// <summary>
		/// Processes monthly loan repayments.
		/// </summary>
		private void ProcessLoanRepayments(GameDate date)
		{
			var gameState = GameState.Instance;
			if (gameState?.ActiveLoans == null) return;

			var loansToRemove = new List<Loan>();

			foreach (var loan in gameState.ActiveLoans.ToList())
			{
				if (loan.NextPaymentDate.IsSameMonth(date))
				{
					float payment = loan.ProcessMonthlyPayment(date);
					if (payment > 0)
					{
						RecordTransaction(date, -payment, $"Loan payment: {loan.Purpose}", TransactionCategory.LoanRepayment);
					}

					if (loan.IsFullyRepaid)
					{
						loansToRemove.Add(loan);
						GD.Print($"[EconomySystem] Loan {loan.LoanId} fully repaid!");
					}
				}
			}

			foreach (var loan in loansToRemove)
			{
				gameState.ActiveLoans.Remove(loan);
			}
		}

		/// <summary>
		/// Handles seasonal events that affect the economy.
		/// </summary>
		private void HandleSeasonalEvent(SeasonalEvent eventType, GameDate date)
		{
			switch (eventType)
			{
				case SeasonalEvent.HuntingSeasonOpen:
					_isHuntingSeasonOpen = true;
					GD.Print("[EconomySystem] Hunting season opened - revenue stream enabled");
					break;

				case SeasonalEvent.HuntingSeasonClose:
					_isHuntingSeasonOpen = false;
					GD.Print("[EconomySystem] Hunting season closed - revenue stream disabled");
					break;

				case SeasonalEvent.DroughtStart:
					_isDroughtActive = true;
					GD.Print("[EconomySystem] Drought started - increased operational costs");
					break;

				case SeasonalEvent.DroughtEnd:
					_isDroughtActive = false;
					GD.Print("[EconomySystem] Drought ended - operational costs normalized");
					break;

				case SeasonalEvent.FireRisk:
					_isFireRiskHigh = true;
					break;

				case SeasonalEvent.FirstRains:
					_isFireRiskHigh = false;
					break;
			}
		}

		/// <summary>
		/// Records a transaction and updates monthly accumulators.
		/// </summary>
		private void RecordTransaction(GameDate date, float amount, string description, TransactionCategory category)
		{
			var transaction = new Transaction(date, amount, description, category);
			
			// Add to GameState transaction history
			GameState.Instance?.AddTransaction(transaction);

			// Update monthly accumulators
			if (amount > 0)
				MonthlyRevenue += amount;
			else
				MonthlyExpenses += Math.Abs(amount);
		}

		#region Revenue Calculations (Placeholder implementations)

		private float CalculateTrophyHuntingRevenue(GameDate date)
		{
			// Placeholder: Base hunting revenue with seasonal variation
			// TODO: Replace with actual TrophyHuntingSystem integration
			float baseRevenue = 85000f; // Average monthly during season
			float seasonalMultiplier = date.Season == Season.Autumn ? 1.2f : 1.0f;
			return baseRevenue * seasonalMultiplier;
		}

		private float CalculatePhotographicTourismRevenue(GameDate date)
		{
			// Placeholder: Lodge occupancy model
			// TODO: Replace with actual TourismSystem integration
			int totalBeds = 20;
			float nightlyRate = 3500f; // ZAR per bed per night
			float occupancyRate = date.Season == Season.Summer ? 0.8f : 0.6f;
			return totalBeds * nightlyRate * 30 * occupancyRate;
		}

		private float CalculateLiveGameSalesRevenue(GameDate date)
		{
			// Placeholder: Occasional game sales
			// TODO: Replace with actual AnimalMarketSystem integration
			return date.Month % 4 == 0 ? 45000f : 0f; // Quarterly sales
		}

		private float CalculateCarbonCreditsRevenue(GameDate date)
		{
			// Placeholder: Carbon credit calculation
			// TODO: Replace with actual ConservationSystem integration
			float hectaresUnderConservation = 5000f;
			float zarPerHectareQuarterly = 75f;
			return hectaresUnderConservation * zarPerHectareQuarterly;
		}

		private float CalculateResearchPartnershipGrant(GameDate date)
		{
			// Placeholder: Annual research grant
			// TODO: Replace with actual ResearchSystem integration
			return 150000f; // Annual grant
		}

		#endregion

		#region Cost Calculations (Placeholder implementations)

		private float CalculateStaffWages(GameDate date)
		{
			// Placeholder: Basic staff costs
			// TODO: Replace with actual StaffSystem integration
			return 125000f; // Monthly payroll
		}

		private float CalculateInfrastructureMaintenance(GameDate date)
		{
			// Placeholder: Infrastructure maintenance
			// TODO: Replace with actual InfrastructureSystem integration
			float baseMaintenance = 25000f;
			return _isDroughtActive ? baseMaintenance * 1.1f : baseMaintenance;
		}

		private float CalculateVehicleFuelCosts(GameDate date)
		{
			// Placeholder: Vehicle operational costs
			// TODO: Replace with actual VehicleSystem integration
			float baseFuelCost = 18000f;
			float droughtMultiplier = _isDroughtActive ? 1.4f : 1.0f;
			return baseFuelCost * droughtMultiplier;
		}

		private float CalculateAnnualPermitFees(GameDate date)
		{
			// Placeholder: Annual permit renewals
			// TODO: Replace with actual ComplianceSystem integration
			return 35000f; // Annual DFFE and provincial permits
		}

		#endregion
	}
}
