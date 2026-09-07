#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BetterSpire2.Journal.Core;

public sealed record PoisonDamageShare(string PlayerId, long Amount);

/// <summary>Immutable provenance captured before one poison damage command starts.</summary>
public sealed class PoisonDamageCredit
{
    private readonly string[] _players;
    private readonly BigInteger[] _weights;
    private readonly BigInteger _total;

    internal PoisonDamageCredit(IReadOnlyDictionary<string, decimal> weights)
    {
        var ordered = weights.Where(p => p.Value > 0)
            .OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
        _players = ordered.Select(p => p.Key).ToArray();
        _weights = new BigInteger[ordered.Length];
        // Convert decimals to common exact integer units once. This avoids both
        // overflow and lost integer damage when a large amount is apportioned.
        int scale = ordered.Length == 0 ? 0 : ordered.Max(p => Scale(p.Value));
        for (int i = 0; i < ordered.Length; i++)
        {
            int[] bits = decimal.GetBits(ordered[i].Value);
            BigInteger mantissa = (uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) +
                ((BigInteger)(uint)bits[2] << 64);
            _weights[i] = mantissa * BigInteger.Pow(10, scale - Scale(ordered[i].Value));
            _total += _weights[i];
        }
    }
    private static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xff;

    /// <summary>Largest remainders, stable network-ID ties; sum equals observed damage exactly.</summary>
    public IReadOnlyList<PoisonDamageShare> Allocate(long amount)
    {
        if (amount <= 0) return Array.Empty<PoisonDamageShare>();
        if (_total <= 0) return new[] { new PoisonDamageShare("", amount) };
        var amounts = new long[_players.Length];
        var remainders = new BigInteger[_players.Length];
        long remaining = amount;
        for (int i = 0; i < amounts.Length; i++)
        {
            amounts[i] = (long)BigInteger.DivRem((BigInteger)amount * _weights[i], _total, out remainders[i]);
            remaining -= amounts[i];
        }
        foreach (int i in Enumerable.Range(0, amounts.Length)
            .OrderByDescending(i => remainders[i]).ThenBy(i => _players[i], StringComparer.Ordinal)
            .Take(checked((int)remaining))) amounts[i]++;
        return Enumerable.Range(0, amounts.Length).Where(i => amounts[i] > 0)
            .Select(i => new PoisonDamageShare(_players[i], amounts[i])).ToArray();
    }

    public void Append(JournalSession session, int round, JournalPhase phase,
        Stat metric, long amount)
    {
        if (metric is not (Stat.DamageDealtHp or Stat.DamageDealtBlocked or Stat.Overkill))
            throw new ArgumentOutOfRangeException(nameof(metric));
        foreach (var share in Allocate(amount))
        {
            if (share.PlayerId.Length > 0 && session.Run?.Players.ContainsKey(share.PlayerId) == true)
                session.Append(new JournalEvent(share.PlayerId, round, phase, metric,
                    share.Amount, "effect:poison", "Poison"));
            else session.AppendUnattributed(round, metric, share.Amount);
        }
    }
}
