using BetterSpire2.Runtime;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;

namespace BetterSpire2.Patches.Combat;

/// <summary>
/// Hides combat-only overlays as soon as the run is marked as lost.
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.LoseCombat))]
internal static class CombatManager_LoseCombat_Patch
{
    [HarmonyPrefix]
    private static void Prefix() => CombatLifecycle.Ending(lost: true);
}
