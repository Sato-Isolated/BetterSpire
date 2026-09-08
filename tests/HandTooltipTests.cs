using System;
using BetterSpire2.HandViewer;

internal static class HandTooltipTests
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string message)
        { checks++; if (!ok) throw new Exception("Hand tooltip: " + message); }
        var above = HandTooltipGeometry.Place(100, 300, 342, 340, 180, 1920, 1080);
        Check(above == (100, 112), "prefer above the hovered card");
        var below = HandTooltipGeometry.Place(100, 20, 62, 340, 180, 1920, 1080);
        Check(below == (100, 70), "use below when near top edge");
        foreach (float vw in new[] { 320f, 1280f, 1920f, 3840f })
        foreach (float vh in new[] { 240f, 720f, 1080f })
        foreach (float x in new[] { 0f, vw / 2, vw - 21 })
        foreach (float y in new[] { 0f, vh / 2, vh - 42 })
        {
            float width = Math.Min(340, vw), height = Math.Min(300, vh);
            var p = HandTooltipGeometry.Place(x, y, y + 42, width, height, vw, vh);
            Check(p.X >= 0 && p.X + width <= vw, "horizontal viewport bounds");
            Check(p.Y >= 0 && p.Y + height <= vh, "vertical viewport bounds");
        }
        return checks;
    }
}
