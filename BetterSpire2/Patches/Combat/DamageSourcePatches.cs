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
    internal static MethodInfo? LightningDamage() => AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Damage), ContextTargetsDamage);
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

[HarmonyPatch]
internal static class Journal_LightningOrbDamageOrigin_Patch
{
    private const string Capability = "damage-source:lightning";
    [HarmonyPrepare]
    private static bool Prepare()
    {
        bool found = DamageSourceNativeMethods.LightningMoveNext() != null && DamageSourceNativeMethods.LightningDamage() != null;
        DamageObservationCoverage.Require(Capability, found); return found;
    }
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod() => DamageSourceNativeMethods.LightningMoveNext()!;
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var native = DamageSourceNativeMethods.LightningDamage()!;
        var replacement = AccessTools.Method(typeof(DamageSourceObserver), nameof(DamageSourceObserver.LightningDamage))!;
        var result = DamageSourceTranspiler.Replace(instructions, __originalMethod, native, replacement, Capability,
            (typeof(LightningOrb), null), (typeof(bool), null)).ToList();
        bool valid = result.Any(instruction => instruction.Calls(replacement));
        DamageObservationCoverage.Require(Capability, valid);
        return result;
    }
}

[HarmonyPatch]
internal static class Journal_DarkOrbDamageOrigin_Patch
{
    private const string Capability = "damage-source:dark-orb";
    [HarmonyPrepare]
    private static bool Prepare()
    {
        bool found = DamageSourceNativeMethods.DarkOrbMoveNext() != null && DamageSourceNativeMethods.SingleDamage() != null;
        DamageObservationCoverage.Require(Capability, found); return found;
    }
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod() => DamageSourceNativeMethods.DarkOrbMoveNext()!;
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var native = DamageSourceNativeMethods.SingleDamage()!;
        var replacement = AccessTools.Method(typeof(DamageSourceObserver), nameof(DamageSourceObserver.DarkOrbEvokeDamage))!;
        var result = DamageSourceTranspiler.Replace(instructions, __originalMethod, native, replacement, Capability,
            (typeof(DarkOrb), null)).ToList();
        bool valid = result.Any(instruction => instruction.Calls(replacement));
        DamageObservationCoverage.Require(Capability, valid);
        return result;
    }
}

[HarmonyPatch]
internal static class Journal_ThornsDamageOrigin_Patch
{
    private const string Capability = "damage-source:thorns";
    [HarmonyPrepare]
    private static bool Prepare()
    {
        bool found = DamageSourceNativeMethods.ThornsMoveNext() != null && DamageSourceNativeMethods.SingleDamageWithCard() != null;
        DamageObservationCoverage.Require(Capability, found); return found;
    }
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod() => DamageSourceNativeMethods.ThornsMoveNext()!;
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var native = DamageSourceNativeMethods.SingleDamageWithCard()!;
        var replacement = AccessTools.Method(typeof(DamageSourceObserver), nameof(DamageSourceObserver.ThornsDamage))!;
        var result = DamageSourceTranspiler.Replace(instructions, __originalMethod, native, replacement, Capability,
            (typeof(ThornsPower), null)).ToList();
        bool valid = result.Any(instruction => instruction.Calls(replacement));
        DamageObservationCoverage.Require(Capability, valid);
        return result;
    }
}

[HarmonyPatch]
internal static class Journal_FlameBarrierDamageOrigin_Patch
{
    private const string Capability = "damage-source:flame-barrier";
    [HarmonyPrepare]
    private static bool Prepare()
    {
        bool found = DamageSourceNativeMethods.FlameBarrierMoveNext() != null && DamageSourceNativeMethods.SingleDamage() != null;
        DamageObservationCoverage.Require(Capability, found); return found;
    }
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod() => DamageSourceNativeMethods.FlameBarrierMoveNext()!;
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var native = DamageSourceNativeMethods.SingleDamage()!;
        var replacement = AccessTools.Method(typeof(DamageSourceObserver), nameof(DamageSourceObserver.FlameBarrierDamage))!;
        var result = DamageSourceTranspiler.Replace(instructions, __originalMethod, native, replacement, Capability,
            (typeof(FlameBarrierPower), null)).ToList();
        bool valid = result.Any(instruction => instruction.Calls(replacement));
        DamageObservationCoverage.Require(Capability, valid);
        return result;
    }
}

[HarmonyPatch]
internal static class Journal_ExplosiveArrowsDamageOrigin_Patch
{
    private const string Capability = "damage-source:explosive-arrows";
    [HarmonyPrepare]
    private static bool Prepare()
    {
        bool found = DamageSourceNativeMethods.ExplosiveArrowsMoveNext() != null && DamageSourceNativeMethods.LightningDamage() != null;
        DamageObservationCoverage.Require(Capability, found); return found;
    }
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod() => DamageSourceNativeMethods.ExplosiveArrowsMoveNext()!;
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var native = DamageSourceNativeMethods.LightningDamage()!;
        var replacement = AccessTools.Method(typeof(DamageSourceObserver), nameof(DamageSourceObserver.ExplosiveArrowsDamage))!;
        var result = DamageSourceTranspiler.Replace(instructions, __originalMethod, native, replacement, Capability,
            (typeof(ExplosiveArrowsPower), null)).ToList();
        bool valid = result.Any(instruction => instruction.Calls(replacement));
        DamageObservationCoverage.Require(Capability, valid);
        return result;
    }
}

[HarmonyPatch]
internal static class Journal_ViolentChordDamageOrigin_Patch
{
    private const string Capability = "damage-source:violent-chord";
    [HarmonyPrepare]
    private static bool Prepare()
    {
        bool found = DamageSourceNativeMethods.ViolentChordMoveNext() != null && DamageSourceNativeMethods.LightningDamage() != null;
        DamageObservationCoverage.Require(Capability, found); return found;
    }
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod() => DamageSourceNativeMethods.ViolentChordMoveNext()!;
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var native = DamageSourceNativeMethods.LightningDamage()!;
        var replacement = AccessTools.Method(typeof(DamageSourceObserver), nameof(DamageSourceObserver.ViolentChordDamage))!;
        var result = DamageSourceTranspiler.Replace(instructions, __originalMethod, native, replacement, Capability,
            (typeof(ViolentChordPower), null)).ToList();
        bool valid = result.Any(instruction => instruction.Calls(replacement));
        DamageObservationCoverage.Require(Capability, valid);
        return result;
    }
}
