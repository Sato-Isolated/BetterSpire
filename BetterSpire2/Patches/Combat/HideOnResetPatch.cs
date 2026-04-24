using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;

namespace BetterSpire2.Patches.Combat;

/// <summary>
/// Clears combat-only overlays once the combat manager resets state.
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.Reset))]
internal static class CombatManager_Reset_Patch
{
	[HarmonyPrepare]
	private static bool Prepare(MethodBase original)
	{
		if (original is not null)
		{
			return true;
		}

		ModLog.Info($"Target method not found for {nameof(CombatManager_Reset_Patch)} - patch skipped.");
		return false;
	}

	[HarmonyPostfix]
	private static void Postfix()
	{
		DamageTracker.Hide();
		TurnSummaryTracker.Hide();
	}
}
