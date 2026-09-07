#nullable enable
namespace BetterSpire2.DamageMeter.Core;

public enum DamageMeterScope { Run, Combat }

public static class DamageMeterContext
{
    // The map wins even when opened temporarily during a fight. Selecting a scope is read-only.
    // Keep the just-completed fight visible in its room until the player opens/leaves for the map.
    public static DamageMeterScope Select(bool mapVisible, bool inCombatRoom) =>
        !mapVisible && inCombatRoom ? DamageMeterScope.Combat : DamageMeterScope.Run;
}
