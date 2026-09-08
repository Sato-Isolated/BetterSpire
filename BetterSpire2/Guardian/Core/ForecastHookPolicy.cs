#nullable enable
using System;
namespace BetterSpire2.Guardian.Core;

/// <summary>New lifecycle reactions default to uncertain rather than silently supported.</summary>
public static class ForecastHookPolicy
{
    public static bool IsReaction(string methodName) =>
        methodName.StartsWith("Before", StringComparison.Ordinal) ||
        methodName.StartsWith("After", StringComparison.Ordinal);
}
