#nullable enable
using System.Reflection;

namespace BetterSpire2.Core;

internal static class ModBuildInfo
{
    internal static string Version { get; } =
        typeof(ModEntry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ModEntry).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
