#nullable enable
namespace BetterSpire2.DamageMeter.Core;

public enum DamageMeterScope { Run, Combat, Round }

public static class DamageMeterContext
{
    // The map wins even when opened temporarily during a fight. Selecting a scope is read-only.
    // Keep the just-completed fight visible in its room until the player opens/leaves for the map.
    public static DamageMeterScope Select(bool mapVisible, bool inCombatRoom, bool showCurrentRound = false) =>
        !mapVisible && inCombatRoom
            ? showCurrentRound ? DamageMeterScope.Round : DamageMeterScope.Combat
            : DamageMeterScope.Run;
}
