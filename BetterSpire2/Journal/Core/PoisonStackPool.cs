#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterSpire2.Journal.Core;

/// <summary>
/// Meter convention for a shared poison pool, not a change to native ownership.
/// Additions belong to their observed applier; decay is proportional. Missing
/// provenance has its own empty-ID share and is never assigned to a local player.
/// One instance per native poison power (not a monster's name or a player's slot).
/// </summary>
public sealed class PoisonStackPool
{
    public const int MaxContributors = 256;
    private readonly Dictionary<string, decimal> _weights = new(StringComparer.Ordinal);
    public int Total { get; private set; }

    public void ObserveChange(string? playerId, decimal amount)
    {
        // Native ApplyInternal/ModifyAmount truncate decimal stack changes to int.
        int delta = (int)Math.Clamp(decimal.Truncate(amount), int.MinValue, int.MaxValue);
        if (delta > 0)
        {
            int added = Math.Min(delta, int.MaxValue - Total);
            if (added == 0) return;
            string id = playerId ?? "";
            if (!_weights.ContainsKey(id) && _weights.Count >= MaxContributors) id = "";
            _weights.TryGetValue(id, out decimal old);
            _weights[id] = old + added;
            Total += added;
        }
        else if (delta < 0) Resize((int)Math.Max(0L, (long)Total + delta));
    }

    /// <summary>Reconcile at the actual tick, without inventing an applier for unseen stacks.</summary>
    public PoisonDamageCredit Capture(int actualStacks)
    {
        actualStacks = Math.Max(0, actualStacks);
        if (actualStacks > Total) ObserveChange(null, actualStacks - Total);
        else if (actualStacks < Total) Resize(actualStacks);
        return new PoisonDamageCredit(_weights);
    }

    private void Resize(int total)
    {
        if (total <= 0) { _weights.Clear(); Total = 0; return; }
        if (Total <= 0 || total == Total) return;
        // Keep fractional ownership between ticks: integer rounding at each decay
        // would repeatedly remove stacks from one player on tied contributions.
        string[] ids = _weights.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        decimal remaining = total;
        for (int i = 0; i < ids.Length; i++)
        {
            decimal weight = i == ids.Length - 1 ? remaining :
                Math.Min(remaining, _weights[ids[i]] * total / Total);
            remaining -= weight;
            if (weight <= 0) _weights.Remove(ids[i]);
            else _weights[ids[i]] = weight;
        }
        Total = total;
    }
}
