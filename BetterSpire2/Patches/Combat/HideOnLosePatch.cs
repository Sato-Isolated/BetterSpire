using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;

namespace BetterSpire2.Patches.Combat;

/// <summary>
/// Hides combat-only overlays as soon as the run is marked as lost.
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.LoseCombat))]
internal static class CombatManager_LoseCombat_Patch
{
	[HarmonyPrepare]
	private static bool Prepare(MethodBase original)
	{
		if (original is not null)
		{
			return true;
		}

		ModLog.Info($"Target method not found for {nameof(CombatManager_LoseCombat_Patch)} - patch skipped.");
		return false;
	}

	[HarmonyPrefix]
	private static void Prefix()
	{
		DamageTracker.Hide();
		DeckTracker.Hide();
		TurnSummaryTracker.Hide();
		InstantSpeedHelper.OnCombatEnd();
	}
}
