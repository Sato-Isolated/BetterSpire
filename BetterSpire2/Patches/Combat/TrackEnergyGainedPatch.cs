#nullable enable
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using System;

namespace BetterSpire2.Patches.Combat;

/// <summary>
/// Records actual energy gained by players so the turn summary can show generated energy.
/// </summary>
[HarmonyPatch(typeof(PlayerCombatState), nameof(PlayerCombatState.GainEnergy), new[] { typeof(decimal) })]
internal static class PlayerCombatState_GainEnergy_Patch
{
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
                JournalController.RecordEnergyGained(creature, gainedAmount);
            }
        }
        catch (Exception ex)
        {
            ModLog.Error(nameof(PlayerCombatState_GainEnergy_Patch), ex);
        }
    }
}
