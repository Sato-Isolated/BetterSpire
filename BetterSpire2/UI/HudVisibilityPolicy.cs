#nullable enable
namespace BetterSpire2.UI;

/// <summary>
/// Passive HUDs coexist. Only native modal screens and the full journal suspend
/// their display. A visible HUD area is not necessarily an input-capturing area.
/// Pure policy: no Godot objects, persisted state, or changes to damage collection.
/// </summary>
internal static class HudVisibilityPolicy
{
    internal static bool IsTransientBlocker(bool nativeModalOpen, bool journalOpen) =>
        nativeModalOpen || journalOpen;

    internal static bool ShowMeter(bool enabled, bool activeRun, bool onlyCombat,
        bool combatScope, bool transientBlocked) =>
        enabled && activeRun && !transientBlocked && (!onlyCombat || combatScope);

    internal static bool ShowHand(bool transientBlocked, bool mapVisible) =>
        !transientBlocked && !mapVisible;
}
