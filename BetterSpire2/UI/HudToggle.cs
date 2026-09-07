#nullable enable
using Godot;

namespace BetterSpire2.UI;

/// <summary>
/// A fixed-size switch composed from ordinary Godot controls. Uses BaseButton's native
/// mouse/keyboard toggle semantics, not CheckButton icons inherited from the game's theme.
/// Child visuals ignore input; the whole 132 x 32 slot is the accessible click target.
/// </summary>
internal sealed class HudToggle
{
    internal Button Button { get; }
    private readonly Label _state;
    private readonly Panel _knob;
    private readonly StyleBoxFlat _trackStyle, _knobStyle;

    internal HudToggle()
    {
        Button = new Button
        {
            ToggleMode = true,
            CustomMinimumSize = new Vector2(CompactHudGeometry.SettingControlWidth, CompactHudGeometry.SettingControlHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
        };
        Button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        Button.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        Button.AddThemeStyleboxOverride("disabled", new StyleBoxEmpty());
        var hover = UiHelpers.CreatePanelStyle(new Color(HudTheme.Accent, .08f), Colors.Transparent, 0, 5, 0);
        Button.AddThemeStyleboxOverride("hover", hover);
        Button.AddThemeStyleboxOverride("hover_pressed", hover);
        Button.AddThemeStyleboxOverride("focus", UiHelpers.CreatePanelStyle(Colors.Transparent, HudTheme.Accent, 1, 5, 0));
        Button.FocusMode = Control.FocusModeEnum.All;
        Button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        _state = UiHelpers.CreateLabel("", HudTheme.Text, 12, HorizontalAlignment.Right);
        _state.Position = new Vector2(0, 0);
        _state.Size = new Vector2(82, CompactHudGeometry.SettingControlHeight);
        _state.VerticalAlignment = VerticalAlignment.Center;
        _state.ClipText = true;
        Button.AddChild(_state);
        var track = new Panel
        {
            Position = new Vector2(92, 6), Size = new Vector2(36, 20),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _trackStyle = UiHelpers.CreatePanelStyle(HudTheme.Raised, HudTheme.Border, 1, 10, 0);
        track.AddThemeStyleboxOverride("panel", _trackStyle);
        Button.AddChild(track);
        _knob = new Panel { Size = new Vector2(14, 14), MouseFilter = Control.MouseFilterEnum.Ignore };
        _knobStyle = UiHelpers.CreatePanelStyle(HudTheme.Muted, Colors.Transparent, 0, 7, 0);
        _knob.AddThemeStyleboxOverride("panel", _knobStyle);
        track.AddChild(_knob);
        SetState(false, true, "");
    }

    internal void SetState(bool value, bool enabled, string tooltip)
    {
        Button.Disabled = !enabled;
        Button.SetPressedNoSignal(value);
        Button.TooltipText = tooltip;
        _state.Text = ModText.T(value ? "On" : "Off");
        _state.AddThemeColorOverride("font_color", enabled ? HudTheme.Text : HudTheme.Disabled);
        _trackStyle.BgColor = value ? new Color(HudTheme.Accent, enabled ? .65f : .22f) : HudTheme.Raised;
        _trackStyle.BorderColor = value && enabled ? HudTheme.Accent : HudTheme.Border;
        _knobStyle.BgColor = enabled ? (value ? HudTheme.Text : HudTheme.Muted) : HudTheme.Disabled;
        _knob.Position = new Vector2(value ? 19 : 3, 3); // Round 14 px thumb, 3 px inset at both ends.
        Button.MouseDefaultCursorShape = enabled ? Control.CursorShape.PointingHand : Control.CursorShape.Arrow;
    }
}
