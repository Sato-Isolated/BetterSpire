#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using BetterSpire2.DamageMeter;
using MegaCrit.Sts2.Core.Nodes;

namespace BetterSpire2.Patches.Input;

/// <summary>Consume only owned shortcuts and active drags, before native NGame input.</summary>
[HarmonyPatch(typeof(NGame), nameof(NGame._Input))]
internal static class NGame_Input_Patch
{
    private static readonly HashSet<Key> OwnedKeys = new();
    internal static void ResetOwnedKeys() => OwnedKeys.Clear();
    // Native NGame debug shortcuts are tested on release, so consume the whole owned key cycle.
    private static bool HandleKeyEvent(InputEventKey key)
    {
        if (key.IsEcho()) return OwnedKeys.Contains(key.Keycode);
        if (!key.Pressed) return OwnedKeys.Remove(key.Keycode);
        bool handled = HandleKey(key);
        if (handled) OwnedKeys.Add(key.Keycode);
        return handled;
    }
    [HarmonyPrefix]
    private static bool Prefix(InputEvent inputEvent)
    {
        try
        {
            if (NGame.Instance?.FeedbackScreen?.Visible == true) { HudOverlayManager.Reset(); return true; }
            bool handled = inputEvent switch
            {
                InputEventKey key => HandleKeyEvent(key),
                InputEventMouseButton or InputEventMouseMotion => HudOverlayManager.HandleInput(inputEvent),
                _ => false
            };
            if (!handled) return true;
            NGame.Instance?.GetViewport().SetInputAsHandled();
            return false;
        }
        catch (Exception ex) { ModLog.Error("BetterSpire.Input", ex); return true; }
    }
    private static bool HandleKey(InputEventKey key)
    {
        // Hidden mod windows must not steal Escape/navigation from native modal screens.
        if (!SettingsMenu.IsVisible && JournalService.GameUiBlocking &&
            key.Keycode is Key.Escape or Key.Pageup or Key.Pagedown) return false;
        if (key.AltPressed)
        {
            if (!key.CtrlPressed && DamageTracker.DetailsVisible && key.Keycode is Key.Pageup or Key.Pagedown)
            { DamageTracker.ChangeDetailPage(key.Keycode == Key.Pagedown ? 1 : -1); return true; }
            return false;
        }
        if (key.CtrlPressed)
        {
            switch (key.Keycode)
            {
                case Key.F2: DamageTracker.ExportLastForecast(); return true;
                case Key.F5: JournalController.ExportJournal(); return true;
                case Key.F7: PerformanceProbe.Export(); return true;
                default: return false;
            }
        }
        switch (key.Keycode)
        {
            case Key.F1: SettingsMenu.Toggle(); return true;
            case Key.F2:
                if (ModSettings.PlayerDamageTotal) { JournalController.Hide(); DamageTracker.ToggleDetails(); }
                return true;
            case Key.F3: TeammateHandViewer.Toggle(); return true;
            case Key.F4: DamageTracker.ToggleVisibility(); return true;
            case Key.F5:
                if (DamageTracker.DetailsVisible) DamageTracker.ToggleDetails();
                JournalController.ToggleJournal(); return true;
            case Key.F6: DamageMeterController.Toggle(); return true;
            case Key.F7: PerformanceProbe.Toggle(); return true;
            case Key.Escape:
                if (SettingsMenu.IsVisible) { SettingsMenu.Hide(); return true; }
                if (TeammateHandViewer.IsVisible) { TeammateHandViewer.Hide(); return true; }
                if (JournalController.IsJournalVisible) { JournalController.Hide(); return true; }
                if (DamageTracker.DetailsVisible) { DamageTracker.ToggleDetails(); return true; }
                return false;
            case Key.Pageup when TeammateHandViewer.IsVisible && !SettingsMenu.IsVisible:
                TeammateHandViewer.PrevPage(); return true;
            case Key.Pagedown when TeammateHandViewer.IsVisible && !SettingsMenu.IsVisible:
                TeammateHandViewer.NextPage(); return true;
            default: return false;
        }
    }
}
