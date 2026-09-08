#nullable enable
using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace BetterSpire2.HandViewer;

public static partial class TeammateHandViewer
{
    private static Panel? _cardTooltip;
    private static Label? _cardTooltipText;

    // Use our own overlay: native Control.TooltipText depends on Godot's tooltip
    // timer and GUI hover handling. Polling also works with a stationary pointer
    // when a hand changes or another overlay opens.
    private static void RefreshCardTooltip()
    {
        if (!IsVisible || SettingsMenu.IsVisible || JournalService.GameUiBlocking
            || NGame.Instance?.FeedbackScreen?.Visible == true
            || _layoutController?.IsInteracting == true
            || !_panel!.GetWindow().HasFocus()
            || Input.IsMouseButtonPressed(MouseButton.Left))
        { HideCardTooltip(); return; }

        Vector2 point = _panel.GetGlobalMousePosition();
        if (!Hits(_scroll, point) || Hits(_scroll!.GetHScrollBar(), point))
        { HideCardTooltip(); return; }
        foreach (var section in _sections)
        {
            if (!section.TryGetCardTooltip(point, out string text, out Rect2 rect)) continue;
            ShowCardTooltip(text, rect);
            return;
        }
        HideCardTooltip();
    }

    private static void ShowCardTooltip(string text, Rect2 cardRect)
    {
        if (!UiHelpers.IsValid(_cardTooltip))
        {
            _cardTooltip = new Panel
            {
                Name = "BetterSpireCardTooltip", MouseFilter = Control.MouseFilterEnum.Ignore,
                ClipContents = true, Visible = false
            };
            _cardTooltip.AddThemeStyleboxOverride("panel",
                UiHelpers.CreatePanelStyle(HudTheme.Background, HudTheme.Border, 1, 5, 0));
            _cardTooltipText = UiHelpers.CreateLabel("", HudTheme.Text, 16);
            _cardTooltipText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _cardTooltipText.Position = new Vector2(10, 8);
            _cardTooltip.AddChild(_cardTooltipText);
            // Sibling of the auto-sized hand strip: never changes its geometry or
            // gets clipped by the card row's ScrollContainer.
            _canvasLayer!.AddChild(_cardTooltip);
        }

        Vector2 viewport = _panel!.GetViewport().GetVisibleRect().Size;
        float width = Math.Min(340, viewport.X);
        if (_cardTooltipText!.Text != text) _cardTooltipText.Text = text;
        _cardTooltipText.Size = new Vector2(Math.Max(1, width - 20), 0);
        float height = Math.Min(viewport.Y, _cardTooltipText.GetMinimumSize().Y + 16);
        _cardTooltip!.Size = new Vector2(width, height);
        var position = HandTooltipGeometry.Place(cardRect.Position.X, cardRect.Position.Y,
            cardRect.End.Y, width, height, viewport.X, viewport.Y);
        _cardTooltip.Position = new Vector2(position.X, position.Y);
        _cardTooltip.Visible = true;
    }

    private static void HideCardTooltip()
    {
        if (UiHelpers.IsValid(_cardTooltip)) _cardTooltip!.Visible = false;
    }
}
