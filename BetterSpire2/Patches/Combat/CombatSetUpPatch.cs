using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;

namespace BetterSpire2.Patches.Combat;

/// <summary>
/// Keeps BetterSpire's combat overlays synchronized as soon as a new combat starts.
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetUpCombat))]
internal static class CombatManager_SetUpCombat_Patch
{
	[HarmonyPrepare]
	private static bool Prepare(MethodBase original)
	{
		if (original is not null)
		{
			return true;
		}

		ModLog.Info($"Target method not found for {nameof(CombatManager_SetUpCombat_Patch)} - patch skipped.");
		return false;
	}

	[HarmonyPostfix]
	private static void Postfix()
	{
		try
		{
			DeckTracker.OnCombatSetUp();
			TurnSummaryTracker.OnCombatSetUp();
		}
		catch (Exception ex)
		{
			ModLog.Error(nameof(CombatManager_SetUpCombat_Patch), ex);
		}
	}
}
