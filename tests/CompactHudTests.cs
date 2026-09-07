#nullable enable
using System;
using BetterSpire2.UI;

internal static class CompactHudTests
{
    private static int _checks;
    private static void Check(bool ok, string message)
    { _checks++; if (!ok) throw new Exception("Compact HUD: " + message); }
    private static bool Near(float a, float b) => Math.Abs(a - b) < .01f;
    internal static int Run()
    {
        Check(CompactHudGeometry.ScaledCardWidth(100) == 42, "portrait width is half the old 84 px");
        Check(CompactHudGeometry.ScaledPortraitHeight(100) == 42, "square portrait height");
        var five = CompactHudGeometry.HandStrip(5, 100, 1920);
        Check(Near(five.Width, 226) && Near(five.Height, 65) && !five.Overflow, "5 cards: 226 x 65, no scrollbar");
        var ten = CompactHudGeometry.HandStrip(10, 100, 1920);
        Check(Near(ten.Width, 420) && Near(ten.Height, 75) && ten.Overflow, "10 cards: overflow, no second row");
        Check(CompactHudGeometry.SettingControlWidth == 132 && CompactHudGeometry.SettingControlHeight == 32, "stable control slot");
        Check(CompactHudGeometry.SettingsScrollGutter >= 12, "space between content and scrollbar");
        foreach (float width in new[] { 320f, 640f, 1280f, 1366f, 1649f, 1920f, 2560f, 3840f })
        foreach (int count in new[] { 0, 1, 5, 8, 10, 20, 100, int.MaxValue })
        foreach (int scale in new[] { 0, 50, 75, 100, 125, 150, 175, 200, 500 })
        {
            Check(CompactHudGeometry.ScaledPortraitHeight(scale) == CompactHudGeometry.ScaledCardWidth(scale), "square at every scale");
            var hand = CompactHudGeometry.HandStrip(count, scale, width);
            Check(float.IsFinite(hand.Width) && hand.Width > 0 && hand.Width <= CompactHudGeometry.Available(width, 16), "strip fits viewport");
            Check(hand.Height <= 117, "long hand never becomes a tall grid");
            Check(CompactHudGeometry.ScaledCardWidth(scale) >= 21 && CompactHudGeometry.ScaledCardWidth(scale) <= 84, "scale is bounded");
        }
        foreach (float width in new[] { 320f, 640f, 1280f, 1649f, 1920f, 3840f })
        foreach (float height in new[] { 240f, 360f, 720f, 900f, 1080f, 2160f })
        foreach (float content in new[] { 0f, 140f, 500f, 1600f })
        {
            float w = CompactHudGeometry.SettingsWidth(width);
            float h = CompactHudGeometry.SettingsHeight(height, 164, content);
            Check(w > 0 && w <= CompactHudGeometry.Available(width, 24), "F1 width fits before dragging");
            Check(h > 0 && h <= Math.Min(760, CompactHudGeometry.Available(height, 24)), "F1 height remains in safe rectangle");
            float x = OverlayAxis.Position(.5f, w, Math.Min(24, width * .25f), CompactHudGeometry.Available(width, 24));
            Check(x >= 0 && x + w <= width + .01f, "centered panel is fully visible");
        }
        Check(Near(CompactHudGeometry.SettingsHeight(1080, 164, 200), 364), "short section shrinks to content");
        Check(Near(CompactHudGeometry.SettingsHeight(1080, 164, 5000), 760), "long section scrolls instead of expanding beyond screen");
        Console.WriteLine($"PASS {_checks} compact-HUD production-geometry checks. No Godot rendering is executed by these tests.");
        return _checks;
    }
}
