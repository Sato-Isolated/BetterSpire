#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BetterSpire2.Journal.Core;

namespace BetterSpire2.DamageMeter.Core;

public sealed record DamageMeterRow(string Id, string Name, bool IsLocal, int Rank,
    decimal Damage, int ShareTenths, double RelativeToLeader, bool IsGroup = false);

public sealed class DamageMeterSnapshot
{
    public DamageMeterScope Scope { get; init; }
    public IReadOnlyList<DamageMeterRow> Rows { get; init; } = Array.Empty<DamageMeterRow>();
    public decimal AttributedDamage { get; init; }
    // Separate, never awarded to the local player or silently included in a player's share.
    public decimal UnattributedDamage { get; init; }
    public bool IsPartial { get; init; }
    public bool IncludesBlock { get; init; }
    public bool IsMultiplayer => Rows.Count > 1;
}

/// <summary>Read-only projection of the journal. No second event collector, time-based DPS or forecasting.</summary>
public static class DamageMeterBuilder
{
    public const int VisiblePlayerLimit = 4;
    public static DamageMeterSnapshot Build(RunJournal? run, bool includeBlock)
    {
        if (run == null) return new DamageMeterSnapshot { IncludesBlock = includeBlock };
        var totals = run.Players.Keys.ToDictionary(id => id, _ => 0m, StringComparer.Ordinal);
        decimal unknown = 0;
        foreach (var combat in run.Combats)
        foreach (var round in combat.Rounds)
        {
            unknown += Amount(round.UnattributedDamage, includeBlock);
            foreach (var player in round.Players)
                if (totals.ContainsKey(player.Key))
                    totals[player.Key] += Amount(player.Value.Totals, includeBlock);
        }
        return FromTotals(run, totals, unknown, includeBlock);
    }
    public static DamageMeterSnapshot BuildLive(JournalSession session, bool includeBlock,
        DamageMeterScope scope = DamageMeterScope.Run)
    {
        var run = session.Run;
        if (run == null) return new DamageMeterSnapshot { IncludesBlock = includeBlock, Scope = scope };
        var index = scope == DamageMeterScope.Combat ? session.CombatDamageIndex : session.DamageIndex;
        var totals = new Dictionary<string, decimal>(run.Players.Count, StringComparer.Ordinal);
        foreach (var player in run.Players.Keys)
        {
            index.Players.TryGetValue(player, out var amount);
            totals[player] = amount.Hp + (includeBlock ? amount.Blocked : 0);
        }
        var unattributed = index.Unattributed;
        // An incomplete old fight must not mark a fully observed new fight as partial.
        bool partial = scope == DamageMeterScope.Combat
            ? session.Active == null || session.Active.Partial
            : RunIsPartial(run);
        return FromTotals(run, totals, unattributed.Hp + (includeBlock ? unattributed.Blocked : 0),
            includeBlock, scope, partial);
    }
    private static bool RunIsPartial(RunJournal run) => run.Partial ||
        run.DamageMeterCoverageVersion < 1 || run.Combats.Any(c => c.Partial);

    private static DamageMeterSnapshot FromTotals(RunJournal run, Dictionary<string, decimal> totals,
        decimal unknown, bool includeBlock, DamageMeterScope scope = DamageMeterScope.Run, bool? partial = null)
    {
        // Identity, not names, distinguishes two players with identical display names.
        // Fixed identity tie-break prevents rows oscillating after unrelated journal updates.
        var ordered = run.Players.Values.OrderByDescending(p => totals[p.Id])
            .ThenBy(p => p.Id, StringComparer.Ordinal).ToArray();
        decimal total = totals.Values.Sum();
        decimal leader = ordered.Length == 0 ? 0 : totals[ordered[0].Id];
        int[] shares = AllocateShares(ordered.Select(p => totals[p.Id]).ToArray());
        int rank = 0;
        decimal previous = -1;
        var rows = new List<DamageMeterRow>();
        for (int i = 0; i < ordered.Length; i++)
        {
            var player = ordered[i];
            decimal damage = totals[player.Id];
            if (damage != previous) rank = i + 1;
            previous = damage;
            rows.Add(new DamageMeterRow(player.Id, player.Name, player.IsLocal, rank, damage,
                shares[i], leader > 0 ? (double)(damage / leader) : 0));
        }
        return new DamageMeterSnapshot { Rows = rows, AttributedDamage = total,
            UnattributedDamage = unknown, IncludesBlock = includeBlock, Scope = scope,
            IsPartial = partial ?? RunIsPartial(run) };
    }
    private static decimal Amount(StatLine line, bool includeBlock) =>
        (decimal)line[Stat.DamageDealtHp] + (includeBlock ? line[Stat.DamageDealtBlocked] : 0);

    /// <summary>Largest remainder allocation: displayed shares sum to 100.0%, including ties.</summary>
    public static int[] AllocateShares(IReadOnlyList<decimal> amounts)
    {
        var result = new int[amounts.Count];
        if (amounts.Any(a => a < 0)) throw new ArgumentOutOfRangeException(nameof(amounts));
        decimal total = amounts.Sum();
        if (total == 0) return result;
        var remainders = new decimal[amounts.Count];
        for (int i = 0; i < amounts.Count; i++)
        {
            decimal exact = amounts[i] / total * 1000m;
            result[i] = (int)decimal.Floor(exact);
            remainders[i] = exact - result[i];
        }
        int missing = 1000 - result.Sum();
        foreach (int index in Enumerable.Range(0, amounts.Count)
            .Where(i => amounts[i] > 0).OrderByDescending(i => remainders[i]).ThenBy(i => i).Take(missing))
            result[index]++;
        return result;
    }

    /// <summary>Native parties fit without folding. Larger modded parties keep the local player visible.</summary>
    public static IReadOnlyList<DamageMeterRow> VisibleRows(DamageMeterSnapshot snapshot, bool french)
    {
        if (snapshot.Rows.Count <= VisiblePlayerLimit) return snapshot.Rows;
        var shown = snapshot.Rows.Take(VisiblePlayerLimit).ToList();
        var local = snapshot.Rows.FirstOrDefault(p => p.IsLocal);
        if (local != null && !shown.Any(p => p.Id == local.Id)) shown[^1] = local;
        var ids = shown.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var omitted = snapshot.Rows.Where(p => !ids.Contains(p.Id)).ToArray();
        shown.Add(new DamageMeterRow("", (french ? "Autres" : "Others") + " (" + omitted.Length + ")",
            false, 0, omitted.Sum(p => p.Damage), omitted.Sum(p => p.ShareTenths), 0, true));
        return shown;
    }
}

public static class DamageMeterText
{
    public static string Number(decimal value, bool french)
    {
        var culture = CultureInfo.GetCultureInfo(french ? "fr-FR" : "en-US");
        if (value < 1000000m) return value.ToString("N0", culture);
        if (value < 1000000000m) return (value / 1000000m).ToString("0.##", culture) + " M";
        if (value < 1000000000000m) return (value / 1000000000m).ToString("0.##", culture) + (french ? " Md" : " B");
        return value.ToString("0.##E+0", CultureInfo.InvariantCulture);
    }
    public static string Share(int tenths, bool french) => (tenths / 10m)
        .ToString("0.#", CultureInfo.GetCultureInfo(french ? "fr-FR" : "en-US")) + " %";
    public static string Name(string name, bool french)
    {
        // Name is a raw platform nickname, not "nickname · character".
        // A literal middle dot or brackets may be part of the user's Steam nickname.
        string first = PlayerNameText.Clean(name);
        if (first.Length == 0) return french ? "Joueur" : "Player";
        var elements = StringInfo.GetTextElementEnumerator(first);
        var result = new System.Text.StringBuilder();
        int count = 0;
        while (elements.MoveNext())
        {
            if (count++ >= 20) { result.Append('…'); break; }
            result.Append(elements.GetTextElement());
        }
        return result.ToString();
    }
}
