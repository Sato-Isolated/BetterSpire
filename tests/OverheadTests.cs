using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterSpire2.Guardian.Core;

internal static class OverheadTests
{
    private sealed record ExpectedReadout(string Actor, string Text, OverheadSeverity Severity, bool DangerVisible);
    private sealed record Fixture(string Name, ForecastInput Input, ExpectedReadout[] Expected);

    internal static void Run()
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var fixtures = JsonSerializer.Deserialize<List<Fixture>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "overhead_fixtures.json")), options)!;
        foreach (var fixture in fixtures)
        {
            var result = ForecastSimulator.Run(fixture.Input);
            foreach (var expected in fixture.Expected)
            {
                var actor = result.Actors[expected.Actor];
                var readout = OverheadPresentation.Format(actor, result.IsPartial);
                Equal(expected.Text, readout.Text, fixture.Name + " text");
                Equal(false, readout.Text.Contains('?') || readout.Text.Contains('*'), fixture.Name + " no suffix markers");
                Equal(OverheadPresentation.Format(actor, false).Text,
                    OverheadPresentation.Format(actor, true).Text, fixture.Name + " uncertainty changes color, not text");
                Equal(expected.Severity, readout.Severity, fixture.Name + " severity");
                Equal(expected.DangerVisible, OverheadPresentation.ShouldShow(actor, result.IsPartial, true), fixture.Name + " danger-only");
                Equal(true, OverheadPresentation.ShouldShow(actor, result.IsPartial, false), fixture.Name + " always-on");
            }
        }
        // No locale-specific grouping or decimal text may distort the compact integer readout.
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-CH");
            var example = ForecastSimulator.Run(fixtures[0].Input).Local;
            Equal("34 (36)", OverheadPresentation.Format(example, false).Text, "culture invariant");
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
        Equal("…", OverheadPresentation.Resolving, "pending has no stale number");
        Equal("", OverheadPresentation.Unavailable, "unavailable is hidden, not a fabricated zero");
        LayoutCases();
    }

    private static void LayoutCases()
    {
        var viewport = new HudRect(0, 0, 1000, 700);
        var noObstacles = Array.Empty<HudRect>();
        HudRect? Place(float x = 300, float y = 400, float w = 100, float h = 30, float offset = 0,
            IReadOnlyList<HudRect>? obstacles = null) => OverheadLayout.Place(x, y, w, h, offset, 16, viewport, obstacles ?? noObstacles);
        Equal(new HudRect(250, 354, 100, 30), Place(), "centered above head");
        Equal(new HudRect(270, 354, 100, 30), Place(offset: 20), "adjustable horizontal offset");
        Equal(new HudRect(6, 354, 100, 30), Place(x: 10), "left screen margin");
        Equal(new HudRect(894, 354, 100, 30), Place(x: 995), "right screen margin");
        var intent = new HudRect(240, 350, 120, 40);
        Equal(new HudRect(250, 314, 100, 30), Place(obstacles: new[] { intent }), "native intent avoidance");
        Equal(new HudRect(250, 274, 100, 30), Place(obstacles: new[] { intent, new HudRect(240, 310, 120, 30) }), "stacked obstacles");
        Equal(null, Place(y: 20), "no space above head: hide");
        Equal(null, Place(x: -20), "offscreen actor: hide");
        Equal(null, Place(w: 1100), "oversized text: hide rather than clip");
        Equal(null, Place(w: -1), "invalid dimensions");
        Equal(null, Place(x: float.NaN), "invalid transform");
        Equal(null, Place(x: float.PositiveInfinity), "infinite transform");
        // Deterministic screen-size sweep; successful placements must stay on-screen and above the actor.
        int visible = 0;
        for (int x = 0; x <= 1000; x += 25)
        for (int y = 0; y <= 700; y += 25)
        {
            var placed = Place(x, y, obstacles: new[] { intent });
            if (placed is not { } r) continue;
            visible++;
            if (r.X < 6 || r.Y < 6 || r.Right > 994 || r.Bottom > 694 || r.Bottom > y - 16 || r.Intersects(intent))
                throw new Exception("Overhead rectangle covers an obstacle or leaves the viewport.");
        }
        if (visible == 0) throw new Exception("Geometry sweep placed no labels.");
    }

    private static void Equal(object? expected, object? actual, string label)
    { if (!Equals(expected, actual)) throw new Exception($"{label}: expected {expected}, got {actual}"); }
}
