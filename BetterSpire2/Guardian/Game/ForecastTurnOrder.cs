#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterSpire2.Guardian.Game;

/// <summary>Ordering at the native-state/forecast boundary, without executing game effects.</summary>
internal static class ForecastTurnOrder
{
    internal static T[] Participants<T>(IEnumerable<T> players, Func<T, bool> isPartOfPlayerTurn) =>
        players.Where(isPartOfPlayerTurn).ToArray();

    internal static IEnumerable<(T Target, int HitIndex)> AttackHits<T>(IReadOnlyList<T> targets, int repeats)
    {
        if (repeats < 0 || repeats > 1000) throw new ArgumentOutOfRangeException(nameof(repeats));
        for (int hit = 0; hit < repeats; hit++)
            foreach (T target in targets)
                yield return (target, hit);
    }
}
