#nullable enable
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using System;

namespace BetterSpire2.UI;

public static class ClockDisplay
{
    private static CanvasLayer? _layer;
    private static PanelContainer? _panel;
    private static Label? _label;
    private static DockableOverlayController? _layoutController;
    private static ulong _lastRefreshAt;

    private static readonly Vector2 DefaultSize = new(76f, 28f);

    public static void Toggle(bool on)
    {
        if (on)
        {
            EnsureCreated();
            RefreshIfVisible();
        }
        else
        {
            Remove();
        }
    }

    public static void SyncVisibility()
    {
        if (ModSettings.ShowClock)
        {
            EnsureCreated();
            RefreshIfVisible();
        }
        else
        {
            Remove();
        }
    }

    public static void Update()
    {
        if (!ModSettings.ShowClock)
        {
            if (_layer != null || _layoutController != null) Remove();
            return;
        }

        EnsureCreated();
        ulong ticksMsec = Time.GetTicksMsec();
        if (ticksMsec - _lastRefreshAt < 1000)
        {
            return;
        }

        _lastRefreshAt = ticksMsec;
        RefreshIfVisible();
    }

    public static bool HandleInput(InputEvent inputEvent)
    {
        if (!ModSettings.ShowClock || !UiHelpers.IsValid(_panel)) return false;
        bool active = _layoutController?.IsInteracting == true;
        _layoutController?.HandleInput(inputEvent);
        return active || _layoutController?.IsInteracting == true;
    }

    internal static void Stop() => Remove();

    internal static bool IsPointInInteractionArea(Vector2 point)
    {
        return ModSettings.ShowClock && _layoutController?.IsPointInInteractionArea(point) == true;
    }

    internal static void ResetPointerCursor()
    {
        _layoutController?.ResetPointerCursor();
    }

    public static void ResetLayout()
    {
        if (_layoutController == null)
        {
            return;
        }

        _layoutController.ResetLayout(DefaultSize, static (safeRect, size) =>
            new Vector2(safeRect.End.X - size.X, safeRect.Position.Y + 132f));
    }

    public static void RefreshIfVisible()
    {
        if (UiHelpers.IsValid(_label))
        {
            string text = GetCurrentTimeText();
            if (_label!.Text != text) _label.Text = text;
        }
    }

    private static void EnsureCreated()
    {
        if (UiHelpers.IsValid(_panel) && UiHelpers.IsValid(_layer) && _layer!.GetParent() == NGame.Instance)
        {
            return;
        }

        if (_layer != null || _layoutController != null) Remove(persistLayout: false);
        NGame? game = NGame.Instance;
        Viewport? viewport = game?.GetViewport();
        if (game == null || !UiHelpers.IsValid(viewport))
        {
            return;
        }

        _layer = UiHelpers.CreateCanvasLayer(HudLayers.Clock);
        _panel = new PanelContainer
        {
            ClipContents = true,
            ZIndex = 210
        };
        _panel.AddThemeStyleboxOverride("panel", HudTheme.Panel(4, accented: false));

        _label = UiHelpers.CreateLabel(
            GetCurrentTimeText(),
            HudTheme.Text,
            12,
            HorizontalAlignment.Center);
        _label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _label.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _panel.AddChild(_label, forceReadableName: false, Node.InternalMode.Disabled);

        _layer.AddChild(_panel, forceReadableName: false, Node.InternalMode.Disabled);
        game.AddChild(_layer, forceReadableName: false, Node.InternalMode.Disabled);

        _layoutController = new DockableOverlayController(
            _panel,
            _label,
            viewport!,
            game,
            DefaultSize,
            DefaultSize,
            canResize: false,
            loadLayout: ModSettings.GetClockLayout,
            saveLayout: ModSettings.SaveClockLayout);
        _layoutController.Initialize(DefaultSize, static (safeRect, size) =>
            new Vector2(safeRect.End.X - size.X, safeRect.Position.Y + 132f));
    }

    private static void Remove(bool persistLayout = true)
    {
        if (persistLayout)
        {
            _layoutController?.Persist();
        }
        _layoutController?.Dispose();
        _layoutController = null;

        if (UiHelpers.IsValid(_layer))
        {
            _layer!.QueueFree();
        }

        _layer = null;
        _panel = null;
        _label = null;
    }

    private static string GetCurrentTimeText()
    {
        return ModSettings.Clock24Hour ? DateTime.Now.ToString("HH:mm") : DateTime.Now.ToString("h:mm tt");
    }
    internal static void CancelPointerInteraction()
    {
        if (_layoutController?.IsInteracting == true) _layoutController.Persist();
        _layoutController?.CancelInteraction();
    }

}
