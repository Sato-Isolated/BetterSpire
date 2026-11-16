#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BetterSpire2.Journal.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace BetterSpire2.Patches.Combat;

internal static class DamageSourceNativeMethods
{
    private static readonly Type[] ContextCreatureDamage = { typeof(PlayerChoiceContext), typeof(Creature),
        typeof(decimal), typeof(ValueProp), typeof(Creature) };
    private static readonly Type[] ContextTargetsDamage = { typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>),
        typeof(decimal), typeof(ValueProp), typeof(Creature) };
    private static readonly Type[] ContextCreatureDamageWithCard = { typeof(PlayerChoiceContext), typeof(Creature),
        typeof(decimal), typeof(ValueProp), typeof(Creature), typeof(CardModel), typeof(CardPlay) };

    internal static MethodInfo? LightningMoveNext() => PoisonNativeMethods.MoveNext(
        AccessTools.DeclaredMethod(typeof(LightningOrb), "ApplyLightningDamage", new[] { typeof(decimal),
            typeof(Creature), typeof(PlayerChoiceContext), typeof(bool) }));
    internal static MethodInfo? DarkOrbMoveNext() => PoisonNativeMethods.MoveNext(
        AccessTools.DeclaredMethod(typeof(DarkOrb), nameof(DarkOrb.Evoke), new[] { typeof(PlayerChoiceContext) }));
    internal static MethodInfo? ThornsMoveNext() => PoisonNativeMethods.MoveNext(
        AccessTools.DeclaredMethod(typeof(ThornsPower), nameof(ThornsPower.BeforeDamageReceived), new[] {
            typeof(PlayerChoiceContext), typeof(Creature), typeof(decimal), typeof(ValueProp), typeof(Creature), typeof(CardModel) }));
    internal static MethodInfo? FlameBarrierMoveNext() => PoisonNativeMethods.MoveNext(
        AccessTools.DeclaredMethod(typeof(FlameBarrierPower), nameof(FlameBarrierPower.AfterDamageReceived), new[] {
            typeof(PlayerChoiceContext), typeof(Creature), typeof(DamageResult), typeof(ValueProp), typeof(Creature), typeof(CardModel) }));
    internal static MethodInfo? ExplosiveArrowsMoveNext() => PoisonNativeMethods.MoveNext(
        AccessTools.DeclaredMethod(typeof(ExplosiveArrowsPower), nameof(ExplosiveArrowsPower.AfterArrowImpaled), new[] {
            typeof(ImpaledPower), typeof(ArrowSlot), typeof(PlayerChoiceContext), typeof(CardModel) }));
    internal static MethodInfo? ViolentChordMoveNext() => PoisonNativeMethods.MoveNext(
        AccessTools.DeclaredMethod(typeof(ViolentChordPower), nameof(ViolentChordPower.AfterCardPlayed), new[] {
            typeof(PlayerChoiceContext), typeof(CardPlay) }));
    internal static MethodInfo? TargetsDamage() => AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Damage), ContextTargetsDamage);
    internal static MethodInfo? SingleDamage() => AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Damage), ContextCreatureDamage);
    internal static MethodInfo? SingleDamageWithCard() => AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Damage), ContextCreatureDamageWithCard);
}

internal static class DamageSourceTranspiler
{
    internal static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions,
        MethodBase originalMethod, MethodInfo nativeCall, MethodInfo replacement, string capability,
        params (Type FieldType, string? FieldName)[] appendedFields)
    {
        var code = instructions.ToList();
        var stateMachine = originalMethod.DeclaringType;
        var fields = stateMachine?.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var loads = new List<CodeInstruction>();
        foreach (var (fieldType, fieldName) in appendedFields)
        {
            var matches = fields?.Where(field => field.FieldType == fieldType &&
                (fieldName == null || field.Name == fieldName)).ToArray() ?? Array.Empty<FieldInfo>();
            if (matches.Length != 1 || !code.Any(instruction => instruction.Calls(nativeCall)))
                return Failed(code, capability, "unexpected async state or native damage call");
            loads.Add(new CodeInstruction(OpCodes.Ldarg_0));
            loads.Add(new CodeInstruction(OpCodes.Ldfld, matches[0]));
        }
        if (code.Count(instruction => instruction.Calls(nativeCall)) != 1)
            return Failed(code, capability, "unexpected number of native damage calls");

        int index = code.FindIndex(instruction => instruction.Calls(nativeCall));
        var call = code[index];
        if (loads.Count > 0)
        {
            loads[0].labels.AddRange(call.labels);
            call.labels.Clear();
            foreach (var block in call.blocks.Where(block => block.blockType != ExceptionBlockType.EndExceptionBlock).ToArray())
            { loads[0].blocks.Add(block); call.blocks.Remove(block); }
            code.InsertRange(index, loads);
            index += loads.Count;
            call = code[index];
        }
        call.opcode = OpCodes.Call;
        call.operand = replacement;
        return code;
    }

    private static IEnumerable<CodeInstruction> Failed(List<CodeInstruction> code, string capability, string reason)
    {
        DamageObservationCoverage.Require(capability, false);
        ModLog.Info("Damage source attribution unavailable: " + reason + "; keeping native damage calls.");
        return code;
    }
}

/// <summary>
/// One source attribution: inside an async native hook, swap its single CreatureCmd.Damage call for the
/// DamageSourceObserver wrapper that credits the real source. Any shape mismatch keeps the native call and
/// marks the capability partial.
/// </summary>
internal sealed class DamageSourceOrigin(string capability, Func<MethodInfo?> target, Func<MethodInfo?> native,
    string wrapper, params (Type FieldType, string? FieldName)[] capturedFields)
{
    internal bool Prepare()
    {
        bool found = target() != null && native() != null;
        DamageObservationCoverage.Require(capability, found);
        return found;
    }

    internal MethodBase Target() => target()!;

    internal IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var replacement = AccessTools.Method(typeof(DamageSourceObserver), wrapper)!;
        var result = DamageSourceTranspiler.Replace(instructions, original, native()!, replacement, capability,
            capturedFields).ToList();
        DamageObservationCoverage.Require(capability, result.Any(instruction => instruction.Calls(replacement)));
        return result;
    }
}

// Harmony needs one class per target; each declares only its source.
[HarmonyPatch]
internal static class Journal_LightningOrbDamageOrigin_Patch
{
    private static readonly DamageSourceOrigin Origin = new("damage-source:lightning",
        DamageSourceNativeMethods.LightningMoveNext, DamageSourceNativeMethods.TargetsDamage,
        nameof(DamageSourceObserver.LightningDamage), (typeof(LightningOrb), null), (typeof(bool), null));
    [HarmonyPrepare] private static bool Prepare() => Origin.Prepare();
    [HarmonyTargetMethod] private static MethodBase TargetMethod() => Origin.Target();
    [HarmonyTranspiler] private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod) => Origin.Transpile(instructions, __originalMethod);
}

[HarmonyPatch]
internal static class Journal_DarkOrbDamageOrigin_Patch
{
    private static readonly DamageSourceOrigin Origin = new("damage-source:dark-orb",
        DamageSourceNativeMethods.DarkOrbMoveNext, DamageSourceNativeMethods.SingleDamage,
        nameof(DamageSourceObserver.DarkOrbEvokeDamage), (typeof(DarkOrb), null));
    [HarmonyPrepare] private static bool Prepare() => Origin.Prepare();
    [HarmonyTargetMethod] private static MethodBase TargetMethod() => Origin.Target();
    [HarmonyTranspiler] private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod) => Origin.Transpile(instructions, __originalMethod);
}

[HarmonyPatch]
internal static class Journal_ThornsDamageOrigin_Patch
{
    private static readonly DamageSourceOrigin Origin = new("damage-source:thorns",
        DamageSourceNativeMethods.ThornsMoveNext, DamageSourceNativeMethods.SingleDamageWithCard,
        nameof(DamageSourceObserver.ThornsDamage), (typeof(ThornsPower), null));
    [HarmonyPrepare] private static bool Prepare() => Origin.Prepare();
    [HarmonyTargetMethod] private static MethodBase TargetMethod() => Origin.Target();
    [HarmonyTranspiler] private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod) => Origin.Transpile(instructions, __originalMethod);
}

[HarmonyPatch]
internal static class Journal_FlameBarrierDamageOrigin_Patch
{
    private static readonly DamageSourceOrigin Origin = new("damage-source:flame-barrier",
        DamageSourceNativeMethods.FlameBarrierMoveNext, DamageSourceNativeMethods.SingleDamage,
        nameof(DamageSourceObserver.FlameBarrierDamage), (typeof(FlameBarrierPower), null));
    [HarmonyPrepare] private static bool Prepare() => Origin.Prepare();
    [HarmonyTargetMethod] private static MethodBase TargetMethod() => Origin.Target();
    [HarmonyTranspiler] private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod) => Origin.Transpile(instructions, __originalMethod);
}

[HarmonyPatch]
internal static class Journal_ExplosiveArrowsDamageOrigin_Patch
{
    private static readonly DamageSourceOrigin Origin = new("damage-source:explosive-arrows",
        DamageSourceNativeMethods.ExplosiveArrowsMoveNext, DamageSourceNativeMethods.TargetsDamage,
        nameof(DamageSourceObserver.ExplosiveArrowsDamage), (typeof(ExplosiveArrowsPower), null));
    [HarmonyPrepare] private static bool Prepare() => Origin.Prepare();
    [HarmonyTargetMethod] private static MethodBase TargetMethod() => Origin.Target();
    [HarmonyTranspiler] private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod) => Origin.Transpile(instructions, __originalMethod);
}

[HarmonyPatch]
internal static class Journal_ViolentChordDamageOrigin_Patch
{
    private static readonly DamageSourceOrigin Origin = new("damage-source:violent-chord",
        DamageSourceNativeMethods.ViolentChordMoveNext, DamageSourceNativeMethods.TargetsDamage,
        nameof(DamageSourceObserver.ViolentChordDamage), (typeof(ViolentChordPower), null));
    [HarmonyPrepare] private static bool Prepare() => Origin.Prepare();
    [HarmonyTargetMethod] private static MethodBase TargetMethod() => Origin.Target();
    [HarmonyTranspiler] private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod) => Origin.Transpile(instructions, __originalMethod);
}
