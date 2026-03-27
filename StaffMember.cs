using System;

namespace BasterBoer.Core.Staff
{
	/// <summary>
	/// Defines the job role a staff member performs on the farm or reserve.
	/// </summary>
	public enum StaffRole
	{
		Ranger,
		Tracker,
		Guide,
		Vet,
		ProfessionalHunter,
		LodgeManager,
		Mechanic,
		SecurityGuard,
		Farmhand,
		Chef
	}

	/// <summary>
	/// Defines the current employment or health state of a staff member.
	/// </summary>
	public enum StaffStatus
	{
		Active,
		OnLeave,
		Resigned,
		Terminated,
		Sick
	}

	/// <summary>
	/// Cache-friendly data container for an individual staff member.
	/// </summary>
	public struct StaffMember
	{
		/// <summary>
		/// Unique identifier for the staff member.
		/// </summary>
		public int Id;

		/// <summary>
		/// Display name of the staff member.
		/// </summary>
		public string Name;

		/// <summary>
		/// Primary role of the staff member.
		/// </summary>
		public StaffRole Role;

		/// <summary>
		/// Current staff status.
		/// </summary>
		public StaffStatus Status;

		/// <summary>
		/// Core competency from 0 to 1.
		/// </summary>
		public float SkillLevel;

		/// <summary>
		/// Current morale from 0 to 1.
		/// </summary>
		public float Morale;

		/// <summary>
		/// Current loyalty from 0 to 1.
		/// </summary>
		public float Loyalty;

		/// <summary>
		/// Monthly salary in South African Rand.
		/// </summary>
		public float MonthlySalaryZAR;

		/// <summary>
		/// Number of full months the staff member has been employed.
		/// </summary>
		public int MonthsEmployed;

		/// <summary>
		/// Zone assignment. -1 means unassigned.
		/// </summary>
		public int AssignedZoneId;

		/// <summary>
		/// Current fatigue from 0 to 1.
		/// </summary>
		public float Fatigue;

		/// <summary>
		/// Current health from 0 to 1.
		/// </summary>
		public float Health;

		/// <summary>
		/// Creates a ranger staff member.
		/// </summary>
		public static StaffMember CreateRanger(int id, string name, float skill, float salary)
		{
			return Create(id, name, StaffRole.Ranger, skill, salary);
		}

		/// <summary>
		/// Creates a tracker staff member.
		/// </summary>
		public static StaffMember CreateTracker(int id, string name, float skill, float salary)
		{
			return Create(id, name, StaffRole.Tracker, skill, salary);
		}

		/// <summary>
		/// Creates a guide staff member.
		/// </summary>
		public static StaffMember CreateGuide(int id, string name, float skill, float salary)
		{
			return Create(id, name, StaffRole.Guide, skill, salary);
		}

		/// <summary>
		/// Creates a vet staff member.
		/// </summary>
		public static StaffMember CreateVet(int id, string name, float skill, float salary)
		{
			return Create(id, name, StaffRole.Vet, skill, salary);
		}

		/// <summary>
		/// Creates a professional hunter staff member.
		/// </summary>
		public static StaffMember CreateProfessionalHunter(int id, string name, float skill, float salary)
		{
			return Create(id, name, StaffRole.ProfessionalHunter, skill, salary);
		}

		/// <summary>
		/// Creates a lodge manager staff member.
		/// </summary>
		public static StaffMember CreateLodgeManager(int id, string name, float skill, float salary)
		{
			return Create(id, name, StaffRole.LodgeManager, skill, salary);
		}

		/// <summary>
		/// Creates a mechanic staff member.
		/// </summary>
		public static StaffMember CreateMechanic(int id, string name, float skill, float salary)
		{
			return Create(id, name, StaffRole.Mechanic, skill, salary);
		}

		/// <summary>
		/// Creates a security guard staff member.
		/// </summary>
		public static StaffMember CreateSecurityGuard(int id, string name, float skill, float salary)
		{
			return Create(id, name, StaffRole.SecurityGuard, skill, salary);
		}

		/// <summary>
		/// Creates a farmhand staff member.
		/// </summary>
		public static StaffMember CreateFarmhand(int id, string name, float skill, float salary)
		{
			return Create(id, name, StaffRole.Farmhand, skill, salary);
		}

		/// <summary>
		/// Creates a chef staff member.
		/// </summary>
		public static StaffMember CreateChef(int id, string name, float skill, float salary)
		{
			return Create(id, name, StaffRole.Chef, skill, salary);
		}

		/// <summary>
		/// Internal common factory.
		/// </summary>
		private static StaffMember Create(int id, string name, StaffRole role, float skill, float salary)
		{
			return new StaffMember
			{
				Id = id,
				Name = string.IsNullOrWhiteSpace(name) ? BuildDefaultName(id, role) : name.Trim(),
				Role = role,
				Status = StaffStatus.Active,
				SkillLevel = Math.Clamp(skill, 0f, 1f),
				Morale = GetStartingMorale(role),
				Loyalty = GetStartingLoyalty(role, skill),
				MonthlySalaryZAR = Math.Max(0f, salary),
				MonthsEmployed = 0,
				AssignedZoneId = -1,
				Fatigue = 0.10f,
				Health = 1.00f
			};
		}

		/// <summary>
		/// Creates a default South African-style name if none was provided.
		/// </summary>
		private static string BuildDefaultName(int id, StaffRole role)
		{
			string[] firstNames =
			{
				"Thabo", "Sipho", "Johan", "Pieter", "Maria",
				"Lindiwe", "Andile", "Naledi", "Kobus", "Zanele",
				"Mpho", "Sizwe", "Anika", "Bheki", "Ruan"
			};

			string[] surnames =
			{
				"Molefe", "Khumalo", "van der Merwe", "Botha", "Nkosi",
				"Mokoena", "Pretorius", "Dlamini", "Visser", "Ndlovu",
				"Smit", "Zulu", "Coetzee", "Mthembu", "Steyn"
			};

			int safeId = Math.Abs(id);
			int firstIndex = (safeId + ((int)role * 3)) % firstNames.Length;
			int surnameIndex = ((safeId * 2) + (int)role) % surnames.Length;

			return firstNames[firstIndex] + " " + surnames[surnameIndex];
		}

		/// <summary>
		/// Gets starting morale by role.
		/// </summary>
		private static float GetStartingMorale(StaffRole role)
		{
			switch (role)
			{
				case StaffRole.LodgeManager:
				case StaffRole.Chef:
					return 0.70f;

				case StaffRole.Ranger:
				case StaffRole.Tracker:
				case StaffRole.Guide:
				case StaffRole.Farmhand:
					return 0.65f;

				case StaffRole.Vet:
				case StaffRole.ProfessionalHunter:
				case StaffRole.Mechanic:
				case StaffRole.SecurityGuard:
				default:
					return 0.62f;
			}
		}

		/// <summary>
		/// Gets starting loyalty by role and skill.
		/// </summary>
		private static float GetStartingLoyalty(StaffRole role, float skill)
		{
			float baseLoyalty;

			switch (role)
			{
				case StaffRole.Farmhand:
				case StaffRole.Ranger:
				case StaffRole.Tracker:
				case StaffRole.SecurityGuard:
					baseLoyalty = 0.42f;
					break;

				case StaffRole.Chef:
				case StaffRole.Guide:
				case StaffRole.LodgeManager:
					baseLoyalty = 0.38f;
					break;

				case StaffRole.Vet:
				case StaffRole.ProfessionalHunter:
				case StaffRole.Mechanic:
				default:
					baseLoyalty = 0.34f;
					break;
			}

			baseLoyalty += Math.Clamp(skill, 0f, 1f) * 0.08f;
			return Math.Clamp(baseLoyalty, 0f, 1f);
		}
	}
}
