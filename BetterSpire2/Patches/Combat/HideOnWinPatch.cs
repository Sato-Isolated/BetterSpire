using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;

namespace BetterSpire2.Patches.Combat;

/// <summary>
/// Hides combat-only overlays immediately when a victorious combat starts shutting down.
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.EndCombatInternal))]
internal static class CombatManager_EndCombatInternal_Patch
{
	[HarmonyPrepare]
	private static bool Prepare(MethodBase original)
	{
		if (original is not null)
		{
			return true;
		}

		ModLog.Info($"Target method not found for {nameof(CombatManager_EndCombatInternal_Patch)} - patch skipped.");
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
