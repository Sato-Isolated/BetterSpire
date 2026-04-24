using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace BetterSpire2.Patches.Combat;

[HarmonyPatch(typeof(Creature), nameof(Creature.DamageBlockInternal))]
/// <summary>
/// Records how much player block actually absorbs during combat resolution.
/// </summary>
internal static class Creature_DamageBlockInternal_Patch
{
	[HarmonyPrepare]
	private static bool Prepare(MethodBase original)
	{
		if (original is not null)
		{
			return true;
		}

		ModLog.Info($"Target method not found for {nameof(Creature_DamageBlockInternal_Patch)} - patch skipped.");
		return false;
	}

	[HarmonyPostfix]
	private static void Postfix(Creature __instance, decimal __result)
	{
		try
		{
			int amount = (int)__result;
			if (amount > 0)
			{
				TurnSummaryTracker.RecordBlockSpent(__instance, amount);
			}
		}
		catch (Exception ex)
		{
			ModLog.Error(nameof(Creature_DamageBlockInternal_Patch), ex);
		}
	}
}