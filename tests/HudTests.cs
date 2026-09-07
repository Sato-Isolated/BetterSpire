#nullable enable
using System;
using System.Collections.Generic;
using BetterSpire2.Core;
using BetterSpire2.HandViewer;
using BetterSpire2.UI;

internal static class HudTests
{
    private static int _checks;
    private static void Check(bool value, string name)
    { _checks++; if (!value) throw new Exception("FAIL " + name); }
    private static bool Near(float a, float b) => Math.Abs(a - b) < .002f;
    public static int Main()
    {
        string[] parents = { "PlayerDamageTotal", "ShowDamageMeter", "ShowTeammateHand", "CompactHandViewer", "ShowClock" };
        for (int mask = 0; mask < 32; mask++)
        {
            var states = new Dictionary<string, bool>();
            for (int i = 0; i < parents.Length; i++) states[parents[i]] = (mask & (1 << i)) != 0;
            bool Read(string key) => states.TryGetValue(key, out var value) && value;
            bool Enabled(string key) => ModSettingPolicy.DisabledReason(key, Read).Length == 0;
            foreach (string key in new[] { "GuardianOnlyDanger", "GuardianScalePercent", "GuardianOverheadGap", "GuardianXPercent", "GuardianShowPets" })
                Check(Enabled(key) == Read("PlayerDamageTotal"), key);
            foreach (string key in new[] { "DamageMeterLocked", "DamageMeterXPercent", "DamageMeterScalePercent", "DamageMeterShowBars" })
                Check(Enabled(key) == Read("ShowDamageMeter"), key);
            foreach (string key in new[] { "AutoShowTeammateHand", "HideOwnHand", "CompactHandViewer" })
                Check(Enabled(key) == Read("ShowTeammateHand"), key);
            Check(Enabled("CardScalePercent") == (Read("ShowTeammateHand") && !Read("CompactHandViewer")), "card scale dependency");
            Check(Enabled("Clock24Hour") == Read("ShowClock"), "clock dependency");
            foreach (string key in new[] { "PlayerDamageTotal", "ShowDamageMeter", "ShowTeammateHand", "ShowClock", "MultiHitTotals", "ShowTurnSummary", "SkipSplash", "InstantFastMode" })
                Check(Enabled(key), "independent " + key);
        }
        Check(HandViewerSelection.Preserve(new ulong[] { 8, 0, 99 }, 0, 0) == 1, "NetId zero selected");
        Check(HandViewerSelection.Preserve(new ulong[] { 99, 8 }, 8, 0) == 1, "selection survives reorder");
        Check(HandViewerSelection.Preserve(new ulong[] { 99, 8 }, 0, 90) == 1, "missing player clamps");
        Check(HandViewerSelection.Preserve(Array.Empty<ulong>(), 0, 20) == 0, "empty selection");
        Check(HandViewerSelection.Move(0, 0, -1) == 0, "empty navigation");
        Check(HandViewerSelection.Move(0, 4, -1) == 3, "previous wraps");
        Check(HandViewerSelection.Move(3, 4, 1) == 0, "next wraps");
        Check(HandViewerSelection.Move(int.MaxValue, 4, 1) == 0, "overflow safe navigation");
        foreach (float extent in new[] { 1f, 50f, 320f, 720f, 1080f, 2160f, 3440f })
        foreach (float size in new[] { 1f, 28f, 76f, 216f, 640f, 1000f })
        for (int percent = 0; percent <= 100; percent++)
        {
            float start = 16f, u = percent / 100f;
            float position = OverlayAxis.Position(u, size, start, extent);
            Check(position >= start && position <= start + Math.Max(0, extent - size) + .002f, "bounds");
            float back = OverlayAxis.Normalize(position, size, start, extent);
            Check(Near(back, extent - size <= .001f ? 0f : u), "normalized round trip");
            Check(Near(OverlayAxis.Clamp(position, size, start, extent, 12, false), position), "no spontaneous snap");
        }
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Check(OverlayAxis.Unit(invalid) == 0, "invalid saved normalized position");
            Check(OverlayAxis.Clamp(invalid, 76, 16, 1000, 12, false) == 16, "invalid pixels");
        }
        Check(OverlayAxis.Unit(-1) == 0 && OverlayAxis.Unit(2) == 1, "normalized clamp");
        Check(OverlayAxis.Clamp(22, 76, 16, 1000, 12, true) == 16, "left snap");
        Check(OverlayAxis.Clamp(935, 76, 16, 1000, 12, true) == 940, "right snap");
        _checks += HudCoexistenceTests.Run();
        _checks += CompactHudTests.Run();
        Console.WriteLine($"PASS {_checks} HUD pure C# checks. Godot rendering / in-game behavior NOT tested.");
        return 0;
    }
}
