#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using BetterSpire2.Journal.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace BetterSpire2.Patches.Combat;

// A Doom batch may continue killing secondary enemies after the final primary
// enemy triggers victory. Keep the journal open until the whole native batch returns.
[HarmonyPatch]
internal static class Journal_DoomBatchScope_Patch
{
    [HarmonyPrepare]
    private static bool Prepare()
    {
        bool found = AccessTools.DeclaredMethod(typeof(DoomPower), nameof(DoomPower.DoomKill),
            new[] { typeof(IReadOnlyList<Creature>) }) != null;
        DamageObservationCoverage.Require("effect.doom-batch", found); return found;
    }
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod() => AccessTools.DeclaredMethod(typeof(DoomPower),
        nameof(DoomPower.DoomKill), new[] { typeof(IReadOnlyList<Creature>) });
    [HarmonyPrefix]
    private static void Prefix(out NativeDamageObserver.Command? __state)
    {
        __state = null;
        try { __state = NativeDamageObserver.Enter(null, null); }
        catch (Exception ex) { NativeDamageObserver.Report(ex); }
    }
    [HarmonyPostfix]
    private static void Postfix(Task __result, NativeDamageObserver.Command? __state)
    { if (__state != null) NativeDamageObserver.CompleteWhenReady(__result, __state); }
    [HarmonyFinalizer]
    private static void Finalizer(Exception? __exception, NativeDamageObserver.Command? __state)
    {
        try { __state?.Dispose(); if (__exception != null) __state?.Complete(); }
        catch (Exception ex) { NativeDamageObserver.Report(ex); }
    }
}

[HarmonyPatch]
internal static class Journal_NativeDamageScope_Patch
{
    [HarmonyPrepare]
    private static bool Prepare()
    {
        bool found = PoisonNativeMethods.ManyDamage() != null;
        DamageObservationCoverage.Require("damage.command", found); return found;
    }
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod() => PoisonNativeMethods.ManyDamage()!;
    [HarmonyPrefix]
    private static void Prefix(Creature? __4, CardModel? __5, out NativeDamageObserver.Command? __state)
    {
        __state = null;
        try { __state = NativeDamageObserver.Enter(__4, __5); }
        catch (Exception ex) { NativeDamageObserver.Report(ex); }
    }
    [HarmonyPostfix]
    private static void Postfix(Task<IEnumerable<DamageResult>> __result, NativeDamageObserver.Command? __state)
    { if (__state != null) NativeDamageObserver.CompleteWhenReady(__result, __state); }
    [HarmonyFinalizer]
    private static void Finalizer(Exception? __exception, NativeDamageObserver.Command? __state)
    {
        try { __state?.Dispose(); if (__exception != null) __state?.Complete(); }
        catch (Exception ex) { NativeDamageObserver.Report(ex); }
    }
}

[HarmonyPatch]
internal static class Journal_NativeDamageResults_Patch
{
    [HarmonyPrepare]
    private static bool Prepare()
    {
        bool found = PoisonNativeMethods.MoveNext(PoisonNativeMethods.ManyDamage()) != null;
        DamageObservationCoverage.Require("damage.results", found); return found;
    }
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod() => PoisonNativeMethods.MoveNext(PoisonNativeMethods.ManyDamage())!;
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var hp = AccessTools.PropertyGetter(typeof(DamageResult), nameof(DamageResult.UnblockedDamage));
        var overkill = AccessTools.PropertyGetter(typeof(DamageResult), nameof(DamageResult.OverkillDamage));
        // v113: foreach completed result starts with HP + overkill, before the
        // history IsInProgress/IsEnding guard. Require this structure, not a guessed offset.
        int index = code.FindIndex(c => c.Calls(hp));
        int nextGetter = index < 0 ? -1 : code.FindIndex(index + 1, c => c.Calls(overkill));
        bool valid = index >= 0 && nextGetter is >= 0 && nextGetter - index is 2 or 3 &&
            nextGetter + 1 < code.Count && code[nextGetter + 1].opcode == OpCodes.Add &&
            code.Skip(index + 1).Take(nextGetter - index - 1).All(c =>
                c.opcode.Name!.StartsWith("ldloc", StringComparison.Ordinal) ||
                c.opcode == OpCodes.Ldarg_0 || c.opcode == OpCodes.Ldfld);
        DamageObservationCoverage.Require("damage.results", valid);
        if (!valid) return code;
        code[index].opcode = OpCodes.Call;
        code[index].operand = AccessTools.Method(typeof(NativeDamageObserver), nameof(NativeDamageObserver.ReadCompletedHp));
        return code;
    }
}
