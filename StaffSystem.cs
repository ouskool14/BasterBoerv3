using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using BasterBoer.Core.Staff;
using BasterBoer.Core.Economy;
using BasterBoer.Core.Time;

namespace BasterBoer.Core.Systems
{
	/// <summary>
	/// Aggregate statistics for the current staff roster.
	/// </summary>
	public struct StaffStats
	{
		/// <summary>
		/// Total currently employed staff, excluding resigned and terminated staff.
		/// </summary>
		public int TotalStaff;

		/// <summary>
		/// Number of staff currently in active duty.
		/// </summary>
		public int ActiveStaff;

		/// <summary>
		/// Average morale of currently employed staff.
		/// </summary>
		public float AverageMorale;

		/// <summary>
		/// Average loyalty of currently employed staff.
		/// </summary>
		public float AverageLoyalty;

		/// <summary>
		/// Monthly payroll of currently employed staff in ZAR.
		/// </summary>
		public float MonthlyPayroll;

		/// <summary>
		/// Count of staff by role.
		/// </summary>
		public Dictionary<StaffRole, int> StaffByRole;
	}

	/// <summary>
	/// Central singleton managing all staff simulation, payroll, morale, loyalty, and assignments.
	/// </summary>
	public sealed class StaffSystem
	{
		private static StaffSystem _instance;

		/// <summary>
		/// Global singleton instance.
		/// </summary>
		public static StaffSystem Instance => _instance ??= new StaffSystem();

		private readonly List<StaffMember> _staff;
		private readonly List<Transaction> _salaryTransactions;
		private readonly Random _random;

		private int _nextStaffId;

		private Type _gameStateType;
		private PropertyInfo _cashBalanceProperty;
		private FieldInfo _cashBalanceField;

		/// <summary>
		/// Fired when a staff member is successfully hired.
		/// </summary>
		public event Action<StaffMember> OnStaffHired;

		/// <summary>
		/// Fired when a staff member resigns.
		/// </summary>
		public event Action<StaffMember, string> OnStaffResigned;

		/// <summary>
		/// Fired for generic staff events such as illness, mistakes, equipment issues, or initiative.
		/// </summary>
		public event Action<StaffMember, string> OnStaffEvent;

		/// <summary>
		/// Read-only access to the full staff list, including resigned and terminated staff.
		/// </summary>
		public IReadOnlyList<StaffMember> Staff => _staff;

		/// <summary>
		/// Read-only access to recorded salary transactions that were successfully created.
		/// </summary>
		public IReadOnlyList<Transaction> SalaryTransactions => _salaryTransactions;

		/// <summary>
		/// Private constructor for singleton pattern.
		/// </summary>
		private StaffSystem()
		{
			_staff = new List<StaffMember>(64);
			_salaryTransactions = new List<Transaction>(256);
			_random = new Random();
			_nextStaffId = 1;
		}

		/// <summary>
		/// Hires a staff member, assigning a unique ID if required, and returns the stored staff record.
		/// </summary>
		public StaffMember HireStaff(StaffMember staff)
		{
			if (staff.Id <= 0 || FindStaffIndexById(staff.Id) >= 0)
			{
				staff.Id = _nextStaffId++;
			}
			else
			{
				_nextStaffId = Math.Max(_nextStaffId, staff.Id + 1);
			}

			staff.Name = string.IsNullOrWhiteSpace(staff.Name) ? "Unnamed Staff" : staff.Name.Trim();
			staff.Status = StaffStatus.Active;
			staff.SkillLevel = Math.Clamp(staff.SkillLevel, 0f, 1f);
			staff.Morale = Math.Clamp(staff.Morale, 0f, 1f);
			staff.Loyalty = Math.Clamp(staff.Loyalty, 0f, 1f);
			staff.MonthlySalaryZAR = Math.Max(0f, staff.MonthlySalaryZAR);
			staff.AssignedZoneId = staff.AssignedZoneId < 0 ? -1 : staff.AssignedZoneId;
			staff.Fatigue = Math.Clamp(staff.Fatigue, 0f, 1f);
			staff.Health = Math.Clamp(staff.Health <= 0f ? 1f : staff.Health, 0f, 1f);

			_staff.Add(staff);

			GD.Print($"[StaffSystem] Hired {staff.Name} ({staff.Role}) for R{staff.MonthlySalaryZAR:0.00}/month.");
			OnStaffHired?.Invoke(staff);

			return staff;
		}

		/// <summary>
		/// Terminates a staff member. Optionally pays one month of severance.
		/// </summary>
		public bool TerminateStaff(int staffId, bool withSeverance)
		{
			int index = FindStaffIndexById(staffId);
			if (index < 0)
			{
				GD.Print($"[StaffSystem] Terminate failed. Staff ID {staffId} not found.");
				return false;
			}

			StaffMember staff = _staff[index];
			if (staff.Status == StaffStatus.Terminated || staff.Status == StaffStatus.Resigned)
			{
				GD.Print($"[StaffSystem] Terminate ignored. {staff.Name} is already inactive.");
				return false;
			}

			if (withSeverance && staff.MonthlySalaryZAR > 0f)
			{
				if (TryDeductCashBalance(staff.MonthlySalaryZAR))
				{
					GameDate now = TimeSystem.Instance?.CurrentDate ?? new GameDate(2024, 1, 1);
					TryCreateFinancialTransaction(
						now,
						staff.MonthlySalaryZAR,
						"Severance paid to " + staff.Name,
						TransactionCategory.StaffWages);

					GD.Print($"[StaffSystem] Paid severance of R{staff.MonthlySalaryZAR:0.00} to {staff.Name}.");
				}
				else
				{
					GD.Print($"[StaffSystem] Could not deduct severance from cash balance for {staff.Name}.");
				}
			}

			staff.Status = StaffStatus.Terminated;
			staff.AssignedZoneId = -1;
			staff.Morale = Math.Clamp(staff.Morale - 0.20f, 0f, 1f);
			_staff[index] = staff;

			GD.Print($"[StaffSystem] Terminated {staff.Name}.");
			OnStaffEvent?.Invoke(staff, withSeverance
				? $"{staff.Name} was terminated with severance."
				: $"{staff.Name} was terminated without severance.");

			return true;
		}

		/// <summary>
		/// Gets a staff member by ID, or null if not found.
		/// </summary>
		public StaffMember? GetStaffById(int id)
		{
			int index = FindStaffIndexById(id);
			if (index < 0)
			{
				return null;
			}

			return _staff[index];
		}

		/// <summary>
		/// Gets all currently employed staff of a given role.
		/// </summary>
		public List<StaffMember> GetStaffByRole(StaffRole role)
		{
			List<StaffMember> results = new List<StaffMember>();

			for (int i = 0; i < _staff.Count; i++)
			{
				StaffMember staff = _staff[i];
				if (staff.Role == role && IsCurrentEmployee(staff.Status))
				{
					results.Add(staff);
				}
			}

			return results;
		}

		/// <summary>
		/// Assigns a staff member to a zone.
		/// </summary>
		public bool AssignToZone(int staffId, int zoneId)
		{
			int index = FindStaffIndexById(staffId);
			if (index < 0)
			{
				GD.Print($"[StaffSystem] Assign failed. Staff ID {staffId} not found.");
				return false;
			}

			StaffMember staff = _staff[index];
			if (!CanBeAssigned(staff.Status))
			{
				GD.Print($"[StaffSystem] Assign failed. {staff.Name} is not available for assignment.");
				return false;
			}

			staff.AssignedZoneId = zoneId;
			_staff[index] = staff;

			GD.Print($"[StaffSystem] Assigned {staff.Name} to zone {zoneId}.");
			return true;
		}

		/// <summary>
		/// Removes a zone assignment from a staff member.
		/// </summary>
		public bool UnassignStaff(int staffId)
		{
			int index = FindStaffIndexById(staffId);
			if (index < 0)
			{
				GD.Print($"[StaffSystem] Unassign failed. Staff ID {staffId} not found.");
				return false;
			}

			StaffMember staff = _staff[index];
			staff.AssignedZoneId = -1;
			_staff[index] = staff;

			GD.Print($"[StaffSystem] Unassigned {staff.Name}.");
			return true;
		}

		/// <summary>
		/// Gets all currently employed staff assigned to a given zone.
		/// </summary>
		public List<StaffMember> GetStaffInZone(int zoneId)
		{
			List<StaffMember> results = new List<StaffMember>();

			for (int i = 0; i < _staff.Count; i++)
			{
				StaffMember staff = _staff[i];
				if (staff.AssignedZoneId == zoneId && IsCurrentEmployee(staff.Status))
				{
					results.Add(staff);
				}
			}

			return results;
		}

		/// <summary>
		/// Daily simulation tick. Updates fatigue, health, morale drift, minor events, and tiny skill gains.
		/// </summary>
		public void OnDailyTick()
		{
			for (int i = 0; i < _staff.Count; i++)
			{
				StaffMember staff = _staff[i];

				if (staff.Status == StaffStatus.Resigned || staff.Status == StaffStatus.Terminated)
				{
					continue;
				}

				switch (staff.Status)
				{
					case StaffStatus.Active:
						SimulateActiveDaily(ref staff);
						break;

					case StaffStatus.OnLeave:
						SimulateLeaveDaily(ref staff);
						break;

					case StaffStatus.Sick:
						SimulateSickDaily(ref staff);
						break;
				}

				staff.SkillLevel = Math.Clamp(staff.SkillLevel, 0f, 1f);
				staff.Morale = Math.Clamp(staff.Morale, 0f, 1f);
				staff.Loyalty = Math.Clamp(staff.Loyalty, 0f, 1f);
				staff.Fatigue = Math.Clamp(staff.Fatigue, 0f, 1f);
				staff.Health = Math.Clamp(staff.Health, 0f, 1f);

				_staff[i] = staff;
			}
		}

		/// <summary>
		/// Monthly simulation tick. Processes salaries, morale and loyalty shifts, resignations, tenure, and larger random events.
		/// </summary>
		public void OnMonthlyTick()
		{
			bool[] salaryPaid = ProcessAllSalaries();

			for (int i = 0; i < _staff.Count; i++)
			{
				StaffMember staff = _staff[i];

				if (!IsCurrentEmployee(staff.Status))
				{
					continue;
				}

				staff.MonthsEmployed += 1;

				UpdateMonthlyMoraleAndLoyalty(ref staff, salaryPaid[i]);
				ApplyMonthlyRecovery(ref staff);
				HandleMonthlyRandomEvents(ref staff, salaryPaid[i]);
				EvaluatePotentialResignation(ref staff, salaryPaid[i]);

				staff.SkillLevel = Math.Clamp(staff.SkillLevel, 0f, 1f);
				staff.Morale = Math.Clamp(staff.Morale, 0f, 1f);
				staff.Loyalty = Math.Clamp(staff.Loyalty, 0f, 1f);
				staff.Fatigue = Math.Clamp(staff.Fatigue, 0f, 1f);
				staff.Health = Math.Clamp(staff.Health, 0f, 1f);

				_staff[i] = staff;
			}

			StaffStats stats = GetStats();
			GD.Print($"[StaffSystem] Monthly tick complete. Staff: {stats.TotalStaff}, Active: {stats.ActiveStaff}, Payroll: R{stats.MonthlyPayroll:0.00}");
		}

		/// <summary>
		/// Returns overall staff effectiveness from 0 to 1.
		/// </summary>
		public float GetEffectiveness(int staffId)
		{
			int index = FindStaffIndexById(staffId);
			if (index < 0)
			{
				return 0f;
			}

			StaffMember staff = _staff[index];

			float statusFactor;
			switch (staff.Status)
			{
				case StaffStatus.Active:
					statusFactor = 1f;
					break;

				case StaffStatus.Sick:
					statusFactor = 0.20f;
					break;

				case StaffStatus.OnLeave:
				case StaffStatus.Resigned:
				case StaffStatus.Terminated:
				default:
					statusFactor = 0f;
					break;
			}

			float restFactor = 1f - staff.Fatigue;
			float baseEffectiveness =
				(staff.SkillLevel * 0.50f) +
				(staff.Morale * 0.20f) +
				(restFactor * 0.15f) +
				(staff.Health * 0.15f);

			if (staff.Role == StaffRole.Ranger || staff.Role == StaffRole.Tracker)
			{
				baseEffectiveness += staff.Loyalty * 0.05f;
			}

			return Math.Clamp(baseEffectiveness * statusFactor, 0f, 1f);
		}

		/// <summary>
		/// Builds current roster statistics.
		/// </summary>
		public StaffStats GetStats()
		{
			StaffStats stats = new StaffStats
			{
				TotalStaff = 0,
				ActiveStaff = 0,
				AverageMorale = 0f,
				AverageLoyalty = 0f,
				MonthlyPayroll = 0f,
				StaffByRole = new Dictionary<StaffRole, int>()
			};

			for (int r = 0; r < Enum.GetValues(typeof(StaffRole)).Length; r++)
			{
				stats.StaffByRole[(StaffRole)r] = 0;
			}

			float moraleSum = 0f;
			float loyaltySum = 0f;

			for (int i = 0; i < _staff.Count; i++)
			{
				StaffMember staff = _staff[i];
				if (!IsCurrentEmployee(staff.Status))
				{
					continue;
				}

				stats.TotalStaff += 1;
				stats.MonthlyPayroll += Math.Max(0f, staff.MonthlySalaryZAR);
				moraleSum += staff.Morale;
				loyaltySum += staff.Loyalty;
				stats.StaffByRole[staff.Role] = stats.StaffByRole[staff.Role] + 1;

				if (staff.Status == StaffStatus.Active)
				{
					stats.ActiveStaff += 1;
				}
			}

			if (stats.TotalStaff > 0)
			{
				stats.AverageMorale = moraleSum / stats.TotalStaff;
				stats.AverageLoyalty = loyaltySum / stats.TotalStaff;
			}

			return stats;
		}

		/// <summary>
		/// Finds the index of a staff record by ID.
		/// </summary>
		private int FindStaffIndexById(int staffId)
		{
			for (int i = 0; i < _staff.Count; i++)
			{
				if (_staff[i].Id == staffId)
				{
					return i;
				}
			}

			return -1;
		}

		/// <summary>
		/// Returns true if the staff member should count as currently employed.
		/// </summary>
		private bool IsCurrentEmployee(StaffStatus status)
		{
			return status == StaffStatus.Active ||
				   status == StaffStatus.OnLeave ||
				   status == StaffStatus.Sick;
		}

		/// <summary>
		/// Returns true if the staff member can be assigned to work.
		/// </summary>
		private bool CanBeAssigned(StaffStatus status)
		{
			return status == StaffStatus.Active;
		}

		/// <summary>
		/// Returns true if the staff member is on payroll.
		/// </summary>
		private bool IsOnPayroll(StaffStatus status)
		{
			return status == StaffStatus.Active ||
				   status == StaffStatus.OnLeave ||
				   status == StaffStatus.Sick;
		}

		/// <summary>
		/// Simulates a normal working day.
		/// </summary>
		private void SimulateActiveDaily(ref StaffMember staff)
		{
			float workload = CalculateDailyWorkload(staff);
			float fatigueGain = workload * (0.85f + ((1f - staff.SkillLevel) * 0.35f));
			float moraleDrift = (staff.Loyalty - 0.50f) * 0.0015f - (workload * 0.015f);

			staff.Fatigue = Math.Clamp(staff.Fatigue + fatigueGain, 0f, 1f);
			staff.Health = Math.Clamp(staff.Health - Math.Max(0f, staff.Fatigue - 0.85f) * 0.02f, 0f, 1f);
			staff.Morale = Math.Clamp(staff.Morale + moraleDrift, 0f, 1f);

			float skillGain = 0.00015f + (staff.Morale * 0.00010f);
			if (staff.Morale > 0.80f)
			{
				skillGain += 0.00010f;
			}

			staff.SkillLevel = Math.Clamp(staff.SkillLevel + skillGain, 0f, 1f);

			float sickChance = 0.0015f + (staff.Fatigue * 0.004f);
			if (RandomRoll(sickChance))
			{
				staff.Status = StaffStatus.Sick;
				staff.Morale = Math.Clamp(staff.Morale - 0.04f, 0f, 1f);
				staff.Health = Math.Clamp(staff.Health - 0.06f, 0f, 1f);
				OnStaffEvent?.Invoke(staff, $"{staff.Name} called in sick.");
				return;
			}

			float equipmentIssueChance = staff.AssignedZoneId >= 0 ? 0.0035f : 0.0010f;
			if (staff.Role != StaffRole.Mechanic && RandomRoll(equipmentIssueChance))
			{
				staff.Morale = Math.Clamp(staff.Morale - 0.02f, 0f, 1f);
				staff.Fatigue = Math.Clamp(staff.Fatigue + 0.03f, 0f, 1f);
				OnStaffEvent?.Invoke(staff, $"{staff.Name} lost time to an equipment issue.");
			}

			if (staff.Morale < 0.30f)
			{
				float mistakeChance = 0.008f + ((0.30f - staff.Morale) * 0.040f);
				if (RandomRoll(mistakeChance))
				{
					staff.Morale = Math.Clamp(staff.Morale - 0.015f, 0f, 1f);
					OnStaffEvent?.Invoke(staff, $"{staff.Name} made a costly mistake on duty.");
				}
			}

			if (staff.Morale > 0.80f)
			{
				float initiativeChance = 0.010f + (staff.Loyalty * 0.010f);
				if (RandomRoll(initiativeChance))
				{
					staff.Morale = Math.Clamp(staff.Morale + 0.02f, 0f, 1f);
					staff.SkillLevel = Math.Clamp(staff.SkillLevel + 0.0015f, 0f, 1f);
					OnStaffEvent?.Invoke(staff, $"{staff.Name} showed initiative and improved operations.");
				}
			}
		}

		/// <summary>
		/// Simulates a day while on leave.
		/// </summary>
		private void SimulateLeaveDaily(ref StaffMember staff)
		{
			staff.Fatigue = Math.Clamp(staff.Fatigue - 0.06f, 0f, 1f);
			staff.Health = Math.Clamp(staff.Health + 0.02f, 0f, 1f);
			staff.Morale = Math.Clamp(staff.Morale + 0.005f, 0f, 1f);

			if (RandomRoll(0.025f))
			{
				staff.Status = StaffStatus.Active;
				OnStaffEvent?.Invoke(staff, $"{staff.Name} returned from leave.");
			}
		}

		/// <summary>
		/// Simulates a day while sick.
		/// </summary>
		private void SimulateSickDaily(ref StaffMember staff)
		{
			staff.Fatigue = Math.Clamp(staff.Fatigue - 0.05f, 0f, 1f);
			staff.Health = Math.Clamp(staff.Health + 0.04f, 0f, 1f);
			staff.Morale = Math.Clamp(staff.Morale - 0.003f, 0f, 1f);

			if (staff.Health > 0.75f && RandomRoll(0.30f))
			{
				staff.Status = StaffStatus.Active;
				OnStaffEvent?.Invoke(staff, $"{staff.Name} recovered and returned to duty.");
			}
		}

		/// <summary>
		/// Computes daily workload pressure.
		/// </summary>
		private float CalculateDailyWorkload(StaffMember staff)
		{
			float baseLoad = staff.AssignedZoneId >= 0 ? 0.030f : 0.010f;

			switch (staff.Role)
			{
				case StaffRole.Ranger:
				case StaffRole.Tracker:
				case StaffRole.ProfessionalHunter:
				case StaffRole.SecurityGuard:
				case StaffRole.Farmhand:
					baseLoad += 0.010f;
					break;

				case StaffRole.Vet:
				case StaffRole.Mechanic:
					baseLoad += 0.006f;
					break;

				case StaffRole.LodgeManager:
				case StaffRole.Guide:
				case StaffRole.Chef:
					baseLoad += 0.004f;
					break;
			}

			return Math.Clamp(baseLoad, 0f, 1f);
		}

		/// <summary>
		/// Processes salaries for all staff. Returns an array indicating if each was paid.
		/// </summary>
		private bool[] ProcessAllSalaries()
		{
			bool[] results = new bool[_staff.Count];
			GameDate now = TimeSystem.Instance?.CurrentDate ?? new GameDate(2024, 1, 1);

			for (int i = 0; i < _staff.Count; i++)
			{
				StaffMember staff = _staff[i];
				if (!IsOnPayroll(staff.Status) || staff.MonthlySalaryZAR <= 0f)
				{
					results[i] = true;
					continue;
				}

				if (TryDeductCashBalance(staff.MonthlySalaryZAR))
				{
					TryCreateFinancialTransaction(
						now,
						staff.MonthlySalaryZAR,
						"Monthly salary paid to " + staff.Name,
						TransactionCategory.StaffWages);
					results[i] = true;
				}
				else
				{
					GD.PrintErr($"[StaffSystem] FAILED to pay salary to {staff.Name}. Insufficient funds.");
					results[i] = false;
				}
			}

			return results;
		}

		private void UpdateMonthlyMoraleAndLoyalty(ref StaffMember staff, bool salaryPaid)
		{
			if (salaryPaid)
			{
				staff.Loyalty = Math.Clamp(staff.Loyalty + 0.015f, 0f, 1f);
				staff.Morale = Math.Clamp(staff.Morale + 0.05f, 0f, 1f);
			}
			else
			{
				staff.Loyalty = Math.Clamp(staff.Loyalty - 0.15f, 0f, 1f);
				staff.Morale = Math.Clamp(staff.Morale - 0.40f, 0f, 1f);
				OnStaffEvent?.Invoke(staff, $"{staff.Name} did not receive their salary this month!");
			}

			// Drift towards neutral
			staff.Morale = Mathf.Lerp(staff.Morale, 0.5f, 0.05f);
		}

		private void ApplyMonthlyRecovery(ref StaffMember staff)
		{
			staff.Health = Math.Clamp(staff.Health + 0.10f, 0f, 1f);
			staff.Fatigue = Math.Clamp(staff.Fatigue - 0.40f, 0f, 1f);
		}

		private void HandleMonthlyRandomEvents(ref StaffMember staff, bool salaryPaid)
		{
			if (RandomRoll(0.05f))
			{
				staff.Morale = Math.Clamp(staff.Morale + 0.15f, 0f, 1f);
				staff.Loyalty = Math.Clamp(staff.Loyalty + 0.05f, 0f, 1f);
				OnStaffEvent?.Invoke(staff, $"{staff.Name} had a personal breakthrough or positive family news.");
			}

			if (RandomRoll(0.02f))
			{
				staff.Health = Math.Clamp(staff.Health - 0.20f, 0f, 1f);
				staff.Status = StaffStatus.Sick;
				OnStaffEvent?.Invoke(staff, $"{staff.Name} caught a severe seasonal illness.");
			}
		}

		private void EvaluatePotentialResignation(ref StaffMember staff, bool salaryPaid)
		{
			float resignationChance = 0f;

			if (!salaryPaid) resignationChance += 0.35f;
			if (staff.Morale < 0.20f) resignationChance += 0.15f;
			if (staff.Loyalty < 0.10f) resignationChance += 0.25f;

			if (resignationChance > 0f && RandomRoll(resignationChance))
			{
				staff.Status = StaffStatus.Resigned;
				staff.AssignedZoneId = -1;
				OnStaffResigned?.Invoke(staff, !salaryPaid ? "Non-payment of salary" : "Burnout and low morale");
				GD.Print($"[StaffSystem] {staff.Name} has resigned due to poor conditions.");
			}
		}

		private bool TryDeductCashBalance(float amount)
		{
			// Integration placeholder: 
			// In final BasterBoer architecture, this should call EconomySystem.Instance.DeductCash()
			// For now we assume success for testing.
			return true;
		}

		private void TryCreateFinancialTransaction(GameDate date, float amount, string desc, TransactionCategory category)
		{
			// Using the correct Transaction constructor from Transaction.cs
			var transaction = new Transaction(date, -amount, desc, category);
			_salaryTransactions.Add(transaction);
		}

		private bool RandomRoll(float chance)
		{
			return (float)_random.NextDouble() < chance;
		}
	}
}
