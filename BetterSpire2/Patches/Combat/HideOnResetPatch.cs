using BetterSpire2.Runtime;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;

namespace BetterSpire2.Patches.Combat;

/// <summary>
/// Clears combat-only overlays once the combat manager resets state.
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.Reset))]
internal static class CombatManager_Reset_Patch
{
    [HarmonyPrefix]
    private static void Prefix() => CombatLifecycle.BeforeReset();
    [HarmonyPostfix]
    private static void Postfix() => CombatLifecycle.AfterReset();
}
