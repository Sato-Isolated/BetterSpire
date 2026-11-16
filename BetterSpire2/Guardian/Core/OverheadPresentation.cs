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

    public static OverheadReadout Format(ActorForecast actor, bool partial,
        bool branchEnumerationComplete = true)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!branchEnumerationComplete)
            return new("?", OverheadSeverity.Uncertain);

        string finalHp = Range(actor.MinimumFinalHp, actor.MaximumFinalHp);
        string hpLost = Range(actor.MinimumHpLost, actor.MaximumHpLost);
        string text = finalHp + " (" + hpLost + ")";
        // A range is a real outcome difference and uses the same uncertainty color as partial models.
        bool alwaysLethal = actor.MinimumFinalHp == 0 && actor.MaximumFinalHp == 0;
        var severity = partial ? OverheadSeverity.Uncertain
            : alwaysLethal ? OverheadSeverity.Lethal
            : actor.HasVariableOutcome || actor.Revivals > 0 ? OverheadSeverity.Uncertain
            : actor.HpLost > 0 ? OverheadSeverity.Loss : OverheadSeverity.Normal;
        return new(text, severity);
    }

    public static bool ShouldShow(ActorForecast actor, bool partial, bool onlyDanger,
        bool branchEnumerationComplete = true)
        => !onlyDanger || partial || !branchEnumerationComplete || actor.MaximumHpLost > 0 ||
            actor.Revivals > 0 || actor.MinimumFinalHp <= 0;

    private static string Range(int minimum, int maximum) => minimum == maximum
        ? minimum.ToString(CultureInfo.InvariantCulture)
        : minimum.ToString(CultureInfo.InvariantCulture) + "–" + maximum.ToString(CultureInfo.InvariantCulture);
}
