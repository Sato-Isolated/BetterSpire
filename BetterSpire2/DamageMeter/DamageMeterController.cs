#nullable enable
using System;
using BetterSpire2.DamageMeter.UI;
using BetterSpire2.DamageMeter.Core;
using BetterSpire2.Journal.Core;
using Godot;

namespace BetterSpire2.DamageMeter;

internal static class DamageMeterController
{
    private static readonly DamageMeterHud Hud = new();
    private static ulong _lastError;
    internal static void Tick(JournalSession session, bool activeRun, bool inCombatRoom, bool mapVisible, bool uiBlocking)
    {
        try
        {
            var scope = DamageMeterContext.Select(mapVisible, inCombatRoom);
            Hud.Render(session, scope, HudVisibilityPolicy.ShowMeter(ModSettings.ShowDamageMeter,
                activeRun, ModSettings.DamageMeterOnlyCombat, scope == DamageMeterScope.Combat, uiBlocking));
        }
        catch (Exception ex)
        {
            Hud.Hide(); // Never leave a stale or partially updated ranking visible.
            Hud.Invalidate();
            ulong now = Time.GetTicksMsec();
            if (_lastError == 0 || now - _lastError >= 10000)
            { _lastError = now; ModLog.Error("DamageMeter.Render", ex); }
        }
    }
    internal static void Toggle()
    {
        ModSettings.ShowDamageMeter = !ModSettings.ShowDamageMeter;
        ModSettings.Save();
        if (!ModSettings.ShowDamageMeter) Hud.Hide();
        Refresh();
    }
    internal static void Refresh()
    {
        if (!ModSettings.ShowDamageMeter) Hud.Hide();
        Hud.Invalidate(); ModRuntime.EnsureStarted();
    }
    internal static void ResetLayout() { Hud.ResetLayout(); Refresh(); }
    internal static void CancelPointerInteraction() => Hud.CancelPointerInteraction();
    internal static void Stop() => Hud.Stop();
    // Visual containment is deliberately independent from the drag/lock hit test.
    internal static bool IsPointInVisibleArea(Vector2 point) => Hud.IsPointInVisibleArea(point);
    internal static bool IsPointInInteractionArea(Vector2 point) => Hud.IsPointInInteractionArea(point);
    internal static bool HandleInput(InputEvent input) => Hud.HandleInput(input);
    internal static void ResetPointerCursor() => Hud.ResetPointerCursor();
    internal static void Hide() => Hud.Hide();
}
