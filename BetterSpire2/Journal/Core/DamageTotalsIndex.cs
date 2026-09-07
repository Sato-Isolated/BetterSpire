#nullable enable
using System;
using System.Collections.Generic;

namespace BetterSpire2.Journal.Core;

public readonly record struct DamageTotal(decimal Hp, decimal Blocked);

/// <summary>Derived damage totals for one scope. Updated with actual saturated bucket deltas.</summary>
public sealed class DamageTotalsIndex
{
    private readonly Dictionary<string, DamageTotal> _players = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, DamageTotal> Players => _players;
    public DamageTotal Unattributed { get; private set; }
    public void Clear() { _players.Clear(); Unattributed = default; }
    public void Rebuild(RunJournal run)
    {
        Clear();
        foreach (var combat in run.Combats)
        foreach (var round in combat.Rounds)
        {
            AddUnattributed(Stat.DamageDealtHp, round.UnattributedDamage[Stat.DamageDealtHp]);
            AddUnattributed(Stat.DamageDealtBlocked, round.UnattributedDamage[Stat.DamageDealtBlocked]);
            foreach (var player in round.Players)
            {
                Add(player.Key, Stat.DamageDealtHp, player.Value.Totals[Stat.DamageDealtHp]);
                Add(player.Key, Stat.DamageDealtBlocked, player.Value.Totals[Stat.DamageDealtBlocked]);
            }
        }
    }
    public void Add(string player, Stat metric, long delta)
    {
        if (delta <= 0 || metric is not (Stat.DamageDealtHp or Stat.DamageDealtBlocked)) return;
        _players.TryGetValue(player, out var previous);
        _players[player] = Increment(previous, metric, delta);
    }
    public void AddUnattributed(Stat metric, long delta)
    {
        if (delta > 0) Unattributed = Increment(Unattributed, metric, delta);
    }
    private static DamageTotal Increment(DamageTotal value, Stat metric, long delta) => metric switch
    {
        Stat.DamageDealtHp => value with { Hp = value.Hp + delta },
        Stat.DamageDealtBlocked => value with { Blocked = value.Blocked + delta },
        _ => value
    };
}
