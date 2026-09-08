#nullable enable
using System;
using BetterSpire2.Journal.Core;
using BetterSpire2.Journal.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Runs;

namespace BetterSpire2.Patches.Combat;

[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.Clear))]
internal static class Journal_HistoryClear_Patch
{
    [HarmonyPrefix]
    private static void Prefix(CombatHistory __instance)
    { try { JournalService.BeforeHistoryClear(__instance); } catch (Exception ex) { ModLog.Error(nameof(Journal_HistoryClear_Patch), ex); } }
}
[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
internal static class Journal_RunCleanup_Patch
{
    [HarmonyPrefix]
    private static void Prefix()
    { try { JournalService.OnRunCleanup(); } catch (Exception ex) { ModLog.Error(nameof(Journal_RunCleanup_Patch), ex); } }
}
[HarmonyPatch(typeof(Creature), nameof(Creature.HealInternal))]
internal static class Journal_Healing_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Creature __instance, out int __state)
    { __state = JournalService.CanTrack(__instance) ? __instance.CurrentHp : -1; }
    [HarmonyPostfix]
    private static void Postfix(Creature __instance, int __state)
    {
        if (__state < 0) return;
        try
        {
            JournalService.Record(__instance, __instance.PetOwner == null ? Stat.Healing : Stat.PetHealing,
                Math.Max(0, __instance.CurrentHp - __state));
        }
        catch (Exception ex) { ModLog.Error(nameof(Journal_Healing_Patch), ex); }
    }
}
