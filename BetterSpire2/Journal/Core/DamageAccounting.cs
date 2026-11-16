#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterSpire2.Journal.Core;

public enum DamageAttributionKind { NativeDealer, PetOwner, CardOwner, PoisonConvention, Unattributed, NotCounted, PowerApplier, DoomConvention }
public sealed record DamageShare(string PlayerId, long Hp, long Blocked, long Overkill);
public sealed record DamageAttribution(DamageAttributionKind Kind, IReadOnlyList<DamageShare> Shares);
// Doom uses the same exact proportional allocation as poison. Strangle is natively
// instanced per applier and therefore has a single, unambiguous owner.
public sealed record EffectDamageCredit(string SourceId, string SourceName,
    string? ApplierId = null, PoisonDamageCredit? StackCredit = null);

/// <summary>One writer for eligible outgoing native results. Overkill and pet subtotals never enter the damage index.</summary>
public static class DamageAccounting
{
    public static DamageAttribution Record(JournalSession session, int round, JournalPhase phase,
        long hp, long blocked, long overkill, string? dealerId, string sourceId, string sourceName,
        bool isCard, bool isPet, bool cardOwnerFallback, PoisonDamageCredit? poison = null,
        EffectDamageCredit? effect = null)
    {
        if (hp < 0 || blocked < 0 || overkill < 0) throw new ArgumentOutOfRangeException(nameof(hp));
        bool effectOwner = dealerId == null && effect?.ApplierId != null;
        dealerId ??= effect?.ApplierId;
        if (effect != null) { sourceId = effect.SourceId; sourceName = effect.SourceName; isCard = false; }
        var stackCredit = effect?.StackCredit ?? poison;
        var shares = new Dictionary<string, long[]>(StringComparer.Ordinal);
        bool known = dealerId != null && session.Run?.Players.ContainsKey(dealerId) == true;
        var kind = known ? (effectOwner ? DamageAttributionKind.PowerApplier : isPet ? DamageAttributionKind.PetOwner : cardOwnerFallback
            ? DamageAttributionKind.CardOwner : DamageAttributionKind.NativeDealer)
            : effect?.StackCredit != null ? DamageAttributionKind.DoomConvention
            : poison != null ? DamageAttributionKind.PoisonConvention : DamageAttributionKind.Unattributed;
        if (known) shares[dealerId!] = new[] { hp, blocked, overkill };
        else if (stackCredit != null)
        {
            Allocate(stackCredit, hp, 0); Allocate(stackCredit, blocked, 1); Allocate(stackCredit, overkill, 2);
            if (effect == null) { sourceId = "effect:poison"; sourceName = "Poison"; isCard = false; }
        }
        else shares[""] = new[] { hp, blocked, overkill };
        var result = shares.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new DamageShare(p.Key, p.Value[0], p.Value[1], p.Value[2])).ToArray();
        bool conventional = kind is DamageAttributionKind.PoisonConvention or DamageAttributionKind.DoomConvention;
        foreach (var share in result)
        {
            Append(share.PlayerId, Stat.DamageDealtHp, share.Hp, conventional);
            Append(share.PlayerId, Stat.DamageDealtBlocked, share.Blocked, conventional);
            Append(share.PlayerId, Stat.Overkill, share.Overkill, conventional);
        }
        if (known && isPet) Append(dealerId!, Stat.PetDamageDealtHp, hp, false);
        return new(kind, result);

        void Allocate(PoisonDamageCredit credit, long amount, int metric)
        {
            foreach (var share in credit.Allocate(amount))
            {
                string id = session.Run?.Players.ContainsKey(share.PlayerId) == true ? share.PlayerId : "";
                if (!shares.TryGetValue(id, out var amounts)) shares[id] = amounts = new long[3];
                amounts[metric] = checked(amounts[metric] + share.Amount);
            }
        }
        void Append(string id, Stat metric, long amount, bool isConventional)
        {
            if (id.Length == 0) session.AppendUnattributed(round, metric, amount);
            else session.Append(new JournalEvent(id, round, phase, metric, amount, sourceId, sourceName, isCard, isConventional));
        }
    }
}
