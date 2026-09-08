#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BetterSpire2.Journal.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace BetterSpire2.Journal.Game;

/// <summary>
/// Read-only metadata for native DamageResult identities. Never changes a dealer,
/// card, damage amount, power, task result or combat hook. Tags exist BEFORE the
/// history Changed event, including the lethal tick which ends the encounter.
/// </summary>
internal static class PoisonDamageObserver
{
    private sealed record Source(Creature Target, CombatRecord Combat, PoisonDamageCredit Credit);
    private static readonly OneShotDamageScope<Source> Scope = new();
    private static ConditionalWeakTable<DamageResult, PoisonDamageCredit> _credits = new();

    internal static void Reset() => _credits = new();
    internal static IDisposable EnterCommand() => Scope.EnterCommand();
    internal static bool TryGetCredit(DamageResult result, out PoisonDamageCredit credit) =>
        _credits.TryGetValue(result, out credit!);

    // Replaces ONLY PoisonPower's direct native Damage call. The extra argument
    // is the actual PoisonPower instance, obtained from its async state machine.
    internal static Task<IEnumerable<DamageResult>> Damage(PlayerChoiceContext choiceContext,
        Creature target, decimal amount, ValueProp props, CardModel? cardSource,
        CardPlay? cardPlay, PoisonPower poison)
    {
        Source? source = null;
        try
        {
            if (cardSource == null && cardPlay == null && target.Side == CombatSide.Enemy &&
                ReferenceEquals(poison.Owner, target))
            {
                var credit = JournalService.CapturePoison(poison);
                var combat = JournalService.Session.Active;
                if (credit != null && combat != null) source = new Source(target, combat, credit);
            }
        }
        catch (Exception ex) { JournalService.Session.MarkPartial(); ModLog.Error("Journal.PoisonCapture", ex); }
        // The scope is only a request for the next central damage command. A
        // command claims it once, and nested commands mask it across await.
        if (source == null) return CreatureCmd.Damage(choiceContext, target, amount, props, cardSource, cardPlay);
        using var pending = Scope.RequestNext(source);
        return CreatureCmd.Damage(choiceContext, target, amount, props, cardSource, cardPlay);
    }

    internal static void RecordDamage(CombatHistory history, ICombatState combatState,
        Creature receiver, Creature? dealer, DamageResult result, CardModel? cardSource)
    {
        try
        {
            var source = Scope.Current;
            if (source != null && dealer == null && cardSource == null &&
                ReferenceEquals(receiver, source.Target) && ReferenceEquals(result.Receiver, receiver) &&
                ReferenceEquals(source.Combat, JournalService.Session.Active) &&
                ReferenceEquals(receiver.CombatState, combatState) && JournalService.Observes(history))
                _credits.GetValue(result, _ => source.Credit);
        }
        catch (Exception ex) { JournalService.Session.MarkPartial(); ModLog.Error("Journal.PoisonHistory", ex); }
        // This native call is unconditional and outside the observer's catch:
        // preserve game failures, event order, result identity and the last kill.
        history.DamageReceived(combatState, receiver, dealer, result, cardSource);
    }
}
