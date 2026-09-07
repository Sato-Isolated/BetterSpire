#nullable enable
using Godot;

namespace BetterSpire2.HandViewer;

public static partial class TeammateHandViewer
{
    private static bool Hits(Control? control, Vector2 point) => UiHelpers.IsValid(control)
        && control!.IsVisibleInTree() && control.GetGlobalRect().HasPoint(point);
    private static bool IsPointInInteractiveControl(Vector2 point)
    {
        foreach (var control in _interactiveControls) if (Hits(control, point)) return true;
        if (!Hits(_scroll, point)) return false;
        if (Hits(_scroll!.GetHScrollBar(), point)) return true;
        foreach (var section in _sections) if (section.ContainsVisiblePoint(point)) return true;
        return false;
    }
    // No giant invisible hit box: only the title, buttons, visible icons and overflow scrollbar.
    public static bool IsPointInPanel(Vector2 point) => IsPointInInteractionArea(point);
    internal static bool IsPointInInteractionArea(Vector2 point) => IsVisible &&
        (Hits(_title, point) || IsPointInInteractiveControl(point));
    internal static void ResetPointerCursor() => _layoutController?.ResetPointerCursor();
    public static bool HandleMouseInput(InputEvent input)
    {
        if (!IsVisible) return false;
        bool active = _layoutController?.IsInteracting == true;
        _layoutController?.HandleInput(input);
        if (active && _layoutController?.IsInteracting != true) _autoFit?.Request();
        return active || _layoutController?.IsInteracting == true;
    }
    internal static void CancelPointerInteraction()
    {
        if (_layoutController?.IsInteracting == true) _layoutController.Persist();
        _layoutController?.CancelInteraction();
    }
}
