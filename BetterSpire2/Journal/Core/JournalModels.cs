#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterSpire2.Journal.Core;

public enum JournalScope { Round, Combat, Run }
public enum CombatOutcome { InProgress, Won, Lost, Interrupted }
public enum JournalPhase { Setup, Player, Enemy, Unknown }
public enum Stat
{
    DamageDealtHp, DamageDealtBlocked, Overkill, HpLost, DamageBlocked,
    BlockGained, BlockRemoved, BlockExpired, Healing, PetHpLost, PetHealing,
    PetDamageDealtHp, CardsPlayed, CardsDrawn, CardsDiscarded, CardsExhausted,
    CardsGenerated, EnergySpent, EnergyGained, StarsGained, StarsSpent,
    OrbsChanneled, PotionsUsed
}

/// <summary>Positive, observed quantities only. Never forecast values or a net "damage".</summary>
public sealed class StatLine
{
    public Dictionary<Stat, long> Values { get; set; } = new();
    public long this[Stat key] => Values.TryGetValue(key, out long value) ? value : 0;
    public void Add(Stat key, long amount)
    {
        if (amount <= 0) return;
        long old = this[key];
        Values[key] = old > long.MaxValue - amount ? long.MaxValue : old + amount;
    }
    public void Add(StatLine other) { foreach (var pair in other.Values) Add(pair.Key, pair.Value); }
}

public sealed class JournalSource
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsCard { get; set; }
    public StatLine Stats { get; set; } = new();
}

public sealed class ActorStats
{
    public StatLine Totals { get; set; } = new();
    public Dictionary<string, JournalSource> Sources { get; set; } = new(StringComparer.Ordinal);
    public void Add(JournalEvent item)
    {
        Totals.Add(item.Metric, item.Amount);
        if (item.SourceId.Length == 0) return;
        // Bound source cardinality even for mods generating unlimited unique cards.
        string key = Sources.ContainsKey(item.SourceId) || Sources.Count < 256 ? item.SourceId : "other:overflow";
        if (!Sources.TryGetValue(key, out var source))
        {
            source = new JournalSource { Id = key, Name = key == "other:overflow" ? "…" : item.SourceName,
                IsCard = key != "other:overflow" && item.IsCard };
            Sources.Add(key, source);
        }
        source.Stats.Add(item.Metric, item.Amount);
    }
    public void Merge(ActorStats other)
    {
        Totals.Add(other.Totals);
        foreach (var source in other.Sources.Values)
        {
            string key = Sources.ContainsKey(source.Id) || Sources.Count < 256 ? source.Id : "other:overflow";
            if (!Sources.TryGetValue(key, out var dest))
            {
                dest = new JournalSource { Id = key, Name = key == "other:overflow" ? "…" : source.Name,
                    IsCard = key != "other:overflow" && source.IsCard };
                Sources.Add(key, dest);
            }
            dest.Stats.Add(source.Stats);
        }
    }
}

public sealed class JournalPlayer
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsLocal { get; set; }
}
public sealed class RoundRecord
{
    // Zero is setup, NOT round one. Enemy phase stays attached to the game's round number.
    public int Number { get; set; }
    public StatLine UnattributedDamage { get; set; } = new();
    public Dictionary<string, ActorStats> Players { get; set; } = new(StringComparer.Ordinal);
}
public sealed class CombatRecord
{
    public string Key { get; set; } = "";
    public string Encounter { get; set; } = "";
    public int Act { get; set; }
    public int Floor { get; set; }
    public int LatestRound { get; set; }
    public CombatOutcome Outcome { get; set; }
    public bool Partial { get; set; }
    public int ReplacedAttempts { get; set; }
    // Zero means a legacy archive with totals but no chronological trace.
    public int DamageTraceVersion { get; set; }
    public bool DamageTraceTruncated { get; set; }
    public long LastDamageSequence { get; set; }
    public List<DamageTraceEntry> DamageTrace { get; set; } = new();
    public List<RoundRecord> Rounds { get; set; } = new();
}
public sealed class RunJournal
{
    public int SchemaVersion { get; set; } = 1;
    // Zero identifies older archives where source-less enemy damage was not recorded.
    public int DamageMeterCoverageVersion { get; set; }
    public string Key { get; set; } = "";
    public string Seed { get; set; } = "";
    public long GameStartTime { get; set; }
    public bool Partial { get; set; }
    public string Status { get; set; } = "ongoing";
    public Dictionary<string, JournalPlayer> Players { get; set; } = new(StringComparer.Ordinal);
    public List<CombatRecord> Combats { get; set; } = new();
}

public sealed record JournalEvent(string PlayerId, int Round, JournalPhase Phase, Stat Metric,
    long Amount, string SourceId = "", string SourceName = "", bool IsCard = false);
