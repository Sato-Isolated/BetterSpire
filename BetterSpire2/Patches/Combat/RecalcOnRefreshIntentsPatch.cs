using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace BetterSpire2.Patches.Combat;

/// <summary>
/// Refreshes BetterSpire's combat overlays whenever creature intents are rebuilt.
/// </summary>
[HarmonyPatch(typeof(NCreature), nameof(NCreature.RefreshIntents))]
internal static class NCreature_RefreshIntents_Patch
{
	

	[HarmonyPostfix]
	private static void Postfix()
	{
		InstantSpeedHelper.OnCombatStart();
		DamageTracker.Recalculate();
	}
}
