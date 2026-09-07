#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace BetterSpire2.Patches.Combat;

internal static class PoisonNativeMethods
{
    internal static MethodInfo? Damage(Type target) => AccessTools.Method(typeof(CreatureCmd),
        nameof(CreatureCmd.Damage), new[] { typeof(PlayerChoiceContext), target, typeof(decimal),
            typeof(ValueProp), typeof(Creature), typeof(CardModel) });
    internal static MethodInfo? MoveNext(MethodInfo? method) => method?
        .GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType.GetMethod("MoveNext",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    internal static MethodInfo? PoisonMoveNext() => MoveNext(AccessTools.Method(typeof(PoisonPower),
        nameof(PoisonPower.AfterSideTurnStart)));
}

[HarmonyPatch]
internal static class Journal_PoisonDamageOrigin_Patch
{
    [HarmonyPrepare]
    private static bool Prepare()
    {
        bool supported = PoisonNativeMethods.PoisonMoveNext() != null && PoisonNativeMethods.Damage(typeof(Creature)) != null;
        if (!supported) ModLog.Info("Poison provenance unavailable: native signature changed; damage remains unattributed.");
        return supported;
    }
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod() => PoisonNativeMethods.PoisonMoveNext()!;

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        MethodInfo original = PoisonNativeMethods.Damage(typeof(Creature))!;
        var ownerFields = __originalMethod.DeclaringType!.GetFields(BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic).Where(f => f.FieldType == typeof(PoisonPower)).ToArray();
        // Fail closed on an unfamiliar game version. Do not guess another call,
        // corrupt IL, or identify poison by matching an unrelated damage amount.
        if (ownerFields.Length != 1 || code.Count(c => c.Calls(original)) != 1)
        {
            ModLog.Info("Poison provenance unavailable: unexpected native IL; keeping original damage call.");
            return code;
        }
        MethodInfo replacement = AccessTools.Method(typeof(PoisonDamageObserver), nameof(PoisonDamageObserver.Damage));
        int index = code.FindIndex(c => c.Calls(original));
        var call = code[index];
        var loadThis = new CodeInstruction(OpCodes.Ldarg_0);
        loadThis.labels.AddRange(call.labels); call.labels.Clear();
        // A protected-region beginning belongs before the inserted instructions;
        // end markers still belong to the replacement call at the same boundary.
        foreach (var block in call.blocks.Where(b => b.blockType != ExceptionBlockType.EndExceptionBlock).ToArray())
        { loadThis.blocks.Add(block); call.blocks.Remove(block); }
        call.opcode = OpCodes.Call; call.operand = replacement;
        code.Insert(index, loadThis);
        code.Insert(index + 1, new CodeInstruction(OpCodes.Ldfld, ownerFields[0]));
        return code;
    }
}

[HarmonyPatch]
internal static class Journal_DamageCommandScope_Patch
{
    [HarmonyPrepare]
    private static bool Prepare() => PoisonNativeMethods.Damage(typeof(IEnumerable<Creature>)) != null;
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod() => PoisonNativeMethods.Damage(typeof(IEnumerable<Creature>))!;
    [HarmonyPrefix]
    private static void Prefix(out IDisposable? __state)
    {
        __state = null;
        try { __state = PoisonDamageObserver.EnterCommand(); }
        catch (Exception ex) { ModLog.Error("Journal.PoisonScope", ex); }
    }
    // Intentionally restore when the native async method RETURNS its task, not
    // when that task completes: its own execution context carries the scope.
    // A void finalizer preserves the original exception and original task.
    [HarmonyFinalizer]
    private static void Finalizer(IDisposable? __state) => __state?.Dispose();
}

[HarmonyPatch]
internal static class Journal_PoisonHistoryTag_Patch
{
    [HarmonyPrepare]
    private static bool Prepare() => PoisonNativeMethods.MoveNext(
        PoisonNativeMethods.Damage(typeof(IEnumerable<Creature>))) != null;
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod() => PoisonNativeMethods.MoveNext(
        PoisonNativeMethods.Damage(typeof(IEnumerable<Creature>)))!;
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var original = AccessTools.Method(typeof(CombatHistory), nameof(CombatHistory.DamageReceived));
        if (code.Count(c => c.Calls(original)) != 1)
        {
            ModLog.Info("Poison provenance unavailable: native history call changed; keeping original history.");
            return code;
        }
        // Only the native command's own result, not a manual history entry made
        // by a nested callback, gets a tag. Preserve all arguments and IL labels.
        var call = code.Single(c => c.Calls(original));
        call.opcode = OpCodes.Call;
        call.operand = AccessTools.Method(typeof(PoisonDamageObserver), nameof(PoisonDamageObserver.RecordDamage));
        return code;
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.RemovePowerInternal))]
internal static class Journal_PoisonRemoved_Patch
{
    [HarmonyPrefix]
    private static void Prefix(PowerModel power)
    {
        if (power is not PoisonPower poison) return;
        try { JournalService.ForgetPoison(poison); }
        catch (Exception ex) { JournalService.Session.MarkPartial(); ModLog.Error("Journal.PoisonRemoved", ex); }
    }
}
