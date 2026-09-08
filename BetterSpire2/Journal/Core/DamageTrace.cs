#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BetterSpire2.Journal.Core;

/// <summary>An observed result, not a reconstruction of pre-damage reductions. No live game references.</summary>
public sealed record DamageTraceEntry
{
    public long Sequence { get; init; }
    public int Round { get; init; }
    public JournalPhase Phase { get; init; }
    public string Dealer { get; init; } = "";
    public string Target { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string TargetPlayerId { get; init; } = "";
    public string SourceId { get; init; } = "";
    public string Source { get; init; } = "";
    public bool CountsForMeter { get; init; }
    public bool Fatal { get; init; }
    public long Hp { get; init; }
    public long Blocked { get; init; }
    public long Overkill { get; init; }
    public DamageAttributionKind Attribution { get; init; }
    public DamageShare[] Shares { get; init; } = Array.Empty<DamageShare>();

    internal DamageTraceEntry Snapshot(long sequence) => this with
    {
        Sequence = sequence, Round = Math.Clamp(Round, 0, 1000000),
        Dealer = Clip(Dealer), Target = Clip(Target), TargetId = Clip(TargetId),
        TargetPlayerId = Clip(TargetPlayerId), SourceId = Clip(SourceId), Source = Clip(Source),
        Shares = Shares.Select(s => s with { }).ToArray()
    };
    private static string Clip(string value) => value.Length <= 160 ? value : value[..160];
    // Conservative JSON-size estimate: escaping can expand a UTF-16 character to six bytes.
    internal long Weight => 512L + 6L * (Dealer.Length + Target.Length + TargetId.Length +
        TargetPlayerId.Length + SourceId.Length + Source.Length) + Shares.Sum(s => 192L + 6L * s.PlayerId.Length);
}

/// <summary>Bounded chronological diagnostics; pruning never removes any aggregate statistic.</summary>
internal sealed class DamageTraceBuffer
{
    internal const int MaxPerCombat = 2048, MaxPerRun = 8192;
    internal const long MaxWeight = 4L * 1024 * 1024;
    private int _count;
    private long _weight;
    internal void Reset(RunJournal run)
    {
        _count = run.Combats.Sum(c => c.DamageTrace.Count);
        _weight = run.Combats.Sum(c => c.DamageTrace.Sum(e => e.Weight));
    }
    internal void Append(RunJournal run, CombatRecord combat, DamageTraceEntry entry)
    {
        ValidateEntry(run, entry, requireSequence: false);
        var copy = entry.Snapshot(checked(combat.LastDamageSequence + 1));
        combat.LastDamageSequence = copy.Sequence;
        combat.DamageTrace.Add(copy); _count++; _weight += copy.Weight;
        if (combat.DamageTrace.Count > MaxPerCombat) Prune(combat, 256);
        while (_count > MaxPerRun || _weight > MaxWeight)
        {
            var oldest = run.Combats.FirstOrDefault(c => c.DamageTrace.Count > 0);
            if (oldest == null) break;
            Prune(oldest, Math.Min(256, oldest.DamageTrace.Count));
        }
    }
    private void Prune(CombatRecord combat, int count)
    {
        for (int i = 0; i < count; i++) _weight -= combat.DamageTrace[i].Weight;
        combat.DamageTrace.RemoveRange(0, count); _count -= count; combat.DamageTraceTruncated = true;
    }
    internal static void Validate(RunJournal run)
    {
        int count = 0; long weight = 0;
        foreach (var combat in run.Combats)
        {
            if (combat.DamageTraceVersion is < 0 or > 1 || combat.DamageTrace == null ||
                combat.DamageTrace.Count > MaxPerCombat || combat.LastDamageSequence < 0 ||
                combat.DamageTraceVersion == 0 && combat.DamageTrace.Count != 0)
                throw new InvalidDataException("Invalid damage trace.");
            long previous = 0;
            foreach (var entry in combat.DamageTrace)
            {
                ValidateEntry(run, entry, true);
                if (entry.Sequence <= previous || entry.Sequence > combat.LastDamageSequence || entry.Round > combat.LatestRound)
                    throw new InvalidDataException("Invalid damage trace order.");
                previous = entry.Sequence; count++; weight += entry.Weight;
            }
        }
        if (count > MaxPerRun || weight > MaxWeight) throw new InvalidDataException("Damage trace budget exceeded.");
    }
    private static void ValidateEntry(RunJournal run, DamageTraceEntry e, bool requireSequence)
    {
        if (e == null || e.Hp < 0 || e.Blocked < 0 || e.Overkill < 0 || e.Round is < 0 or > 1000000 ||
            !Enum.IsDefined(e.Phase) || !Enum.IsDefined(e.Attribution) || requireSequence && e.Sequence <= 0 ||
            e.Dealer == null || e.Target == null || e.TargetId == null || e.TargetPlayerId == null ||
            e.SourceId == null || e.Source == null || e.Shares == null || e.Shares.Length > 65 ||
            e.Shares.Any(s => s == null || s.PlayerId == null || s.PlayerId.Length > 160 ||
                s.Hp < 0 || s.Blocked < 0 || s.Overkill < 0 || s.PlayerId.Length > 0 && !run.Players.ContainsKey(s.PlayerId)))
            throw new InvalidDataException("Invalid observed damage result.");
        if (requireSequence && new[] { e.Dealer, e.Target, e.TargetId, e.TargetPlayerId, e.SourceId, e.Source }.Any(s => s.Length > 160))
            throw new InvalidDataException("Damage trace text exceeds its budget.");
        if (e.Shares.Select(s => s.PlayerId).Distinct(StringComparer.Ordinal).Count() != e.Shares.Length ||
            e.CountsForMeter && (e.Shares.Sum(s => (decimal)s.Hp) != e.Hp ||
            e.Shares.Sum(s => (decimal)s.Blocked) != e.Blocked || e.Shares.Sum(s => (decimal)s.Overkill) != e.Overkill))
            throw new InvalidDataException("Damage attribution does not conserve the observed result.");
    }
}
