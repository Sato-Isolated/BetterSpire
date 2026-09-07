#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace BetterSpire2.HandViewer;

public static partial class TeammateHandViewer
{
    private static CanvasLayer? _canvasLayer;
    private static Control? _panel;
    private static VBoxContainer? _body, _handRoot;
    private static ScrollContainer? _scroll;
    private static Label? _title;
    private static Button? _previous, _next;
    private static DockableOverlayController? _layoutController;
    private static DeferredHudLayout? _autoFit;
    private static readonly List<Control> _interactiveControls = new();
    private static Vector2 _handAnchor;
    private static Vector2 MinimumSize => new(1, 1);
    private static Vector2 MaximumSize => new(0, 0); // Viewport bounds; content determines the size.

    private static void BuildUI()
    {
        var game = NGame.Instance;
        if (!UiHelpers.IsValid(game) || !game!.IsInsideTree()) return;
        _canvasLayer = UiHelpers.CreateCanvasLayer(HudLayers.HandViewer);
        // A plain Control: no opaque panel, border, padded title bar or resize frame.
        _panel = new Control
        {
            Name = "BetterSpireHandViewer", MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0)
        };
        _handRoot = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _handRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _handRoot.AddThemeConstantOverride("separation", CompactHudGeometry.HandBodyGap);
        _panel.AddChild(_handRoot);
        var titleBar = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0, CompactHudGeometry.HandHeaderHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        titleBar.AddThemeConstantOverride("separation", 3);
        _handRoot.AddChild(titleBar);
        _title = UiHelpers.CreateLabel("", HudTheme.Text, 12);
        _title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _title.VerticalAlignment = VerticalAlignment.Center;
        _title.ClipText = true;
        _title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _title.MouseFilter = Control.MouseFilterEnum.Stop;
        HudTheme.Outline(_title);
        titleBar.AddChild(_title);
        _previous = HeaderButton("‹", ModText.T("Previous teammate"));
        _previous.Pressed += PrevPage; titleBar.AddChild(_previous);
        _next = HeaderButton("›", ModText.T("Next teammate"));
        _next.Pressed += NextPage; titleBar.AddChild(_next);
        var close = HeaderButton("×", ModText.T("Close"));
        close.Pressed += Hide; titleBar.AddChild(close);
        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Pass,
            FollowFocus = true
        };
        _scroll.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        HudTheme.StyleScrollBar(_scroll.GetHScrollBar(), horizontal: true);
        _handRoot.AddChild(_scroll);
        _body = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 3);
        _scroll.AddChild(_body);
        _canvasLayer.AddChild(_panel); game.AddChild(_canvasLayer);
        RebuildContent();
        _layoutController = new DockableOverlayController(_panel, _title, game.GetViewport(), game,
            MinimumSize, MaximumSize, false, LoadAutoHandLayout, SaveHandLayout,
            IsPointInInteractiveControl, handleOnly: true);
        _layoutController.Initialize(MeasureHandSize(), DefaultPosition);
        var saved = ModSettings.GetHandViewerLayout();
        _handAnchor = saved.HasPosition && saved.PositionIsNormalized
            ? saved.Position : CurrentHandAnchor();
        _autoFit = new DeferredHudLayout(_panel, FitHandToContent);
        _autoFit.Watch(_body);
        _autoFit.Watch(_handRoot);
        _autoFit.Request();
    }

    private static Vector2 DefaultPosition(Rect2 safe, Vector2 size) =>
        new(safe.Position.X, Math.Min(safe.End.Y - size.Y, safe.Position.Y + 120));

    private static void RebuildContent()
    {
        if (!UiHelpers.IsValid(_body)) return;
        ClearSections();
        foreach (var child in _body!.GetChildren()) { _body.RemoveChild(child); child.QueueFree(); }
        _french = ModText.IsFrench;
        _previous!.Visible = _next!.Visible = _players.Count > 1;
        _previous.Disabled = _next.Disabled = _players.Count < 2;
        _previous.TooltipText = ModText.T("Previous teammate");
        _next.TooltipText = ModText.T("Next teammate");
        _title!.Text = "";
        if (_players.Count > 0)
        {
            var section = new PlayerHandSection(_players[_currentPage], _localNetId);
            _sections.Add(section);
            section.AddTo(_body);
        }
        _scroll!.ScrollHorizontal = 0;
        _autoFit?.Request();
        SyncContextVisibility();
    }

    private static Button HeaderButton(string text, string tooltip)
    {
        var button = new Button
        {
            Text = text, TooltipText = tooltip, CustomMinimumSize = new Vector2(18, 20),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
        };
        button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        button.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        button.AddThemeStyleboxOverride("disabled", new StyleBoxEmpty());
        var hover = UiHelpers.CreatePanelStyle(new Color(HudTheme.Background, .4f), Colors.Transparent, 0, 3, 0);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("hover_pressed", hover);
        button.AddThemeStyleboxOverride("focus", UiHelpers.CreatePanelStyle(Colors.Transparent, HudTheme.Accent, 1, 3, 0));
        button.AddThemeColorOverride("font_color", HudTheme.Muted);
        button.AddThemeColorOverride("font_hover_color", HudTheme.Accent);
        button.AddThemeColorOverride("font_pressed_color", HudTheme.Accent);
        button.AddThemeColorOverride("font_outline_color", HudTheme.Shadow);
        button.AddThemeConstantOverride("outline_size", 3);
        button.AddThemeFontSizeOverride("font_size", 14);
        button.FocusMode = Control.FocusModeEnum.All;
        button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        _interactiveControls.Add(button);
        return button;
    }
}
