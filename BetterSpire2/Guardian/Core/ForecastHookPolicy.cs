#nullable enable
using System;
namespace BetterSpire2.Guardian.Core;

/// <summary>Unsimulated lifecycle reactions and pre-block redirects downgrade forecast certainty.</summary>
public static class ForecastHookPolicy
{
    // These audited base-game callbacks run outside the current forecast window.
    // The caller MUST verify base-game ownership before applying this classification.
    // Do not whitelist all victory hooks: unknown death/victory chains stay uncertain.
    public static bool IsOutsideForecastWindow(string modelName, string hookName) =>
        modelName == "BurningBlood" && hookName == "AfterCombatVictory" ||
        modelName == "Wuppy" && hookName is "BeforeCombatStart" or "AfterObtained";

    public static bool IsReaction(string methodName) =>
        methodName == "ModifyPreBlockDamageTarget" ||
        methodName.StartsWith("Before", StringComparison.Ordinal) ||
        methodName.StartsWith("After", StringComparison.Ordinal);
}
