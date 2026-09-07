#nullable enable
using System;

namespace BetterSpire2.UI;

/// <summary>Engine-independent geometry used by every dockable overlay.</summary>
internal static class OverlayAxis
{
    internal static float Unit(float value) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
    internal static float Normalize(float position, float size, float start, float extent)
    {
        float travel = Math.Max(0f, extent - size);
        return travel <= .001f ? 0f : Unit((position - start) / travel);
    }
    internal static float Position(float normalized, float size, float start, float extent) =>
        start + Unit(normalized) * Math.Max(0f, extent - size);
    internal static float Clamp(float position, float size, float start, float extent, float snapDistance, bool snap)
    {
        float end = Math.Max(start, start + extent - size);
        float value = Math.Clamp(float.IsFinite(position) ? position : start, start, end);
        if (snap)
        {
            if (Math.Abs(value - start) <= snapDistance) value = start;
            if (Math.Abs(value - end) <= snapDistance) value = end;
        }
        return value;
    }
}
