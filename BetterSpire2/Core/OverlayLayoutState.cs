#nullable enable
using Godot;

namespace BetterSpire2.Core;

/// <summary>
/// Saved geometry for an overlay. Version 2 stores its position in normalized
/// viewport space so it survives resolution and aspect-ratio changes.
/// </summary>
public readonly struct OverlayLayoutState
{
    public OverlayLayoutState(Vector2 position, bool hasPosition, bool positionIsNormalized, Vector2 size, bool hasSize)
    {
        Position = position;
        HasPosition = hasPosition;
        PositionIsNormalized = positionIsNormalized;
        Size = size;
        HasSize = hasSize;
    }

    public Vector2 Position { get; }
    public bool HasPosition { get; }
    public bool PositionIsNormalized { get; }
    public Vector2 Size { get; }
    public bool HasSize { get; }

    public static OverlayLayoutState Empty => new(Vector2.Zero, false, true, Vector2.Zero, false);

    public static OverlayLayoutState FromNormalized(Vector2 normalizedPosition, Vector2 size)
    {
        return new OverlayLayoutState(normalizedPosition, true, true, size, true);
    }

    public static OverlayLayoutState FromLegacy(Vector2? position, Vector2? size)
    {
        return new OverlayLayoutState(
            position ?? Vector2.Zero,
            position.HasValue,
            false,
            size ?? Vector2.Zero,
            size.HasValue);
    }
}
