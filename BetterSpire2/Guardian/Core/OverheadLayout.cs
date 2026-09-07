#nullable enable
using System;
using System.Collections.Generic;

namespace BetterSpire2.Guardian.Core;

/// <summary>Viewport-space rectangles, independent of Godot and testable without the game.</summary>
public readonly record struct HudRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public bool Intersects(HudRect b) => X < b.Right && Right > b.X && Y < b.Bottom && Bottom > b.Y;
}

public static class OverheadLayout
{
    /// <summary>
    /// Center just above the hitbox. Avoid native intents and earlier player labels.
    /// If no room remains above the character, hide rather than cover the character/cards.
    /// </summary>
    public static HudRect? Place(float headX, float headY, float width, float height,
        float offsetX, float gap, HudRect viewport, IReadOnlyList<HudRect> obstacles)
    {
        const float margin = 6f;
        if (!float.IsFinite(headX) || !float.IsFinite(headY) || !float.IsFinite(width) ||
            !float.IsFinite(height) || !float.IsFinite(viewport.X) || !float.IsFinite(viewport.Y) ||
            !float.IsFinite(viewport.Width) || !float.IsFinite(viewport.Height) ||
            !float.IsFinite(offsetX) || !float.IsFinite(gap) || width <= 0 || height <= 0 ||
            viewport.Width < width + 2 * margin || viewport.Height < height + 2 * margin)
            return null;
        // Do not leave a detached label at the edge for an off-screen creature.
        if (headX < viewport.X || headX > viewport.Right || headY < viewport.Y || headY > viewport.Bottom)
            return null;
        float x = Math.Clamp(headX + offsetX - width / 2f, viewport.X + margin, viewport.Right - width - margin);
        float y = Math.Min(headY - Math.Max(0, gap) - height, viewport.Bottom - height - margin);
        for (int pass = 0; pass <= obstacles.Count; pass++)
        {
            if (y < viewport.Y + margin) return null;
            var rect = new HudRect(x, y, width, height);
            bool moved = false;
            foreach (var obstacle in obstacles)
            {
                if (obstacle.Width <= 0 || obstacle.Height <= 0 || !rect.Intersects(obstacle)) continue;
                y = obstacle.Y - height - margin;
                moved = true;
                break;
            }
            if (!moved) return rect;
        }
        return null;
    }
}
