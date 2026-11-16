#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BetterSpire2.Journal.Core;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace BetterSpire2.Journal.Game;

/// <summary>
/// Observes only the direct native HP-loss call in a scoped Strangle damage or
/// Doom kill. No invented DamageReceived history, gameplay hooks or dealer edits.
/// Both damage and kill commands mask the scope so nested reactions stay separate.
/// </summary>
internal static class EffectDamageObserver
{
    private sealed record Source(Creature Target, CombatRecord Combat, EffectDamageCredit Credit)
    {
        internal bool Consumed;
    }
    private static readonly OneShotDamageScope<Source> Scope = new();
    internal static IDisposable EnterCommand() => Scope.EnterCommand();

    internal static Task<IEnumerable<DamageResult>> StrangleDamage(PlayerChoiceContext choiceContext,
        Creature target, decimal amount, ValueProp props, CardModel? cardSource,
        CardPlay? cardPlay, StranglePower strangle)
    {
        Source? source = null;
        try
        {
            // Native Strangle is unblockable, with one power instance per applier.
            // Do not credit the card that merely triggered an existing Strangle.
            if (cardSource == null && cardPlay == null && ReferenceEquals(strangle.Owner, target) &&
                props.HasFlag(ValueProp.Unblockable) && props.HasFlag(ValueProp.Unpowered) &&
                JournalService.CaptureEffectCombat(target) is { } combat)
                source = new Source(target, combat, new EffectDamageCredit("effect:strangle",
                    strangle.Title.GetFormattedText(), HistoryReader.PlayerId(strangle.Applier)));
        }
        catch (Exception ex) { Report("StrangleCapture", ex); }
        if (source == null) return CreatureCmd.Damage(choiceContext, target, amount, props, cardSource, cardPlay);
        using var pending = Scope.RequestNext(source);
        return CreatureCmd.Damage(choiceContext, target, amount, props, cardSource, cardPlay);
    }

    internal static Task DoomKill(Creature target, CardModel? cardSource, bool force)
    {
        Source? source = null;
        try
        {
            if (JournalService.CaptureEffectCombat(target) is { } combat)
            {
                var doom = target.GetPower<DoomPower>();
                var credit = doom == null ? null : JournalService.CaptureDoom(doom);
                source = new Source(target, combat, new EffectDamageCredit("effect:doom",
                    doom?.Title.GetFormattedText() ?? (ModText.IsFrench ? "Trépas" : "Doom"), StackCredit: credit));
            }
        }
        catch (Exception ex) { Report("DoomCapture", ex); }
        if (source == null) return CreatureCmd.Kill(target, cardSource, force);
        using var pending = Scope.RequestNext(source);
        return CreatureCmd.Kill(target, cardSource, force);
    }

    internal static DamageResult LoseHp(Creature target, decimal amount, ValueProp props)
    {
        // Unconditional native call, outside the observation catch. Keep the exact
        // result and native failures. Capture before death hooks can clear history,
        // remove powers or revive the receiver; zero-HP kills contribute nothing.
        var result = target.LoseHpInternal(amount, props);
        try
        {
            var source = Scope.Current;
            if (source != null && !source.Consumed && ReferenceEquals(source.Target, target) &&
                ReferenceEquals(result.Receiver, target))
            {
                source.Consumed = true;
                if (result.UnblockedDamage > 0)
                    JournalService.RecordEffectDamage(target, result, source.Combat, source.Credit);
            }
        }
        catch (Exception ex) { Report("EffectDamage", ex); }
        return result;
    }

    private static void Report(string context, Exception error)
    { JournalService.Session.MarkPartial(); ModLog.Error("Journal." + context, error); }
}
