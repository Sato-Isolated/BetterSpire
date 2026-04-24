using System;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;

namespace BetterSpire2.Infrastructure;

public static class InstantSpeedHelper
{
	private static bool _combatInstantActive;

	private static FastModeType _preCombatSpeed;

	public static void OnCombatStart()
	{
		try
		{
			if (ModSettings.InstantFastMode && !_combatInstantActive)
			{
				PrefsSave prefsSave = SaveManager.Instance?.PrefsSave;
				if (prefsSave != null)
				{
					_preCombatSpeed = prefsSave.FastMode;
					prefsSave.FastMode = FastModeType.Instant;
					_combatInstantActive = true;
				}
			}
		}
		catch (Exception ex)
		{
			ModLog.Error("InstantSpeedHelper.OnCombatStart", ex);
		}
	}

	public static void OnCombatEnd()
	{
		try
		{
			if (_combatInstantActive)
			{
				PrefsSave prefsSave = SaveManager.Instance?.PrefsSave;
				if (prefsSave != null)
				{
					prefsSave.FastMode = _preCombatSpeed;
				}
				_combatInstantActive = false;
			}
		}
		catch (Exception ex)
		{
			ModLog.Error("InstantSpeedHelper.OnCombatEnd", ex);
		}
	}
}
