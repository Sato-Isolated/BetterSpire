using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;

namespace BetterSpire2.Patches.Combat;

/// <summary>
/// Hides combat-only overlays immediately when a victorious combat starts shutting down.
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.EndCombatInternal))]
internal static class CombatManager_EndCombatInternal_Patch
{
	

	[HarmonyPrefix]
	private static void Prefix()
	{
		DamageTracker.Hide();
		DeckTracker.Hide();
		TurnSummaryTracker.Hide();
		InstantSpeedHelper.OnCombatEnd();
	}
}
