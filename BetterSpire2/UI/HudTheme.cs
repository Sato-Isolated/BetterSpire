#nullable enable
using Godot;

namespace BetterSpire2.UI;

/// <summary>Shared visual tokens and control states. No game-specific calculations belong here.</summary>
internal static class HudTheme
{
    internal static readonly Color Background = new(.035f, .045f, .07f, .97f);
    internal static readonly Color Surface = new(.055f, .07f, .105f, .98f);
    internal static readonly Color Raised = new(.075f, .09f, .13f, .98f);
    internal static readonly Color Border = new(.24f, .27f, .34f, .9f);
    internal static readonly Color AccentBorder = new(.56f, .43f, .19f, .9f);
    internal static readonly Color Accent = new(.86f, .69f, .34f);
    internal static readonly Color Text = new(.9f, .91f, .93f);
    internal static readonly Color Muted = new(.65f, .68f, .74f);
    internal static readonly Color Disabled = new(.43f, .46f, .53f);
    internal static readonly Color Danger = new(.91f, .42f, .38f);
    internal static readonly Color Success = new(.48f, .79f, .58f);
    internal static readonly Color Info = new(.45f, .67f, .94f);
    internal static readonly Color Shadow = new(0, 0, 0, .94f);
    internal const int Radius = 7;
    internal const int Spacing = 8;

    internal static StyleBoxFlat Panel(float padding = 8, bool accented = true) =>
        UiHelpers.CreatePanelStyle(Background, accented ? AccentBorder : Border, 1, Radius, padding);

    internal static void StyleButton(Button button, bool selected = false, bool danger = false, int fontSize = 13, float padding = 6)
    {
        Color accent = danger ? Danger : Accent;
        button.AddThemeStyleboxOverride("normal", UiHelpers.CreatePanelStyle(selected ? new Color(accent, .2f) : Raised,
            selected ? accent : Border, 1, 5, padding));
        button.AddThemeStyleboxOverride("hover", UiHelpers.CreatePanelStyle(new Color(accent, .26f), accent, 1, 5, padding));
        button.AddThemeStyleboxOverride("pressed", UiHelpers.CreatePanelStyle(new Color(accent, .34f), accent, 1, 5, padding));
        button.AddThemeStyleboxOverride("disabled", UiHelpers.CreatePanelStyle(Surface, Border, 1, 5, padding));
        button.AddThemeStyleboxOverride("focus", UiHelpers.CreatePanelStyle(Colors.Transparent, accent, 2, 5, padding));
        button.AddThemeColorOverride("font_color", selected ? accent : Text);
        button.AddThemeColorOverride("font_hover_color", Text);
        button.AddThemeColorOverride("font_pressed_color", Text);
        button.AddThemeColorOverride("font_disabled_color", Disabled);
        button.AddThemeFontSizeOverride("font_size", fontSize);
        button.FocusMode = Control.FocusModeEnum.All;
    }

    private static ImageTexture? _emptyScrollbarIcon;
    internal static void StyleScrollBar(ScrollBar bar, bool horizontal = false)
    {
        int width = CompactHudGeometry.ScrollbarWidth;
        var track = UiHelpers.CreatePanelStyle(new Color(Muted, .09f), Colors.Transparent, 0, 4, 0);
        var grabber = UiHelpers.CreatePanelStyle(new Color(Muted, .55f), Colors.Transparent, 0, 4, 0);
        // ScrollBar uses style minimum sizes to determine its thickness.
        track.ContentMarginLeft = track.ContentMarginRight = horizontal ? 0 : width * .5f;
        track.ContentMarginTop = track.ContentMarginBottom = horizontal ? width * .5f : 0;
        grabber.ContentMarginLeft = grabber.ContentMarginRight = horizontal ? 8 : width * .5f;
        grabber.ContentMarginTop = grabber.ContentMarginBottom = horizontal ? width * .5f : 8;
        bar.AddThemeStyleboxOverride("scroll", track);
        bar.AddThemeStyleboxOverride("scroll_focus", track);
        bar.AddThemeStyleboxOverride("grabber", grabber);
        var hover = (StyleBoxFlat)grabber.Duplicate(false); hover.BgColor = new Color(Accent, .7f);
        bar.AddThemeStyleboxOverride("grabber_highlight", hover);
        var pressed = (StyleBoxFlat)grabber.Duplicate(false); pressed.BgColor = Accent;
        bar.AddThemeStyleboxOverride("grabber_pressed", pressed);
        if (_emptyScrollbarIcon == null)
        {
            using var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
            image.Fill(Colors.Transparent);
            _emptyScrollbarIcon = ImageTexture.CreateFromImage(image);
        }
        foreach (string icon in new[] { "increment", "increment_highlight", "increment_pressed", "decrement", "decrement_highlight", "decrement_pressed" })
            bar.AddThemeIconOverride(icon, _emptyScrollbarIcon!);
        bar.CustomMinimumSize = horizontal ? new Vector2(0, width) : new Vector2(width, 0);
    }

    internal static void Outline(Label label)
    {
        label.AddThemeColorOverride("font_outline_color", Shadow);
        label.AddThemeConstantOverride("outline_size", 4);
    }
}

/// <summary>Input routing must follow these same visual layers.</summary>
internal static class HudLayers
{
    internal const int Guardian = 15;
    internal const int DamageMeter = 21;
    internal const int Journal = 22;
    internal const int Clock = 25;
    internal const int HandViewer = 30;
    internal const int Settings = 40;
}
