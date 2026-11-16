#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BetterSpire2.Journal.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace BetterSpire2.Patches.Combat;

internal static class EffectNativeMethods
{
    internal static bool Available(string capability, bool supported)
    { DamageObservationCoverage.Require(capability, supported); return supported; }
    internal static MethodInfo? SingleKill() => AccessTools.DeclaredMethod(typeof(CreatureCmd),
        nameof(CreatureCmd.Kill), new[] { typeof(Creature), typeof(CardModel), typeof(bool) });
    internal static MethodInfo? KillCore() => AccessTools.DeclaredMethod(typeof(CreatureCmd),
        "KillWithoutCheckingWinCondition", new[] { typeof(Creature), typeof(CardModel), typeof(bool), typeof(int) });
    internal static MethodInfo? StrangleMoveNext() => PoisonNativeMethods.MoveNext(
        AccessTools.DeclaredMethod(typeof(StranglePower), nameof(StranglePower.AfterCardPlayed)));
    internal static MethodInfo? DoomMoveNext() => PoisonNativeMethods.MoveNext(
        AccessTools.DeclaredMethod(typeof(DoomPower), nameof(DoomPower.DoomKill)));
    internal static MethodInfo LoseHp() => AccessTools.DeclaredMethod(typeof(Creature),
        nameof(Creature.LoseHpInternal), new[] { typeof(decimal), typeof(ValueProp) });
}

[HarmonyPatch]
internal static class Journal_StrangleDamageOrigin_Patch
{
    [HarmonyPrepare]
    private static bool Prepare() => EffectNativeMethods.Available("effect.strangle",
        EffectNativeMethods.StrangleMoveNext() != null && PoisonNativeMethods.SingleDamage() != null);
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod() => EffectNativeMethods.StrangleMoveNext()!;
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        var original = PoisonNativeMethods.SingleDamage()!;
        var fields = __originalMethod.DeclaringType!.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(StranglePower)).ToArray();
        if (fields.Length != 1 || code.Count(c => c.Calls(original)) != 1)
        { DamageObservationCoverage.Require("effect.strangle", false); return code; }
        DamageObservationCoverage.Require("effect.strangle", true);
        int index = code.FindIndex(c => c.Calls(original));
        var call = code[index];
        var loadThis = new CodeInstruction(OpCodes.Ldarg_0);
        loadThis.labels.AddRange(call.labels); call.labels.Clear();
        foreach (var block in call.blocks.Where(b => b.blockType != ExceptionBlockType.EndExceptionBlock).ToArray())
        { loadThis.blocks.Add(block); call.blocks.Remove(block); }
        call.opcode = OpCodes.Call;
        call.operand = AccessTools.Method(typeof(EffectDamageObserver), nameof(EffectDamageObserver.StrangleDamage));
        code.Insert(index, loadThis);
        code.Insert(index + 1, new CodeInstruction(OpCodes.Ldfld, fields[0]));
        return code;
    }
}

[HarmonyPatch]
internal static class Journal_DoomKillOrigin_Patch
{
    [HarmonyPrepare]
    private static bool Prepare() => EffectNativeMethods.Available("effect.doom",
        EffectNativeMethods.DoomMoveNext() != null && EffectNativeMethods.SingleKill() != null);
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod() => EffectNativeMethods.DoomMoveNext()!;
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var original = EffectNativeMethods.SingleKill()!;
        if (code.Count(c => c.Calls(original)) != 1)
        { DamageObservationCoverage.Require("effect.doom", false); return code; }
        DamageObservationCoverage.Require("effect.doom", true);
        var call = code.Single(c => c.Calls(original));
        call.opcode = OpCodes.Call;
        call.operand = AccessTools.Method(typeof(EffectDamageObserver), nameof(EffectDamageObserver.DoomKill));
        return code;
    }
}

[HarmonyPatch]
internal static class Journal_EffectCommandScope_Patch
{
    [HarmonyPrepare]
    private static bool Prepare() => EffectNativeMethods.Available("effect.scope",
        PoisonNativeMethods.ManyDamage() != null && EffectNativeMethods.KillCore() != null);
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods()
    { yield return PoisonNativeMethods.ManyDamage()!; yield return EffectNativeMethods.KillCore()!; }
    [HarmonyPrefix]
    private static void Prefix(out IDisposable? __state)
    {
        __state = null;
        try { __state = EffectDamageObserver.EnterCommand(); }
        catch (Exception ex) { ModLog.Error("Journal.EffectScope", ex); }
    }
    // Restore the caller when the async native method returns its original task.
    // Its execution context retains the scope across awaits, including nested kills.
    [HarmonyFinalizer]
    private static void Finalizer(IDisposable? __state) => __state?.Dispose();
}

[HarmonyPatch]
internal static class Journal_EffectHpResult_Patch
{
    [HarmonyPrepare]
    private static bool Prepare() => EffectNativeMethods.Available("effect.hp",
        PoisonNativeMethods.MoveNext(PoisonNativeMethods.ManyDamage()) != null &&
        PoisonNativeMethods.MoveNext(EffectNativeMethods.KillCore()) != null);
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return PoisonNativeMethods.MoveNext(PoisonNativeMethods.ManyDamage())!;
        yield return PoisonNativeMethods.MoveNext(EffectNativeMethods.KillCore())!;
    }
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        var original = EffectNativeMethods.LoseHp();
        int expected = __originalMethod == PoisonNativeMethods.MoveNext(PoisonNativeMethods.ManyDamage()) ? 3 : 1;
        if (code.Count(c => c.Calls(original)) != expected)
        { DamageObservationCoverage.Require("effect.hp:" + (expected == 3 ? "damage" : "kill"), false); return code; }
        DamageObservationCoverage.Require("effect.hp:" + (expected == 3 ? "damage" : "kill"), true);
        foreach (var call in code.Where(c => c.Calls(original)))
        {
            call.opcode = OpCodes.Call;
            call.operand = AccessTools.Method(typeof(EffectDamageObserver), nameof(EffectDamageObserver.LoseHp));
        }
        return code;
    }
}
