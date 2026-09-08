#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using BetterSpire2.Journal.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace BetterSpire2.Journal.Game;

/// <summary>Copies native history into primitive observations. All attribution is read-only.</summary>
internal sealed class HistoryReader
{
    private static readonly Func<CombatHistoryEntry, int>? ReadRound = Getter<int>("RoundNumber");
    private static readonly Func<CombatHistoryEntry, CombatSide>? ReadSide = Getter<CombatSide>("CurrentSide");
    private static Func<CombatHistoryEntry, T>? Getter<T>(string name)
    {
        var property = AccessTools.Property(typeof(CombatHistoryEntry), name);
        var method = property?.GetGetMethod(true);
        if (method == null) return null;
        try { return method.CreateDelegate<Func<CombatHistoryEntry, T>>(); }
        catch { return entry => (T)property!.GetValue(entry)!; } // Rare restricted-runtime fallback.
    }

    private readonly HistoryCursor _cursor = new();
    private readonly Dictionary<Creature, string> _actorIds = new();
    private string ActorId(Creature creature)
    {
        if (!_actorIds.TryGetValue(creature, out var id)) _actorIds[creature] = id = "actor:" + _actorIds.Count;
        return id;
    }
    private ConditionalWeakTable<PoisonPower, PoisonStackPool> _poison = new();
    internal void Reset()
    {
        _cursor.Reset(); _actorIds.Clear(); _poison = new(); PoisonDamageObserver.Reset();
    }
    internal PoisonDamageCredit CapturePoison(PoisonPower power) =>
        _poison.GetValue(power, _ => new PoisonStackPool()).Capture(power.Amount);
    internal void ForgetPoison(PoisonPower power) => _poison.Remove(power);
    internal void Drain(CombatHistory history, JournalSession session)
    {
        // The supplied DLL returns its List<T> as IEnumerable<T>. Keep the indexed fast path.
        var entries = history.Entries as IReadOnlyList<CombatHistoryEntry> ?? history.Entries.ToArray();
        bool unchanged = _cursor.Consume(entries, index =>
        {
            var entry = entries[index];
            try
            {
                if (ReadRound == null || ReadSide == null) { session.MarkPartial(); return; }
                int round = ReadRound(entry);
                CombatSide side = ReadSide(entry);
                var phase = round <= 0 ? JournalPhase.Setup : side == CombatSide.Player ? JournalPhase.Player :
                    side == CombatSide.Enemy ? JournalPhase.Enemy : JournalPhase.Unknown;
                Map(entry, Math.Max(0, round), phase, session);
            }
            catch (Exception ex)
            { session.MarkPartial(); ModLog.Error("Journal.HistoryEntry", ex); }
        });
        // Normal clearing is intercepted before the list changes. Do not pretend a mutated
        // third-party history can be fully reconstructed from aggregate data.
        if (!unchanged) session.MarkPartial();
    }
    internal static string? PlayerId(Creature? creature) => Owner(creature)?.NetId.ToString(CultureInfo.InvariantCulture);
    private static Player? Owner(Creature? creature) => creature?.IsPlayer == true ? creature.Player : creature?.PetOwner;
    internal static void Emit(JournalSession session, Creature? creature, int round, JournalPhase phase,
        Stat metric, long amount, CardModel? source = null, string? other = null)
    {
        string? id = PlayerId(creature);
        if (id == null || amount <= 0) return;
        string key = source != null ? "card:" + source.Id.Entry + ":" + source.Title : other ?? "";
        string title = source?.Title ?? "";
        session.Append(new JournalEvent(id, round, phase, metric, amount, key, title, source != null));
    }
    private void Map(CombatHistoryEntry entry, int round, JournalPhase phase, JournalSession session)
    {
        switch (entry)
        {
            case PowerReceivedEntry e when e.Power is PoisonPower poison:
                // Signed, native post-modifier delta. A rejected application has
                // no positive delta; a natural decrement has a negative delta.
                // Do not read power.Applier: stacked poison retains only one applier.
                string? applier = PlayerId(e.Applier);
                if (applier != null && session.Run?.Players.ContainsKey(applier) != true) applier = null;
                _poison.GetValue(poison, _ => new PoisonStackPool()).ObserveChange(applier, e.Amount);
                break;
            case CardPlayFinishedEntry e:
                Emit(session, e.Actor, round, phase, Stat.CardsPlayed, 1, e.CardPlay.Card); break;
            case CardDrawnEntry e: Emit(session, e.Actor, round, phase, Stat.CardsDrawn, 1); break;
            case CardDiscardedEntry e: Emit(session, e.Actor, round, phase, Stat.CardsDiscarded, 1); break;
            case CardExhaustedEntry e: Emit(session, e.Actor, round, phase, Stat.CardsExhausted, 1); break;
            case CardGeneratedEntry e: Emit(session, e.Actor, round, phase, Stat.CardsGenerated, 1); break;
            case EnergySpentEntry e: Emit(session, e.Actor, round, phase, Stat.EnergySpent, e.Amount); break;
            case StarsModifiedEntry e:
                Emit(session, e.Actor, round, phase, e.Amount > 0 ? Stat.StarsGained : Stat.StarsSpent, Math.Abs((long)e.Amount)); break;
            case OrbChanneledEntry e: Emit(session, e.Actor, round, phase, Stat.OrbsChanneled, 1); break;
            case PotionUsedEntry e: Emit(session, e.Actor, round, phase, Stat.PotionsUsed, 1); break;
            case BlockGainedEntry e:
                Emit(session, e.Receiver, round, phase, Stat.BlockGained, e.Amount, e.CardPlay?.Card, "effect:unknown"); break;
            case DamageReceivedEntry e:
                // UnblockedDamage is actual HP removed, excluding OverkillDamage in this DLL.
                Emit(session, e.Receiver, round, phase, e.Receiver.PetOwner != null ? Stat.PetHpLost : Stat.HpLost,
                    e.Result.UnblockedDamage, e.CardSource, "effect:unknown");
                // Pet attacks can consume the owner's block. That block is counted ONCE from history,
                // not once again via DamageBlockInternal. Pet HP remains a separate metric.
                Emit(session, e.Receiver, round, phase, Stat.DamageBlocked, e.Result.BlockedDamage);
                Creature? dealer = Owner(e.Dealer) != null ? e.Dealer : e.CardSource?.Owner?.Creature;
                string? dealerId = PlayerId(dealer);
                bool eligible = e.Receiver.Side == CombatSide.Enemy &&
                    (dealerId != null || e.Dealer == null || e.Dealer.Side != CombatSide.Enemy);
                string sourceId = e.CardSource == null ? "effect:unknown" : "card:" + e.CardSource.Id.Entry + ":" + e.CardSource.Title;
                string sourceName = e.CardSource?.Title ?? "";
                var attribution = new DamageAttribution(DamageAttributionKind.NotCounted, Array.Empty<DamageShare>());
                if (eligible)
                {
                    PoisonDamageObserver.TryGetCredit(e.Result, out var credit);
                    attribution = DamageAccounting.Record(session, round, phase,
                        e.Result.UnblockedDamage, e.Result.BlockedDamage, e.Result.OverkillDamage,
                        dealerId, sourceId, sourceName, e.CardSource != null, e.Dealer?.PetOwner != null,
                        Owner(e.Dealer) == null && dealerId != null, credit);
                    if (attribution.Kind == DamageAttributionKind.PoisonConvention)
                    { sourceId = "effect:poison"; sourceName = "Poison"; }
                }
                session.RecordDamageTrace(new DamageTraceEntry
                {
                    Round = round, Phase = phase, Dealer = e.Dealer?.Name ?? dealer?.Name ?? "",
                    Target = e.Receiver.Name, TargetId = ActorId(e.Receiver),
                    TargetPlayerId = PlayerId(e.Receiver) ?? "", SourceId = sourceId, Source = sourceName,
                    Hp = e.Result.UnblockedDamage, Blocked = e.Result.BlockedDamage, Overkill = e.Result.OverkillDamage,
                    Fatal = e.Result.WasTargetKilled, CountsForMeter = eligible, Attribution = attribution.Kind,
                    Shares = attribution.Shares.ToArray()
                });
                break;
        }
    }
}
