using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;

namespace BetterSpire2.Patches.Combat;

/// <summary>
/// Recomputes combat damage overlays when a player locks in end turn.
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToEndTurn))]
internal static class CombatManager_SetReadyToEndTurn_Patch
{
	[HarmonyPrepare]
	private static bool Prepare(MethodBase original)
	{
		if (original is not null)
		{
			return true;
		}

		ModLog.Info($"Target method not found for {nameof(CombatManager_SetReadyToEndTurn_Patch)} - patch skipped.");
		return false;
	}

	[HarmonyPostfix]
	private static void Postfix()
	{
		DamageTracker.Recalculate();
	}
}
