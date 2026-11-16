#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterSpire2.Journal.Core;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace BetterSpire2.Journal.Game;

internal sealed record NativeDamageContext(CombatRecord Combat);

/// <summary>Observes completed native result metadata before the optional history call.
/// Async-local command identity prevents nested reactions inheriting an outer dealer/source.</summary>
internal static class NativeDamageObserver
{
    private static readonly AsyncLocal<Command?> Current = new();
    internal sealed class Command : IDisposable
    {
        internal readonly NativeDamageContext? Context;
        internal readonly Creature? Dealer;
        internal readonly CardModel? Card;
        internal readonly EffectDamageCredit? Credit;
        private readonly Command? _previous;
        private readonly IDisposable _sourceScope;
        private bool _restored;
        private int _completed;
        internal Command(Creature? dealer, CardModel? card)
        {
            _previous = Current.Value;
            _sourceScope = DamageSourceObserver.EnterCommand();
            Credit = DamageSourceObserver.CurrentCredit;
            Context = JournalService.BeginNativeDamage();
            Dealer = dealer; Card = card;
            Current.Value = this;
        }
        public void Dispose()
        {
            if (_restored) return;
            _restored = true;
            Current.Value = _previous;
            _sourceScope.Dispose();
        }
        internal void Complete()
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0) return;
            if (Context != null) JournalService.EndNativeDamage(Context);
        }
    }
    internal static Command Enter(Creature? dealer, CardModel? card) => new(dealer, card);

    // The original Task is returned unchanged. This bookkeeping continuation runs
    // on the captured game context; it never enumerates the caller's target sequence.
    internal static async void CompleteWhenReady(Task task, Command command)
    {
        try { await task; }
        catch
        {
            // A native failure may occur after HP changed but before complete result
            // metadata exists. Disclose that gap without replacing the native failure.
            if (command.Context is { } context && ReferenceEquals(context.Combat, JournalService.Session.Active))
                JournalService.Session.MarkPartial("damage.interrupted");
        }
        finally
        {
            try { command.Complete(); }
            catch (Exception ex) { Report(ex); }
        }
    }
    internal static int ReadCompletedHp(DamageResult result)
    {
        try
        {
            var command = Current.Value;
            if (command?.Context is { } context)
            {
                PoisonDamageObserver.TagResult(result, context.Combat);
                JournalService.RecordNativeDamage(context, result, command.Dealer, command.Card, command.Credit);
            }
        }
        catch (Exception ex) { Report(ex); }
        return result.UnblockedDamage;
    }
    internal static void Report(Exception ex)
    { JournalService.Session.MarkPartial("damage.results"); ModLog.Error("Journal.NativeDamage", ex); }
}
