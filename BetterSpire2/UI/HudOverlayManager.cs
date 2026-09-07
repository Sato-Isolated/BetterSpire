#nullable enable
using Godot;
using BetterSpire2.DamageMeter;

namespace BetterSpire2.UI;

/// <summary>One captured drag at a time. Ordinary buttons and tooltips still receive Godot GUI input.</summary>
internal static class HudOverlayManager
{
    private enum PointerTarget { None, Settings, HandViewer, Clock, DamageMeter, Blocked }
    private static PointerTarget _capturedTarget, _hoveredTarget;

    internal static bool HandleInput(InputEvent input)
    {
        if (JournalService.GameUiBlocking && !SettingsMenu.IsVisible) { Reset(); return false; }
        if (input is InputEventMouseMotion motion)
        {
            if (_capturedTarget != PointerTarget.None)
            {
                bool handled = Route(_capturedTarget, motion);
                if ((motion.ButtonMask & MouseButtonMask.Left) == 0 || !handled) _capturedTarget = PointerTarget.None;
                return handled;
            }
            var hovered = ResolveTarget(motion.Position);
            if (hovered != _hoveredTarget) { ResetCursorsExcept(hovered); _hoveredTarget = hovered; }
            return Route(hovered, motion);
        }
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } button) return false;
        if (!button.Pressed)
        {
            var target = _capturedTarget != PointerTarget.None ? _capturedTarget : ResolveTarget(button.Position);
            _capturedTarget = PointerTarget.None;
            return Route(target, button);
        }
        var hit = ResolveTarget(button.Position);
        if (hit != PointerTarget.Settings) SettingsMenu.HandleOutsideClick(button.Position);
        // The hand viewer stays open; background clicks continue to the game.
        ResetCursorsExcept(hit);
        bool captured = Route(hit, button);
        _capturedTarget = captured ? hit : PointerTarget.None;
        return captured;
    }
    private static PointerTarget ResolveTarget(Vector2 pointer)
    {
        // Same order as HudLayers. A journal button must never drag the meter behind it.
        if (SettingsMenu.IsPointInPanel(pointer)) return PointerTarget.Settings;
        if (JournalService.GameUiBlocking) return PointerTarget.Blocked;
        if (TeammateHandViewer.IsPointInInteractionArea(pointer)) return PointerTarget.HandViewer;
        if (ClockDisplay.IsPointInInteractionArea(pointer)) return PointerTarget.Clock;
        if (JournalController.IsPointInWindow(pointer)) return PointerTarget.Blocked;
        if (DamageMeterController.IsPointInInteractionArea(pointer)) return PointerTarget.DamageMeter;
        return PointerTarget.None;
    }
    private static bool Route(PointerTarget target, InputEvent input) => target switch
    {
        PointerTarget.Settings => SettingsMenu.HandleMouseInput(input),
        PointerTarget.HandViewer => TeammateHandViewer.HandleMouseInput(input),
        PointerTarget.Clock => ClockDisplay.HandleInput(input),
        PointerTarget.DamageMeter => DamageMeterController.HandleInput(input),
        _ => false
    };
    private static void ResetCursorsExcept(PointerTarget target)
    {
        if (target != PointerTarget.Settings) SettingsMenu.ResetPointerCursor();
        if (target != PointerTarget.HandViewer) TeammateHandViewer.ResetPointerCursor();
        if (target != PointerTarget.Clock) ClockDisplay.ResetPointerCursor();
        if (target != PointerTarget.DamageMeter) DamageMeterController.ResetPointerCursor();
    }
    internal static void Reset()
    {
        SettingsMenu.CancelPointerInteraction();
        TeammateHandViewer.CancelPointerInteraction();
        ClockDisplay.CancelPointerInteraction();
        DamageMeterController.CancelPointerInteraction();
        _capturedTarget = _hoveredTarget = PointerTarget.None;
        ResetCursorsExcept(PointerTarget.None);
    }
}
