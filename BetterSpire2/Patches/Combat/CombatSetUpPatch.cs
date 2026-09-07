using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using System;

namespace BetterSpire2.Patches.Combat;

/// <summary>
/// Keeps BetterSpire's combat overlays synchronized as soon as a new combat starts.
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetUpCombat))]
internal static class CombatManager_SetUpCombat_Patch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        Safe("InstantSpeed", InstantSpeedHelper.OnCombatStart);
        Safe("Guardian", DamageTracker.OnCombatSetUp);
        Safe("Journal", JournalController.OnCombatSetUp);
        Safe("HandViewer", TeammateHandViewer.OnCombatSetUp);
        Safe("Clock", ClockDisplay.SyncVisibility);
    }
    private static void Safe(string module, Action action)
    {
        try { action(); }
        catch (Exception ex) { ModLog.Error("CombatSetup." + module, ex); }
    }
}
