#nullable enable
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using System;
using System.Collections.Generic;

namespace BetterSpire2.UI;

public static partial class SettingsMenu
{
    private static readonly Color PanelBackgroundColor = HudTheme.Background;
    private static readonly Color SurfaceColor = HudTheme.Surface;
    private static readonly Color RaisedSurfaceColor = HudTheme.Raised;
    private static readonly Color PanelBorderColor = HudTheme.AccentBorder;
    private static readonly Color SubtleBorderColor = HudTheme.Border;
    private static readonly Color AccentColor = HudTheme.Accent;
    private static readonly Color SecondaryTextColor = HudTheme.Text;
    private static readonly Color MutedTextColor = HudTheme.Muted;
    private static readonly Color DangerColor = HudTheme.Danger;
    private static readonly Color SuccessColor = HudTheme.Success;

    private static readonly Dictionary<ModSettingSectionId, Button> _tabButtons = new();

    private static PanelContainer? _panel;
    private static CanvasLayer? _canvasLayer;
    private static Control? _dragHandle;
    private static VBoxContainer? _contentRoot;
    private static Label? _statusLabel;
    private static Viewport? _viewport;
    private static VBoxContainer? _outerRoot;
    private static ScrollContainer? _settingsScroll;
    private static DeferredHudLayout? _autoFit;
    private static Vector2 _settingsAnchor = new(.5f, .5f);
    private static DockableOverlayController? _layoutController;
    private static ModSettingSectionId _activeSection = ModSettingSectionId.Combat;
    private static bool _isVisible;
    internal static bool IsVisible => _isVisible;

    public static void Toggle()
    {
        if (_isVisible)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    public static bool HandleMouseInput(InputEvent inputEvent)
    {
        if (!_isVisible) return false;
        bool active = _layoutController?.IsInteracting == true;
        _layoutController?.HandleInput(inputEvent);
        return active || _layoutController?.IsInteracting == true;
    }
    internal static void ResetPointerCursor() => _layoutController?.ResetPointerCursor();

    internal static void HandleOutsideClick(Vector2 point)
    {
        if (_isVisible && !IsPointInPanel(point))
        {
            Hide();
        }
    }

    public static bool IsPointInPanel(Vector2 point)
    {
        return _isVisible && UiHelpers.IsValid(_panel) && _panel!.GetGlobalRect().HasPoint(point);
    }

    public static void Hide()
    {
        _autoFit?.Dispose(); _autoFit = null;
        _layoutController?.Persist();
        _layoutController?.Dispose();
        _layoutController = null;

        if (UiHelpers.IsValid(_canvasLayer))
        {
            _canvasLayer!.QueueFree();
        }

        _canvasLayer = null;
        _panel = null;
        _dragHandle = null;
        _contentRoot = null;
        _statusLabel = null;
        _viewport = null;
        _outerRoot = null; _settingsScroll = null;
        _tabButtons.Clear();
        _settingBindings.Clear();
        _partyRoot = null; _partyStamp = "";
        _isVisible = false;
    }

    internal static void RefreshIntents()
    {
        try
        {
            NCombatRoom? instance = NCombatRoom.Instance;
            if (instance == null)
            {
                return;
            }

            foreach (NCreature creature in instance.CreatureNodes)
                if (UiHelpers.IsValid(creature)) creature.RefreshIntents();
        }
        catch (Exception ex)
        {
            ModLog.Error("SettingsMenu.RefreshIntents", ex);
        }
    }

    private static void ApplyButtonStyle(Button button, bool selected = false, bool danger = false)
        => HudTheme.StyleButton(button, selected, danger);

    private static void ShowStatus(string text, bool success = true)
    {
        if (!UiHelpers.IsValid(_statusLabel))
        {
            return;
        }

        _statusLabel!.Text = text;
        _statusLabel.Visible = text.Length > 0;
        _statusLabel.AddThemeColorOverride("font_color", success ? SuccessColor : DangerColor);
        _autoFit?.Request();
    }
    internal static void CancelPointerInteraction()
    {
        if (_layoutController?.IsInteracting == true) _layoutController.Persist();
        _layoutController?.CancelInteraction();
    }

}
