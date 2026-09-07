#nullable enable
using System;

namespace BetterSpire2.UI;

/// <summary>Pure layout rules, shared by the Godot UI and the C# regression suite.</summary>
internal static class CompactHudGeometry
{
    internal const int CardWidth = 42;        // 3.5.2: 84 px.
    internal const int CardGap = 4;
    internal const int HandHeaderHeight = 20;
    internal const int HandBodyGap = 3;
    internal const int ScrollbarWidth = 8;
    internal const int SettingsScrollGutter = 14;
    internal const int SettingControlWidth = 132;
    internal const int SettingControlHeight = 32;

    internal static float Available(float extent, float margin)
    {
        extent = float.IsFinite(extent) && extent > 0 ? extent : 1;
        return Math.Max(1, extent - Math.Min(margin, extent * .25f) * 2);
    }

    internal static float SettingsWidth(float viewportWidth)
    {
        if (!float.IsFinite(viewportWidth) || viewportWidth <= 0) viewportWidth = 1;
        return Math.Min(Available(viewportWidth, 24), Math.Clamp(viewportWidth * .46f, 520, 720));
    }

    internal static float SettingsHeight(float viewportHeight, float chromeHeight, float contentHeight)
    {
        float maximum = Math.Min(760, Available(viewportHeight, 24));
        float requested = Math.Max(240, Positive(chromeHeight) + Positive(contentHeight));
        return Math.Min(maximum, requested);
    }

    internal static int ScaledCardWidth(int percent) =>
        Math.Max(1, (int)Math.Round(CardWidth * Math.Clamp(percent, 50, 200) / 100f));
    // Square thumbnails share the same dimension and rounding at every scale.
    internal static int ScaledPortraitHeight(int percent) =>
        ScaledCardWidth(percent);

    internal static (float Width, float Height, bool Overflow) HandStrip(int count, int scalePercent, float viewportWidth)
    {
        int width = ScaledCardWidth(scalePercent), height = ScaledPortraitHeight(scalePercent);
        double natural = count <= 0 ? 0 : (double)count * width + (double)(count - 1) * CardGap;
        float available = Math.Min(420, Available(viewportWidth, 16));
        float wanted = (float)Math.Max(154, Math.Min(float.MaxValue, natural));
        float fitted = Math.Min(available, wanted);
        bool overflow = natural > fitted + .5;
        return (fitted, HandHeaderHeight + HandBodyGap + (count == 0 ? 18 : height)
            + (overflow ? ScrollbarWidth + 2 : 0), overflow);
    }
    private static float Positive(float value) => float.IsFinite(value) ? Math.Max(0, value) : 0;
}
