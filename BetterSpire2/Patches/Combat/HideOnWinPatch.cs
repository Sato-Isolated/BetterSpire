using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using System;
using System.Reflection;

namespace BetterSpire2.Patches.Combat;

/// <summary>
/// Hides combat-only overlays immediately when a victorious combat starts shutting down.
/// </summary>
[HarmonyPatch]
internal static class CombatManager_EndCombatInternal_Patch
{
    // The parameterless overload is a test wrapper; normal victories bypass it.
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod()
    {
        var turnState = typeof(CombatManager).Assembly.GetType(
            "MegaCrit.Sts2.Core.Combat.CombatTurnState", throwOnError: true)!;
        return AccessTools.DeclaredMethod(typeof(CombatManager), "EndCombatInternal", new[] { turnState })
            ?? throw new MissingMethodException("CombatManager.EndCombatInternal(CombatTurnState)");
    }
    [HarmonyPrefix]
    private static void Prefix()
    {
        Safe(DamageTracker.Hide);
        Safe(TeammateHandViewer.Hide);
        Safe(() => JournalController.OnCombatEnding(false));
        Safe(InstantSpeedHelper.OnCombatEnd);
    }
    private static void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) { ModLog.Error("CombatEnding", ex); }
    }
}
