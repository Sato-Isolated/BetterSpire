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
        try
        {
            DeckTracker.OnCombatSetUp();
            TurnSummaryTracker.OnCombatSetUp();
        }
        catch (Exception ex)
        {
            ModLog.Error(nameof(CombatManager_SetUpCombat_Patch), ex);
        }
    }
}
