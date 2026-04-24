using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace BetterSpire2.Patches.Combat;

/// <summary>
/// Records when the game fully clears a player's block at turn transitions.
/// </summary>
[HarmonyPatch(typeof(Creature), "ClearBlock", new Type[] { })]
internal static class Creature_ClearBlock_Patch
{
	[HarmonyPrepare]
	private static bool Prepare(MethodBase original)
	{
		if (original is not null)
		{
			return true;
		}

		ModLog.Info($"Target method not found for {nameof(Creature_ClearBlock_Patch)} - patch skipped.");
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
				TurnSummaryTracker.RecordBlockCleared(__instance, amount);
			}
		}
		catch (Exception ex)
		{
			ModLog.Error(nameof(Creature_ClearBlock_Patch), ex);
		}
	}
}