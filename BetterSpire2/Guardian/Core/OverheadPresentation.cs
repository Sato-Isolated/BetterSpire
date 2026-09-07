#nullable enable
using System;
using System.Globalization;

namespace BetterSpire2.Guardian.Core;

public enum OverheadSeverity { Normal, Loss, Uncertain, Lethal }
public readonly record struct OverheadReadout(string Text, OverheadSeverity Severity);

/// <summary>Pure presentation: the simulator owns all arithmetic, healing and protection rules.</summary>
public static class OverheadPresentation
{
    public const string Resolving = "…";
    // Missing forecasts are hidden, never represented as a guessed zero or a punctuation marker.
    public const string Unavailable = "";

    public static OverheadReadout Format(ActorForecast actor, bool partial)
    {
        ArgumentNullException.ThrowIfNull(actor);
        // Never recalculate StartHp - HpLost here: healing and revivals make it incorrect.
        // HpLost is actual projected HP loss, not incoming damage or the net loss after healing.
        string text = actor.FinalHp.ToString(CultureInfo.InvariantCulture) + " (" +
            actor.HpLost.ToString(CultureInfo.InvariantCulture) + ")";
        // Numbers only. Amber carries uncertainty/revival; F2 retains the full explanation.
        // Lethal remains red even when partial, so the warning is never visually downgraded.
        var severity = actor.FinalHp <= 0 ? OverheadSeverity.Lethal
            : partial || actor.Revivals > 0 ? OverheadSeverity.Uncertain
            : actor.HpLost > 0 ? OverheadSeverity.Loss : OverheadSeverity.Normal;
        return new(text, severity);
    }

    public static bool ShouldShow(ActorForecast actor, bool partial, bool onlyDanger)
        => !onlyDanger || partial || actor.HpLost > 0 || actor.Revivals > 0 || actor.FinalHp <= 0;
}
