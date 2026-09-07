using HarmonyLib;
using System;
using MegaCrit.Sts2.Core.Combat;

namespace BetterSpire2.Patches.Combat;

/// <summary>
/// Clears combat-only overlays once the combat manager resets state.
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.Reset))]
internal static class CombatManager_Reset_Patch
{


    [HarmonyPrefix]
    private static void Prefix() => JournalController.BeforeCombatReset();

    [HarmonyPostfix]
    private static void Postfix()
    {
        Safe(DamageTracker.Hide);
        Safe(TeammateHandViewer.Stop);
        Safe(JournalController.Hide);
        Safe(InstantSpeedHelper.OnCombatEnd);
    }
    private static void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) { ModLog.Error("CombatReset", ex); }
    }
}
