#nullable enable
using MegaCrit.Sts2.Core.Combat;
using BetterSpire2.DamageMeter;
using System;
using System.Collections.Generic;

namespace BetterSpire2.Core;

internal enum ModSettingKind
{
    Toggle,
    Slider
}

internal enum ModSettingSectionId
{
    Combat,
    Multiplayer,
    Gameplay,
    DamageMeter
}

internal sealed class ModSettingSectionDefinition
{
    internal ModSettingSectionDefinition(ModSettingSectionId id, string title, string heading, string description, IReadOnlyList<ModSettingDefinition> settings)
    {
        Id = id;
        Title = title;
        Heading = heading;
        Description = description;
        Settings = settings;
    }

    internal ModSettingSectionId Id { get; }
    internal string Title { get; }
    internal string Heading { get; }
    internal string Description { get; }
    internal IReadOnlyList<ModSettingDefinition> Settings { get; }
}

internal static class ModSettingsCatalog
{
    internal static IReadOnlyList<ModSettingSectionDefinition> Sections { get; } = new[]
    {
        new ModSettingSectionDefinition(ModSettingSectionId.Combat, "Combat", "Combat information", "Make incoming damage and turn results easier to read.", new[]
        {
            ModSettingDefinition.Toggle("MultiHitTotals", "Multi-hit totals", "Show the combined value beside enemy multi-hit intents.", () => ModSettings.MultiHitTotals, value => ModSettings.MultiHitTotals = value, SettingsMenu.RefreshIntents),
            ModSettingDefinition.Toggle("PlayerDamageTotal", "Incoming damage", "Show remaining HP (HP lost) above characters; F2 opens optional details.", () => ModSettings.PlayerDamageTotal, value => ModSettings.PlayerDamageTotal = value, RefreshDamageVisibility),
            ModSettingDefinition.Toggle("GuardianOnlyDanger", "Only show danger", "Hide zero-loss projections, but always show uncertainty and used revivals.", () => ModSettings.GuardianOnlyDanger, value => ModSettings.GuardianOnlyDanger = value, DamageTracker.Recalculate),
            ModSettingDefinition.Slider("GuardianScalePercent", "Guardian scale", "Resize the overhead numbers without moving game elements.", () => ModSettings.GuardianScalePercent, value => ModSettings.GuardianScalePercent = value, 75, 150, 5, DamageTracker.Recalculate),
            ModSettingDefinition.Slider("GuardianOverheadGap", "Height above characters", "Distance between the head and the HP readout, in viewport pixels.", () => ModSettings.GuardianOverheadGap, value => ModSettings.GuardianOverheadGap = value, 8, 160, 4, DamageTracker.Recalculate),
            ModSettingDefinition.Slider("GuardianOverheadOffsetX", "Overhead horizontal offset", "Shift the overhead numbers left or right without moving characters.", () => ModSettings.GuardianOverheadOffsetX, value => ModSettings.GuardianOverheadOffsetX = value, -160, 160, 4, DamageTracker.Recalculate),
            ModSettingDefinition.Toggle("GuardianShowTeammates", "Numbers above teammates", "Show a separate conditional forecast above each teammate.", () => ModSettings.GuardianShowTeammates, value => ModSettings.GuardianShowTeammates = value, DamageTracker.Recalculate),
            ModSettingDefinition.Toggle("GuardianShowPets", "Numbers above pets", "Show a separate readout above Osty or another pet when it is projected to lose HP.", () => ModSettings.GuardianShowPets, value => ModSettings.GuardianShowPets = value, DamageTracker.Recalculate),
            ModSettingDefinition.Slider("GuardianXPercent", "Details horizontal position", "Move only the optional F2 details panel horizontally.", () => ModSettings.GuardianXPercent, value => ModSettings.GuardianXPercent = value, 0, 100, 1, DamageTracker.Recalculate),
            ModSettingDefinition.Slider("GuardianYPercent", "Details vertical position", "Move only the optional F2 details panel vertically.", () => ModSettings.GuardianYPercent, value => ModSettings.GuardianYPercent = value, 0, 100, 1, DamageTracker.Recalculate),
            ModSettingDefinition.Toggle("ShowTurnSummary", "Optional summary line", "Show a small observed-round text line. The full journal is always available with F5.", () => ModSettings.ShowTurnSummary, value => ModSettings.ShowTurnSummary = value, JournalController.Refresh)
        }),
        new ModSettingSectionDefinition(ModSettingSectionId.DamageMeter, "Damage meter", "Combat / run damage meter", "Current fight in combat; run total on the map. Steam nicknames in solo and co-op. F6 only hides the display.", new[]
        {
            ModSettingDefinition.Toggle("ShowDamageMeter", "Show damage meter", "Show combat damage in fights and run damage on the map, without a panel. Toggle with F6.", () => ModSettings.ShowDamageMeter, value => ModSettings.ShowDamageMeter = value, DamageMeterController.Refresh),
            ModSettingDefinition.Toggle("DamageMeterLocked", "Lock meter position", "Make the title click-through too. Unlock to drag the meter like the clock.", () => ModSettings.DamageMeterLocked, value => ModSettings.DamageMeterLocked = value, DamageMeterController.Refresh),
            ModSettingDefinition.Toggle("DamageMeterIncludeBlock", "Include enemy block", "Off: actual enemy HP removed, matching F5. On: also include damage absorbed by enemy block. Overkill is never added.", () => ModSettings.DamageMeterIncludeBlock, value => ModSettings.DamageMeterIncludeBlock = value, DamageMeterController.Refresh),
            ModSettingDefinition.Toggle("DamageMeterShowBars", "Thin ranking bars", "Show a thin comparative line under each player, only in multiplayer.", () => ModSettings.DamageMeterShowBars, value => ModSettings.DamageMeterShowBars = value, DamageMeterController.Refresh),
            ModSettingDefinition.Toggle("DamageMeterOnlyCombat", "Only show during combat", "Hide the meter on the map and outside combat. Leave off for automatic combat / run display.", () => ModSettings.DamageMeterOnlyCombat, value => ModSettings.DamageMeterOnlyCombat = value, DamageMeterController.Refresh),
            ModSettingDefinition.Slider("DamageMeterScalePercent", "Meter scale", "Resize the damage text and bars.", () => ModSettings.DamageMeterScalePercent, value => ModSettings.DamageMeterScalePercent = value, 75, 150, 5, DamageMeterController.Refresh),
            ModSettingDefinition.Slider("DamageMeterXPercent", "Meter horizontal position", "Drag the meter title to move it; the damage rows remain click-through.", () => ModSettings.DamageMeterXPercent, value => { ModSettings.DamageMeterXPercent = value; ModSettings.ClearDamageMeterPosition(); }, 0, 100, 1, DamageMeterController.Refresh),
            ModSettingDefinition.Slider("DamageMeterYPercent", "Meter vertical position", "Keep the meter away from cards and the overhead HP display.", () => ModSettings.DamageMeterYPercent, value => { ModSettings.DamageMeterYPercent = value; ModSettings.ClearDamageMeterPosition(); }, 0, 100, 1, DamageMeterController.Refresh)
        }),
        new ModSettingSectionDefinition(ModSettingSectionId.Multiplayer, "Multiplayer", "Team information", "Inspect teammate hands and control multiplayer overlays.", new[]
        {
            ModSettingDefinition.Toggle("ShowTeammateHand", "Teammate hand viewer", "Allow the F3 teammate hand and status viewer.", () => ModSettings.ShowTeammateHand, value => ModSettings.ShowTeammateHand = value, RefreshHandVisibility),
            ModSettingDefinition.Toggle("AutoShowTeammateHand", "Open at combat start", "Automatically show teammate information when combat begins.", () => ModSettings.AutoShowTeammateHand, value => ModSettings.AutoShowTeammateHand = value),
            ModSettingDefinition.Toggle("HideOwnHand", "Hide your own hand", "Only include teammates in the hand viewer.", () => ModSettings.HideOwnHand, value => ModSettings.HideOwnHand = value, TeammateHandViewer.RefreshIfVisible),
            ModSettingDefinition.Toggle("CompactHandViewer", "Team statistics instead of cards", "Show actual HP, block and energy without rendering cards.", () => ModSettings.CompactHandViewer, TeammateHandViewer.SetCompactMode),
            ModSettingDefinition.Slider("CardScalePercent", "Card size", "Resize the borderless thumbnails. At 100%, portraits are 42 x 42 viewport pixels; hover for details.", () => ModSettings.CardScalePercent, value => ModSettings.CardScalePercent = value, 50, 200, 10, TeammateHandViewer.RefreshIfVisible)
        }),
        new ModSettingSectionDefinition(ModSettingSectionId.Gameplay, "Gameplay", "Game conveniences", "Small options that reduce waiting and keep useful information visible.", new[]
        {
            ModSettingDefinition.Toggle("InstantFastMode", "Instant fast mode", "Remove combat speed delays while leaving menus unchanged.", () => ModSettings.InstantFastMode, value => ModSettings.InstantFastMode = value, RefreshInstantSpeed),
            ModSettingDefinition.Toggle("SkipSplash", "Skip splash screen", "Skip the introduction on the next game launch; this does not change the current screen.", () => ModSettings.SkipSplash, value => ModSettings.SkipSplash = value),
            ModSettingDefinition.Toggle("ShowClock", "Show clock", "Show a small movable clock overlay.", () => ModSettings.ShowClock, value => ModSettings.ShowClock = value, () => ClockDisplay.Toggle(ModSettings.ShowClock)),
            ModSettingDefinition.Toggle("Clock24Hour", "24-hour clock", "Use 24-hour time instead of AM and PM.", () => ModSettings.Clock24Hour, value => ModSettings.Clock24Hour = value, ClockDisplay.RefreshIfVisible)
        })
    };

    private static void RefreshDamageVisibility()
    {
        if (ModSettings.PlayerDamageTotal)
        {
            DamageTracker.Recalculate();
        }
        else
        {
            DamageTracker.Hide();
        }
    }

    private static void RefreshHandVisibility()
    {
        if (!ModSettings.ShowTeammateHand)
        {
            TeammateHandViewer.Hide();
        }
    }

    private static void RefreshInstantSpeed()
    {
        if (!ModSettings.InstantFastMode)
        {
            InstantSpeedHelper.OnCombatEnd();
        }
        else if (CombatManager.Instance is { IsOverOrEnding: false } combat &&
            (combat.IsInProgress || combat.IsStarting) && combat.DebugOnlyGetState() != null)
        {
            InstantSpeedHelper.OnCombatStart();
        }

        ModLog.Info($"Instant Fast Mode (combat-only): {ModSettings.InstantFastMode}");
    }
}
