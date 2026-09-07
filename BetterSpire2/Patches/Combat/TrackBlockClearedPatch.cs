#nullable enable
using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;

namespace BetterSpire2.Patches.Combat;

/// <summary>
/// Observes the game's clear decision without re-running any relic/power hooks.
/// The supplied DLL clears Block synchronously before the first possible await.
/// A prevention callback may remove block, but that is NOT a turn expiration.
/// </summary>
[HarmonyPatch(typeof(Creature), "ClearBlock", new Type[] { })]
internal static class Creature_ClearBlock_Patch
{
    internal sealed class Capture
    {
        internal Creature Creature = null!;
        internal Capture? Previous;
        internal int? AmountAtDecision;
    }
    [ThreadStatic] private static Capture? _current;

    [HarmonyPrefix]
    private static void Prefix(Creature __instance, out Capture? __state)
    {
        __state = null;
        if (!JournalController.ShouldTrackBlockChanges(__instance)) return;
        __state = new Capture { Creature = __instance, Previous = _current };
        _current = __state;
    }
    internal static void ObserveDecision(Creature creature, bool clears)
    {
        // Only a decision inside the matching, original ClearBlock call is relevant.
        if (_current != null && ReferenceEquals(_current.Creature, creature))
            _current.AmountAtDecision = clears ? Math.Max(0, creature.Block) : null;
    }
    [HarmonyPostfix]
    private static void Postfix(Creature __instance, Task __result, Capture? __state)
    {
        try
        {
            if (__result.IsCompletedSuccessfully && __state?.AmountAtDecision is int amount && amount > 0 && __instance.Block == 0)
                JournalController.RecordBlockCleared(__instance, amount);
        }
        catch (Exception ex) { ModLog.Error(nameof(Creature_ClearBlock_Patch), ex); }
    }
    [HarmonyFinalizer]
    private static void Finalizer(Capture? __state)
    {
        // Also restore the stack if another patch/original throws. Never swallow its exception.
        if (__state != null && ReferenceEquals(_current, __state)) _current = __state.Previous;
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.ShouldClearBlock))]
internal static class Journal_BlockClearDecision_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Creature creature, bool __result)
    {
        try { Creature_ClearBlock_Patch.ObserveDecision(creature, __result); }
        catch (Exception ex) { ModLog.Error(nameof(Journal_BlockClearDecision_Patch), ex); }
    }
}
