#nullable enable
using System.Collections.Generic;
using BetterSpire2.Journal.Core;

namespace BetterSpire2.Journal.Game;

internal static class DamageObservationCoverage
{
    private static readonly HashSet<string> Missing = new(System.StringComparer.Ordinal);
    internal static void Require(string capability, bool available)
    {
        if (available) Missing.Remove(capability);
        else
        {
            Missing.Add(capability);
            if (JournalService.Session.Active?.Outcome == CombatOutcome.InProgress)
                JournalService.Session.MarkPartial(capability);
            ModLog.Info("Damage observation unavailable: " + capability);
        }
    }
    internal static void Apply(JournalSession session)
    { foreach (string reason in Missing) session.MarkPartial(reason); }
}
