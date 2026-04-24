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
	

	[HarmonyPostfix]
	private static void Postfix()
	{
		DamageTracker.Recalculate();
	}
}
