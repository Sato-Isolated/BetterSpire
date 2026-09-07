#nullable enable
using System;
using BetterSpire2.Guardian.Core;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace BetterSpire2.Guardian.UI;

internal static class OverheadAnchor
{
    internal static bool TryHead(NCreature creature, Viewport viewport, out Vector2 head)
    {
        head = default;
        if (!Valid(creature) || !creature.IsInsideTree() || !creature.IsVisibleInTree() ||
            creature.Entity == null || creature.Entity.IsDead || creature.Modulate.A <= .01f ||
            creature.GetViewport() != viewport || !TryRect(creature.Hitbox, out var bounds)) return false;
        head = new Vector2(bounds.X + bounds.Width / 2f, bounds.Y);
        return true;
    }

    internal static bool TryIntentRect(NCreature creature, out HudRect rect)
    {
        rect = default;
        var intent = creature.IntentContainer;
        return Valid(intent) && intent.IsInsideTree() && intent.IsVisibleInTree() &&
            intent.Modulate.A > .01f && intent.GetChildCount() > 0 && TryRect(intent, out rect);
    }

    private static bool TryRect(Control? control, out HudRect rect)
    {
        rect = default;
        if (!Valid(control) || !control!.IsInsideTree() || control.Size.X <= 0 || control.Size.Y <= 0) return false;
        // A CanvasLayer is not the creature's parent coordinate space. Map to viewport space,
        // including canvas zoom/translation and mirrored/scaled character layouts.
        var transform = control.GetGlobalTransformWithCanvas();
        Vector2 a = transform * Vector2.Zero;
        Vector2 b = transform * new Vector2(control.Size.X, 0);
        Vector2 c = transform * control.Size;
        Vector2 d = transform * new Vector2(0, control.Size.Y);
        float left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        float right = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        float top = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        float bottom = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        rect = new HudRect(left, top, right - left, bottom - top);
        return float.IsFinite(left) && float.IsFinite(top) && float.IsFinite(right) && float.IsFinite(bottom);
    }

    internal static bool Valid(GodotObject? value) => value != null && GodotObject.IsInstanceValid(value);
}
