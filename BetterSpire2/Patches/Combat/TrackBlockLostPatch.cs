using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace BetterSpire2.Patches.Combat;

[HarmonyPatch(typeof(Creature), nameof(Creature.LoseBlockInternal))]
/// <summary>
/// Records player block that disappears outside of normal damage absorption.
/// </summary>
internal static class Creature_LoseBlockInternal_Patch
{
	[HarmonyPrepare]
	private static bool Prepare(MethodBase original)
	{
		if (original is not null)
		{
			return true;
		}

		ModLog.Info($"Target method not found for {nameof(Creature_LoseBlockInternal_Patch)} - patch skipped.");
		return false;
	}

	[HarmonyPrefix]
	private static void Prefix(Creature __instance, ref int __state)
	{
		__state = TurnSummaryTracker.ShouldTrackBlockChanges(__instance) ? __instance.Block : 0;
	}

	[HarmonyPostfix]
	private static void Postfix(Creature __instance, int __state)
	{
		try
		{
			int amount = Math.Max(0, __state - __instance.Block);
			if (amount > 0)
			{
				TurnSummaryTracker.RecordBlockLost(__instance, amount);
			}
		}
		catch (Exception ex)
		{
			ModLog.Error(nameof(Creature_LoseBlockInternal_Patch), ex);
		}
	}
}