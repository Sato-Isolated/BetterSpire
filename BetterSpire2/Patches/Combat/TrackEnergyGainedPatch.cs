#nullable enable
using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;

namespace BetterSpire2.Patches.Combat;

/// <summary>
/// Records actual energy gained by players so the turn summary can show generated energy.
/// </summary>
[HarmonyPatch(typeof(PlayerCombatState), nameof(PlayerCombatState.GainEnergy), new[] { typeof(decimal) })]
internal static class PlayerCombatState_GainEnergy_Patch
{
	[HarmonyPrepare]
	private static bool Prepare(MethodBase? original)
	{
		if (original is not null)
		{
			return true;
		}

		ModLog.Info($"{nameof(PlayerCombatState_GainEnergy_Patch)} skipped: target method not found.");
		return false;
	}

	[HarmonyPrefix]
	private static void Prefix(PlayerCombatState __instance, ref int __state)
	{
		__state = __instance.Energy;
	}

	[HarmonyPostfix]
	private static void Postfix(PlayerCombatState __instance, int __state, Player ____player)
	{
		try
		{
			if (____player?.Creature is not { } creature)
			{
				return;
			}

			int gainedAmount = Math.Max(0, __instance.Energy - __state);
			if (gainedAmount > 0)
			{
				TurnSummaryTracker.RecordEnergyGained(creature, gainedAmount);
			}
		}
		catch (Exception ex)
		{
			ModLog.Error(nameof(PlayerCombatState_GainEnergy_Patch), ex);
		}
	}
}