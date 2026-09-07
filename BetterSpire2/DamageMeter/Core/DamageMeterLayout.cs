#nullable enable
using System;

namespace BetterSpire2.DamageMeter.Core;

public sealed record MeterPlacement(float X, float Y, float Scale, float Width, float Height);

/// <summary>Viewport-space layout, tested independently of Godot. Default stays in the upper-right margin.</summary>
public static class DamageMeterLayout
{
    public static MeterPlacement Place(float viewportWidth, float viewportHeight, int rows,
        bool multiplayer, bool footer, int scalePercent, int xPercent, int yPercent)
    {
        float width = multiplayer ? 276f : 216f;
        float height = 20 + Math.Clamp(rows, 1, 5) * (multiplayer ? 29 : 24) + (footer ? 20 : 0);
        if (!float.IsFinite(viewportWidth) || !float.IsFinite(viewportHeight) || viewportWidth <= 0 || viewportHeight <= 0)
            return new MeterPlacement(0, 0, 0, width, height);
        float margin = Math.Min(16, Math.Min(viewportWidth, viewportHeight) * .04f);
        float top = Math.Min(112, viewportHeight * .12f);
        float scale = Math.Min(Math.Clamp(scalePercent, 75, 150) / 100f,
            Math.Min((viewportWidth - 2 * margin) / width, (viewportHeight - top - margin) / height));
        scale = Math.Max(.001f, scale);
        float availableX = Math.Max(0, viewportWidth - margin * 2 - width * scale);
        float availableY = Math.Max(0, viewportHeight - top - margin - height * scale);
        return new MeterPlacement(margin + availableX * Math.Clamp(xPercent, 0, 100) / 100f,
            top + availableY * Math.Clamp(yPercent, 0, 100) / 100f, scale, width, height);
    }
}
