#nullable enable
using System;
using System.Threading.Tasks;
using BetterSpire2.Journal.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;

namespace BetterSpire2.Journal.Game;

internal static class JudgementDamageObserver
{
    private sealed record Source(Creature Target, NativeDamageObserver.Command Command)
    { internal bool Consumed; }
    private static readonly OneShotDamageScope<Source> Scope = new();
    internal static IDisposable EnterCommand() => Scope.EnterCommand();

    internal static Task SetCurrentHp(Creature target, decimal amount, Judgement card)
    {
        NativeDamageObserver.Command? command = null;
        try
        {
            if (amount == 0 && target.Side == CombatSide.Enemy && target.CurrentHp > 0 &&
                JournalService.CaptureEffectCombat(target) != null)
                command = NativeDamageObserver.Enter(null, card);
        }
        catch (Exception ex) { Report(ex); }
        if (command == null) return CreatureCmd.SetCurrentHp(target, amount);
        using (command)
        using (Scope.RequestNext(new Source(target, command)))
        {
            try
            {
                var task = CreatureCmd.SetCurrentHp(target, amount);
                NativeDamageObserver.CompleteWhenReady(task, command);
                return task;
            }
            catch
            {
                command.Complete();
                throw;
            }
        }
    }

    internal static void SetHp(Creature target, decimal amount)
    {
        var source = Scope.Current;
        bool observe = source != null && !source.Consumed && ReferenceEquals(source.Target, target) && amount == 0;
        int previousHp = observe ? target.CurrentHp : 0;
        if (observe) source!.Consumed = true;
        target.SetCurrentHpInternal(amount);
        try
        {
            if (observe && previousHp > 0 && source!.Command.Context is { } context)
            {
                // The native zero setter removes the previous HP before synchronous
                // subscribers can revive the target. This result is accounting only.
                var result = new DamageResult(target, ValueProp.Unpowered | ValueProp.Unblockable)
                    { UnblockedDamage = previousHp, WasTargetKilled = true };
                JournalService.RecordNativeDamage(context, result, null, source.Command.Card, null);
            }
        }
        catch (Exception ex) { Report(ex); }
    }

    private static void Report(Exception error)
    { DamageObservationCoverage.Require("effect.judgement", false); ModLog.Error("Journal.Judgement", error); }
}
