#nullable enable
using System;
using System.Collections.Generic;
using BetterSpire2.Journal.Game;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using Timer = Godot.Timer;

namespace BetterSpire2.Runtime;

/// <summary>One main-thread heartbeat. Mouse events never initialize or poll modules.</summary>
internal static class ModRuntime
{
    private static Timer? _timer;
    private static ulong _nextJournal, _nextClock;
    private static bool _ticking;
    private static readonly Dictionary<string, ulong> LastErrors = new();

    internal static void EnsureStarted()
    {
        var game = NGame.Instance;
        if (game == null || !GodotObject.IsInstanceValid(game) || !game.IsInsideTree()) return;
        if (_timer != null && GodotObject.IsInstanceValid(_timer) && _timer.GetParent() == game) return;
        ReleaseTimer();
        _nextJournal = _nextClock = 0;
        _timer = new Timer { Name = "BetterSpireRuntime", WaitTime = .1, OneShot = false };
        _timer.Timeout += Tick;
        game.AddChild(_timer);
        _timer.Start();
        ModConfigBridge.TryRegister();
    }
    private static void Tick()
    {
        if (_ticking) return;
        _ticking = true;
        try
        {
            ulong now = Time.GetTicksMsec();
            TryTick("Guardian", () => DamageTracker.Tick(now), now);
            TryTick("HandViewer", TeammateHandViewer.FlushPendingChanges, now);
            TryTick("Settings", SettingsMenu.RefreshBindings, now);
            if (now >= _nextJournal) { _nextJournal = now + 200; TryTick("Journal", JournalService.Tick, now); }
            if (now >= _nextClock) { _nextClock = now + 1000; TryTick("Clock", ClockDisplay.Update, now); }
        }
        finally { _ticking = false; }
    }
    private static void TryTick(string module, Action action, ulong now)
    {
        try { action(); }
        catch (Exception ex)
        {
            if (!LastErrors.TryGetValue(module, out ulong last) || now - last >= 10000)
            { LastErrors[module] = now; ModLog.Error("Runtime.Tick." + module, ex); }
        }
    }
    internal static void Stop()
    {
        // Teardown failures in one module must not leak into NGame._ExitTree or strand other observers.
        LastErrors.Clear();
        NGame_Input_Patch.ResetOwnedKeys();
        Cleanup(ReleaseTimer);
        Cleanup(DamageTracker.Hide);
        Cleanup(SettingsMenu.Hide);
        Cleanup(ClockDisplay.Stop);
        Cleanup(BetterSpire2.DamageMeter.DamageMeterController.Stop);
        Cleanup(HudOverlayManager.Reset);
        Cleanup(JournalController.BeforeCombatReset);
        Cleanup(JournalService.FlushPersistence);
        Cleanup(TeammateHandViewer.Stop);
        Cleanup(ModConfigBridge.CancelPending);
        Cleanup(PerformanceProbe.Stop);
        Cleanup(InstantSpeedHelper.OnCombatEnd);
        PartyManager.MapDrawings = null;
        PartyManager.ClearMutes();
    }
    private static void Cleanup(Action action)
    {
        try { action(); }
        catch (Exception ex) { ModLog.Error("Runtime.Stop", ex); }
    }

    private static void ReleaseTimer()
    {
        if (_timer != null && GodotObject.IsInstanceValid(_timer))
        { _timer.Stop(); _timer.Timeout -= Tick; _timer.QueueFree(); }
        _timer = null;
    }
}
