using System;
using BasterBoer.Core.Time;

namespace BasterBoer.Core.Economy
{
	/// <summary>
	/// Represents a bank loan with monthly repayment obligations.
	/// </summary>
	public class Loan
	{
		/// <summary>Unique identifier for this loan.</summary>
		public string LoanId { get; }

		/// <summary>Name of the lending institution.</summary>
		public string LenderName { get; }

		/// <summary>Original principal amount in ZAR.</summary>
		public float PrincipalAmount { get; }

		/// <summary>Current outstanding balance in ZAR.</summary>
		public float OutstandingBalance { get; private set; }

		/// <summary>Annual interest rate as decimal (e.g., 0.115 for 11.5%).</summary>
		public float AnnualInterestRate { get; }

		/// <summary>Fixed monthly payment amount in ZAR.</summary>
		public float MonthlyPayment { get; }

		/// <summary>Date this loan was originated.</summary>
		public GameDate OriginationDate { get; }

		/// <summary>Date of next scheduled payment.</summary>
		public GameDate NextPaymentDate { get; private set; }

		/// <summary>Purpose of the loan for display.</summary>
		public string Purpose { get; }

		/// <summary>Returns true if loan is fully repaid.</summary>
		public bool IsFullyRepaid => OutstandingBalance <= 0.01f;

		public Loan(string loanId, string lenderName, float principalAmount, float annualInterestRate, 
				   int termMonths, GameDate originationDate, string purpose = "General")
		{
			if (principalAmount <= 0)
				throw new ArgumentException("Principal must be positive.", nameof(principalAmount));
			if (annualInterestRate < 0)
				throw new ArgumentException("Interest rate cannot be negative.", nameof(annualInterestRate));
			if (termMonths <= 0)
				throw new ArgumentException("Term must be at least 1 month.", nameof(termMonths));

			LoanId = loanId ?? Guid.NewGuid().ToString();
			LenderName = lenderName ?? "Bank";
			PrincipalAmount = principalAmount;
			OutstandingBalance = principalAmount;
			AnnualInterestRate = annualInterestRate;
			OriginationDate = originationDate;
			NextPaymentDate = originationDate.AddMonths(1);
			Purpose = purpose ?? "General";

			// Calculate fixed monthly payment using amortization formula
			MonthlyPayment = CalculateMonthlyPayment(principalAmount, annualInterestRate, termMonths);
		}

		/// <summary>
		/// Calculates monthly payment using standard amortization formula.
		/// </summary>
		private static float CalculateMonthlyPayment(float principal, float annualRate, int months)
		{
			if (annualRate == 0) return principal / months; // No interest case

			float monthlyRate = annualRate / 12f;
			double numerator = principal * monthlyRate * Math.Pow(1 + monthlyRate, months);
			double denominator = Math.Pow(1 + monthlyRate, months) - 1;
			return (float)(numerator / denominator);
		}

		/// <summary>
		/// Processes monthly payment, reducing balance and advancing payment date.
		/// Returns actual amount paid.
		/// </summary>
		public float ProcessMonthlyPayment(GameDate currentDate)
		{
			if (IsFullyRepaid) return 0f;

			float paymentAmount = Math.Min(MonthlyPayment, OutstandingBalance);
			OutstandingBalance -= paymentAmount;
			NextPaymentDate = NextPaymentDate.AddMonths(1);

			// Handle floating point precision
			if (OutstandingBalance < 0.01f)
				OutstandingBalance = 0f;

			return paymentAmount;
		}

		public override string ToString()
		{
			return $"Loan {LoanId}: R{OutstandingBalance:F2} remaining, R{MonthlyPayment:F2}/month ({LenderName})";
		}
	}
}
