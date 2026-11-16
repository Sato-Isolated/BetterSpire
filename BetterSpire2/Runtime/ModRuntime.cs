#nullable enable
using System;
using System.Collections.Generic;
using BetterSpire2.DamageMeter;
using BetterSpire2.Journal.Game;
using BetterSpire2.Runtime.Native;
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

    internal static void EnsureStarted()
    {
        NativeCombatHooks.Start();
        RunLifecycle.EnsureAttached();
        CombatLifecycle.EnsureAttached();
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
            TryTick("NativeHooks", NativeCombatHooks.FlushPending);
            TryTick("Guardian", () => DamageTracker.Tick(now));
            TryTick("HandViewer", TeammateHandViewer.FlushPendingChanges);
            TryTick("Settings", SettingsMenu.RefreshBindings);
            if (now >= _nextJournal)
            {
                _nextJournal = now + 200;
                TryTick("Journal", JournalService.Tick);
                TryTick("DamageMeter", DamageMeterController.Tick); // reads the frame the journal just produced
            }
            if (now >= _nextClock) { _nextClock = now + 1000; TryTick("Clock", ClockDisplay.Update); }
        }
        finally { _ticking = false; }
    }
    private static void TryTick(string module, Action action)
    {
        try { action(); }
        catch (Exception ex) { ModLog.ErrorThrottled("Runtime.Tick." + module, ex); }
    }
    internal static void Stop()
    {
        // Teardown failures in one module must not leak into NGame._ExitTree or strand other observers.
        NGame_Input_Patch.ResetOwnedKeys();
        Cleanup(ReleaseTimer);
        Cleanup(NativeCombatHooks.Stop);
        Cleanup(CombatLifecycle.Stop);
        Cleanup(RunLifecycle.Stop);
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
    private static void Cleanup(Action action) => ModLog.Guard("Runtime.Stop", action);

    private static void ReleaseTimer()
    {
        if (_timer != null && GodotObject.IsInstanceValid(_timer))
        { _timer.Stop(); _timer.Timeout -= Tick; _timer.QueueFree(); }
        _timer = null;
    }
}
