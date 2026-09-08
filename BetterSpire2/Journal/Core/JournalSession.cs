#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterSpire2.Journal.Core;

/// <summary>Single source of truth: per-round observed statistics. No Godot nodes or game objects.</summary>
public sealed class JournalSession
{
    public RunJournal? Run { get; private set; }
    public CombatRecord? Active { get; private set; }
    public long Revision { get; private set; }
    // Presentation/coverage changes and outgoing HP/block only, not every draw or energy event.
    public long DamageRevision { get; private set; }
    private readonly DamageTraceBuffer _trace = new();
    public DamageTotalsIndex DamageIndex { get; } = new();
    public DamageTotalsIndex CombatDamageIndex { get; } = new();
    private readonly Dictionary<int, RoundRecord> _roundIndex = new();
    public void Attach(RunJournal run)
    {
        Run = run ?? throw new ArgumentNullException(nameof(run));
        _trace.Reset(run);
        Active = null;
        _roundIndex.Clear(); DamageIndex.Rebuild(run); CombatDamageIndex.Clear();
        Revision++; DamageRevision++;
    }
    public void RegisterPlayer(JournalPlayer player)
    {
        if (Run == null || string.IsNullOrEmpty(player.Id)) return;
        if (Run.Players.TryGetValue(player.Id, out var old) && old.Name == player.Name && old.IsLocal == player.IsLocal) return;
        Run.Players[player.Id] = player;
        Revision++; DamageRevision++;
    }
    public CombatRecord BeginCombat(string key, string encounter, int act, int floor, bool partial)
    {
        if (Run == null) throw new InvalidOperationException("Attach a run before starting combat.");
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A combat key is required.", nameof(key));
        if (Active?.Outcome == CombatOutcome.InProgress) Active.Outcome = CombatOutcome.Interrupted;
        int existing = Run.Combats.FindIndex(c => c.Key == key);
        int attempts = existing < 0 ? 0 : Run.Combats[existing].ReplacedAttempts + 1;
        // A restored save replaces its old branch. Never add a replay to the old attempt's total.
        if (existing >= 0) Run.Combats.RemoveRange(existing, Run.Combats.Count - existing);
        else Run.Combats.RemoveAll(c => c.Floor > floor);
        Active = new CombatRecord { Key = key, Encounter = encounter, Act = act, Floor = floor,
            Partial = partial, ReplacedAttempts = attempts, DamageTraceVersion = 1 };
        Run.Combats.Add(Active);
        _roundIndex.Clear(); CombatDamageIndex.Clear();
        if (Run.Combats.Count > 512) { Run.Combats.RemoveAt(0); Run.Partial = true; }
        DamageIndex.Rebuild(Run); // Load/replay boundaries only, never each HUD refresh.
        _trace.Reset(Run);
        Revision++; DamageRevision++;
        return Active;
    }
    public void ObserveRound(int number)
    {
        if (Active == null) return;
        number = Math.Clamp(number, 0, 1000000);
        if (number <= Active.LatestRound) return;
        Active.LatestRound = number;
        Revision++;
    }
    public void Append(JournalEvent item)
    {
        if (Active == null || Active.Outcome != CombatOutcome.InProgress || Run == null || item.Amount <= 0 ||
            !Run.Players.ContainsKey(item.PlayerId)) return;
        var bucket = GetRound(item.Round);
        if (bucket == null) return;
        if (!bucket.Players.TryGetValue(item.PlayerId, out var actor))
            bucket.Players[item.PlayerId] = actor = new ActorStats();
        long previous = actor.Totals[item.Metric];
        actor.Add(item);
        long delta = actor.Totals[item.Metric] - previous;
        DamageIndex.Add(item.PlayerId, item.Metric, delta);
        CombatDamageIndex.Add(item.PlayerId, item.Metric, delta);
        Revision++;
        if (delta > 0 && item.Metric is Stat.DamageDealtHp or Stat.DamageDealtBlocked) DamageRevision++;
    }
    public void AppendUnattributed(int round, Stat metric, long amount)
    {
        if (Run == null || Active?.Outcome != CombatOutcome.InProgress || amount <= 0 ||
            metric is not (Stat.DamageDealtHp or Stat.DamageDealtBlocked or Stat.Overkill)) return;
        var bucket = GetRound(round);
        if (bucket == null) return;
        long previous = bucket.UnattributedDamage[metric];
        bucket.UnattributedDamage.Add(metric, amount);
        long delta = bucket.UnattributedDamage[metric] - previous;
        DamageIndex.AddUnattributed(metric, delta);
        CombatDamageIndex.AddUnattributed(metric, delta);
        Revision++;
        if (delta > 0 && metric is Stat.DamageDealtHp or Stat.DamageDealtBlocked) DamageRevision++;
    }
    /// <summary>Primitive native results only. Recording an explanation never increments damage totals.</summary>
    public void RecordDamageTrace(DamageTraceEntry entry)
    {
        if (Run == null || Active?.Outcome != CombatOutcome.InProgress) return;
        ObserveRound(entry.Round);
        _trace.Append(Run, Active, entry);
        Revision++;
    }
    private RoundRecord? GetRound(int number)
    {
        if (Active == null) return null;
        int round = Math.Clamp(number, 0, 1000000);
        ObserveRound(round);
        if (!_roundIndex.TryGetValue(round, out var bucket))
        {
            // Pathological infinite fights cannot consume unbounded memory.
            if (Active.Rounds.Count >= 4096) { Active.Partial = true; Revision++; DamageRevision++; return null; }
            bucket = new RoundRecord { Number = round };
            Active.Rounds.Add(bucket);
            _roundIndex[round] = bucket;
        }
        return bucket;
    }
    public StatLine QueryUnattributed(JournalScope scope, string? combatKey, int round)
    {
        var result = new StatLine();
        if (Run == null) return result;
        foreach (var combat in Run.Combats)
        {
            if (scope != JournalScope.Run && combat.Key != combatKey) continue;
            foreach (var bucket in combat.Rounds)
                if (scope != JournalScope.Round || bucket.Number == round) result.Add(bucket.UnattributedDamage);
        }
        return result;
    }
    public void MarkPartial()
    {
        if (Active == null || Active.Partial) return;
        Active.Partial = true;
        Revision++; DamageRevision++;
    }
    public void Complete(CombatOutcome outcome)
    {
        if (Active == null || outcome == CombatOutcome.InProgress || Active.Outcome == outcome) return;
        // A late generic teardown must not replace a known win or defeat.
        if (Active.Outcome != CombatOutcome.InProgress && outcome == CombatOutcome.Interrupted) return;
        Active.Outcome = outcome;
        Revision++; DamageRevision++;
    }
    public void DetachCombat() { Active = null; _roundIndex.Clear(); CombatDamageIndex.Clear(); Revision++; DamageRevision++; }
    public void SetRunStatus(string status)
    {
        if (Run == null || Run.Status == status) return;
        Run.Status = status;
        Revision++; DamageRevision++;
    }
    public StatLine QueryTotals(JournalScope scope, string? combatKey, int round, string? playerId)
    {
        var result = new StatLine();
        if (Run == null) return result;
        foreach (var combat in Run.Combats)
        {
            if (scope != JournalScope.Run && combat.Key != combatKey) continue;
            foreach (var bucket in combat.Rounds)
            {
                if (scope == JournalScope.Round && bucket.Number != round) continue;
                foreach (var actor in bucket.Players)
                    if (playerId == null || playerId == actor.Key) result.Add(actor.Value.Totals);
            }
        }
        return result;
    }
    public ActorStats Query(JournalScope scope, string? combatKey, int round, string? playerId)
    {
        var result = new ActorStats();
        if (Run == null) return result;
        foreach (var combat in Run.Combats)
        {
            if (scope != JournalScope.Run && combat.Key != combatKey) continue;
            foreach (var bucket in combat.Rounds)
            {
                if (scope == JournalScope.Round && bucket.Number != round) continue;
                foreach (var player in bucket.Players)
                    if (playerId == null || player.Key == playerId) result.Merge(player.Value);
            }
        }
        return result;
    }
}
