#nullable enable
using System;

namespace BetterSpire2.HandViewer;

internal static class HandTooltipGeometry
{
    internal static (float X, float Y) Place(float x, float top, float bottom,
        float width, float height, float viewportWidth, float viewportHeight)
    {
        float maxX = Math.Max(0, viewportWidth - width);
        float maxY = Math.Max(0, viewportHeight - height);
        float y = top - height - 8;
        if (y < 0) y = bottom + 8;
        return (Math.Clamp(x, 0, maxX), Math.Clamp(y, 0, maxY));
    }
}
