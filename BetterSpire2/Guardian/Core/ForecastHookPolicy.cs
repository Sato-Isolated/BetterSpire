#nullable enable
using System;
namespace BetterSpire2.Guardian.Core;

/// <summary>New lifecycle reactions default to uncertain rather than silently supported.</summary>
public static class ForecastHookPolicy
{
    // Only this audited base-game effect is outside the current forecast window.
    // The caller MUST verify base-game ownership before applying this classification.
    // Do not whitelist all victory hooks: unknown death/victory chains stay uncertain.
    public static bool IsOutsideForecastWindow(string modelName, string hookName) =>
        modelName == "BurningBlood" && hookName == "AfterCombatVictory";

    public static bool IsReaction(string methodName) =>
        methodName.StartsWith("Before", StringComparison.Ordinal) ||
        methodName.StartsWith("After", StringComparison.Ordinal);
}
