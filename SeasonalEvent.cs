namespace BasterBoer.Core.Time
{
	/// <summary>
	/// Seasonal pressure and opportunity events affecting gameplay systems.
	/// </summary>
	public enum SeasonalEvent
	{
		/// <summary>Drought conditions begin - reduces grass, increases costs.</summary>
		DroughtStart,
		
		/// <summary>Drought conditions end - normal conditions resume.</summary>
		DroughtEnd,
		
		/// <summary>Trophy hunting season opens - enables hunting revenue.</summary>
		HuntingSeasonOpen,
		
		/// <summary>Trophy hunting season closes - disables hunting revenue.</summary>
		HuntingSeasonClose,
		
		/// <summary>Calving season - triggers breeding outcomes.</summary>
		CalvingSeason,
		
		/// <summary>Fire risk increases - potential for veld fires.</summary>
		FireRisk,
		
		/// <summary>First meaningful rains arrive - grass recovery begins.</summary>
		FirstRains
	}
}
