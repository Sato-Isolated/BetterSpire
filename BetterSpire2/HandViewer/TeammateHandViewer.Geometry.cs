#nullable enable
using System;
using Godot;

namespace BetterSpire2.HandViewer;

public static partial class TeammateHandViewer
{
    private static Vector2 MeasureHandSize()
    {
        Vector2 viewport = _panel?.GetViewport().GetVisibleRect().Size ?? new Vector2(1920, 1080);
        int count = _players.Count == 0 ? 0 : _players[_currentPage].PlayerCombatState?.Hand?.Cards.Count ?? 0;
        var strip = CompactHudGeometry.HandStrip(count, ModSettings.CardScalePercent, viewport.X);
        if (!ModSettings.CompactHandViewer)
        {
            // Fonts inherited from the game can have different ascent/outline metrics.
            float minimum = _handRoot?.GetCombinedMinimumSize().Y ?? 0;
            return new Vector2(strip.Width, Math.Min(CompactHudGeometry.Available(viewport.Y, 16),
                Math.Max(strip.Height, minimum)));
        }
        float width = Math.Min(320, CompactHudGeometry.Available(viewport.X, 16));
        float bodyHeight = _body?.GetCombinedMinimumSize().Y ?? 24;
        return new Vector2(width, Math.Min(CompactHudGeometry.Available(viewport.Y, 16),
            CompactHudGeometry.HandHeaderHeight + CompactHudGeometry.HandBodyGap + bodyHeight));
    }
    private static void FitHandToContent()
    {
        if (!UiHelpers.IsValid(_panel) || _layoutController == null || _layoutController.IsInteracting) return;
        Vector2 requested = MeasureHandSize();
        if (!_panel!.Size.IsEqualApprox(requested))
            _layoutController.ApplyGeometry(MinimumSize, MaximumSize, requested, preserveRelativePosition: false);
        _layoutController.SetNormalizedPosition(_handAnchor);
    }
    private static OverlayLayoutState LoadAutoHandLayout()
    {
        var old = ModSettings.GetHandViewerLayout();
        // Preserve the position. A saved 640 x 330 panel must not enlarge the new icon strip.
        return new OverlayLayoutState(old.Position, old.HasPosition, old.PositionIsNormalized, Vector2.Zero, false);
    }
    private static void SaveHandLayout(OverlayLayoutState layout)
    {
        if (layout.HasPosition && layout.PositionIsNormalized) _handAnchor = layout.Position;
        ModSettings.SaveHandViewerLayout(layout);
    }
    private static Vector2 CurrentHandAnchor()
    {
        if (!UiHelpers.IsValid(_panel)) return Vector2.Zero;
        Vector2 viewport = _panel!.GetViewport().GetVisibleRect().Size;
        float mx = Math.Min(16, viewport.X * .25f), my = Math.Min(16, viewport.Y * .25f);
        return new Vector2(OverlayAxis.Normalize(_panel.Position.X, _panel.Size.X, mx, CompactHudGeometry.Available(viewport.X, 16)),
            OverlayAxis.Normalize(_panel.Position.Y, _panel.Size.Y, my, CompactHudGeometry.Available(viewport.Y, 16)));
    }
    public static void ResetLayout()
    {
        _layoutController?.ResetLayout(MeasureHandSize(), DefaultPosition);
        _handAnchor = CurrentHandAnchor();
        _autoFit?.Request();
    }
}
