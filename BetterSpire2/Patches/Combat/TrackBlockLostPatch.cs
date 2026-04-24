using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace BetterSpire2.Patches.Combat;

[HarmonyPatch(typeof(Creature), nameof(Creature.LoseBlockInternal))]
public class TrackBlockLostPatch
{
	private static void Prefix(Creature __instance, ref int __state)
	{
		__state = TurnSummaryTracker.ShouldTrackBlockChanges(__instance) ? __instance.Block : 0;
	}

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
			ModLog.Error("TrackBlockLostPatch", ex);
		}
	}
}