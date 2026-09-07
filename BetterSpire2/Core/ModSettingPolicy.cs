#nullable enable
using System;

namespace BetterSpire2.Core;

/// <summary>Pure dependency policy; disabling a parent never erases its children's saved values.</summary>
internal static class ModSettingPolicy
{
    internal static string DisabledReason(string key, Func<string, bool> read)
    {
        if (key.StartsWith("Guardian", StringComparison.Ordinal) && !read("PlayerDamageTotal"))
            return "Enable incoming damage first.";
        if (key.StartsWith("DamageMeter", StringComparison.Ordinal) && !read("ShowDamageMeter"))
            return "Enable the damage meter first.";
        if (key is "AutoShowTeammateHand" or "HideOwnHand" or "CompactHandViewer" or "CardScalePercent")
        {
            if (!read("ShowTeammateHand")) return "Enable the hand viewer first.";
            if (key == "CardScalePercent" && read("CompactHandViewer")) return "Switch to cards to change their size.";
        }
        if (key == "Clock24Hour" && !read("ShowClock")) return "Enable the clock first.";
        return "";
    }
}
