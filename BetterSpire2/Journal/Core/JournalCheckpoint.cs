#nullable enable
using System;
using System.Collections.Generic;

namespace BetterSpire2.Journal.Core;

/// <summary>Call only on the game thread. Returned trees may be serialized by one background writer.</summary>
public sealed class JournalCheckpoint
{
    private RunJournal? _run;
    private readonly Dictionary<CombatRecord, CombatRecord> _completed = new();
    private readonly HashSet<CombatRecord> _retained = new();
    private readonly List<CombatRecord> _removed = new();

    public RunJournal Capture(RunJournal run)
    {
        if (!ReferenceEquals(_run, run)) { _run = run; _completed.Clear(); }
        var copy = new RunJournal { Key = run.Key, Seed = run.Seed, SchemaVersion = run.SchemaVersion,
            DamageMeterCoverageVersion = run.DamageMeterCoverageVersion, GameStartTime = run.GameStartTime,
            Partial = run.Partial, Status = run.Status };
        foreach (var player in run.Players)
            copy.Players[player.Key] = new JournalPlayer { Id = player.Value.Id, Name = player.Value.Name, IsLocal = player.Value.IsLocal };
        _retained.Clear();
        foreach (var combat in run.Combats)
        {
            bool completed = combat.Outcome != CombatOutcome.InProgress;
            if (!completed || !_completed.TryGetValue(combat, out var snapshot) ||
                snapshot.Outcome != combat.Outcome || snapshot.Partial != combat.Partial || snapshot.LatestRound != combat.LatestRound)
            {
                snapshot = CopyCombat(combat);
                if (completed) _completed[combat] = snapshot;
            }
            if (completed) _retained.Add(combat);
            copy.Combats.Add(snapshot);
        }
        _removed.Clear();
        foreach (var combat in _completed.Keys) if (!_retained.Contains(combat)) _removed.Add(combat);
        foreach (var combat in _removed) _completed.Remove(combat);
        return copy;
    }
    private static CombatRecord CopyCombat(CombatRecord source)
    {
        var copy = new CombatRecord { Key = source.Key, Encounter = source.Encounter, Act = source.Act, Floor = source.Floor,
            LatestRound = source.LatestRound, Outcome = source.Outcome, Partial = source.Partial, ReplacedAttempts = source.ReplacedAttempts };
        foreach (var round in source.Rounds)
        {
            var bucket = new RoundRecord { Number = round.Number, UnattributedDamage = CopyLine(round.UnattributedDamage) };
            foreach (var player in round.Players)
            {
                var stats = new ActorStats { Totals = CopyLine(player.Value.Totals) };
                foreach (var item in player.Value.Sources)
                    stats.Sources[item.Key] = new JournalSource { Id = item.Value.Id, Name = item.Value.Name,
                        IsCard = item.Value.IsCard, Stats = CopyLine(item.Value.Stats) };
                bucket.Players[player.Key] = stats;
            }
            copy.Rounds.Add(bucket);
        }
        return copy;
    }
    private static StatLine CopyLine(StatLine source) => new() { Values = new Dictionary<Stat, long>(source.Values) };
}
