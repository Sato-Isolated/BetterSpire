#nullable enable
using System;
using BetterSpire2.DamageMeter.UI;
using BetterSpire2.DamageMeter.Core;
using BetterSpire2.Journal.Game;
using Godot;

namespace BetterSpire2.DamageMeter;

internal static class DamageMeterController
{
    private static readonly DamageMeterHud Hud = new();
    /// <summary>Runs after JournalService.Tick; a missing frame means the journal failed or the run ended.</summary>
    internal static void Tick()
    {
        try
        {
            if (JournalService.LastFrame is not { } frame) { Hud.Hide(); return; }
            var scope = DamageMeterContext.Select(frame.MapVisible, frame.InCombatRoom, ModSettings.DamageMeterCurrentRound);
            Hud.Render(JournalService.Session, scope, HudVisibilityPolicy.ShowMeter(ModSettings.ShowDamageMeter,
                frame.ActiveRun, ModSettings.DamageMeterOnlyCombat, scope != DamageMeterScope.Run, frame.UiBlocking));
        }
        catch (Exception ex)
        {
            Hud.Hide(); // Never leave a stale or partially updated ranking visible.
            Hud.Invalidate();
            ModLog.ErrorThrottled("DamageMeter.Render", ex);
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
