#nullable enable
using System;
using BetterSpire2.UI;

internal static class HudCoexistenceTests
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string label)
        { checks++; if (!value) throw new Exception("FAIL HUD coexistence: " + label); }

        // Exercise the exact production policy, without creating native Godot objects.
        for (int mask = 0; mask < 256; mask++)
        {
            bool enabled = (mask & 1) != 0, active = (mask & 2) != 0;
            bool onlyCombat = (mask & 4) != 0, combat = (mask & 8) != 0;
            bool modal = (mask & 16) != 0, journal = (mask & 32) != 0;
            bool map = (mask & 64) != 0, handRequested = (mask & 128) != 0;
            bool blocked = HudVisibilityPolicy.IsTransientBlocker(modal, journal);
            bool meter = HudVisibilityPolicy.ShowMeter(enabled, active, onlyCombat, combat, blocked);
            bool hand = handRequested && HudVisibilityPolicy.ShowHand(blocked, map);
            Check(blocked == (modal || journal), "only transient blocking screens");
            Check(meter == (enabled && active && (!onlyCombat || combat) && !modal && !journal), "meter decision");
            Check(hand == (handRequested && !modal && !journal && !map), "hand independent from meter");
            if (enabled && active && combat && handRequested && !blocked && !map)
                Check(meter && hand, "both panels remain visible");
        }
        // Both key orders, repeated close/reopen and modal suppression preserve the other request.
        foreach (bool handFirst in new[] { true, false })
        {
            bool meterRequested = !handFirst, handRequested = handFirst;
            void Both(bool expectMeter, bool expectHand, bool blocked = false)
            {
                Check(HudVisibilityPolicy.ShowMeter(meterRequested, true, true, true, blocked) == expectMeter, "meter sequence");
                Check((handRequested && HudVisibilityPolicy.ShowHand(blocked, false)) == expectHand, "hand sequence");
            }
            Both(!handFirst, handFirst);
            if (handFirst) meterRequested = true; else handRequested = true;
            Both(true, true);
            Both(false, false, blocked: true);
            Both(true, true);
            meterRequested = false; Both(false, true);
            meterRequested = true; Both(true, true);
            handRequested = false; Both(true, false);
            handRequested = true; Both(true, true);
        }
        return checks;
    }
}
