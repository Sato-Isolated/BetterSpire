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
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;

namespace BetterSpire2.Patches.Combat;

internal static class JudgementNativeMethods
{
    internal static MethodInfo? SetCurrentHp() => AccessTools.DeclaredMethod(typeof(CreatureCmd),
        nameof(CreatureCmd.SetCurrentHp), new[] { typeof(Creature), typeof(decimal) });
    internal static MethodInfo? OnPlayMoveNext() => PoisonNativeMethods.MoveNext(
        AccessTools.DeclaredMethod(typeof(Judgement), "OnPlay", new[] { typeof(PlayerChoiceContext), typeof(CardPlay) }));
}

[HarmonyPatch]
internal static class Journal_JudgementDamageOrigin_Patch
{
    private const string Capability = "effect.judgement";
    private static bool Prepare() => EffectNativeMethods.Available(Capability,
        JudgementNativeMethods.SetCurrentHp() != null && JudgementNativeMethods.OnPlayMoveNext() != null);
    private static MethodBase TargetMethod() => JudgementNativeMethods.OnPlayMoveNext()!;
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var replacement = AccessTools.Method(typeof(JudgementDamageObserver), nameof(JudgementDamageObserver.SetCurrentHp));
        var code = DamageSourceTranspiler.Replace(instructions, __originalMethod, JudgementNativeMethods.SetCurrentHp()!,
            replacement, Capability, (typeof(Judgement), null)).ToList();
        DamageObservationCoverage.Require(Capability, code.Any(c => c.Calls(replacement)));
        return code;
    }
}

[HarmonyPatch]
internal static class Journal_JudgementCommandScope_Patch
{
    private static bool Prepare() => EffectNativeMethods.Available("effect.judgement.scope", JudgementNativeMethods.SetCurrentHp() != null);
    private static MethodBase TargetMethod() => JudgementNativeMethods.SetCurrentHp()!;
    private static void Prefix(out IDisposable? __state)
    {
        __state = null;
        try { __state = JudgementDamageObserver.EnterCommand(); }
        catch (Exception ex) { NativeDamageObserver.Report(ex); }
    }
    private static void Finalizer(IDisposable? __state) => __state?.Dispose();
}

[HarmonyPatch]
internal static class Journal_JudgementHpResult_Patch
{
    private const string Capability = "effect.judgement.hp";
    private static bool Prepare() => EffectNativeMethods.Available(Capability,
        PoisonNativeMethods.MoveNext(JudgementNativeMethods.SetCurrentHp()) != null);
    private static MethodBase TargetMethod() => PoisonNativeMethods.MoveNext(JudgementNativeMethods.SetCurrentHp())!;
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var native = AccessTools.DeclaredMethod(typeof(Creature), nameof(Creature.SetCurrentHpInternal), new[] { typeof(decimal) });
        bool valid = native != null && code.Count(c => c.Calls(native)) == 1;
        DamageObservationCoverage.Require(Capability, valid);
        if (!valid) return code;
        var call = code.Single(c => c.Calls(native));
        call.opcode = OpCodes.Call;
        call.operand = AccessTools.Method(typeof(JudgementDamageObserver), nameof(JudgementDamageObserver.SetHp));
        return code;
    }
}
