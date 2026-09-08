#nullable enable
using System;
using MegaCrit.Sts2.Core.Runs;

namespace BetterSpire2.Runtime;

/// <summary>Native run state delivery. The debug accessor is used only once on late observer attachment.</summary>
internal static class RunLifecycle
{
    private static RunManager? _manager;
    internal static void EnsureAttached()
    {
        var manager = RunManager.Instance;
        if (ReferenceEquals(_manager, manager)) return;
        Stop();
        if (manager == null) return;
        _manager = manager;
        manager.RunStarted += OnStarted;
        // State and NRun._state are private in v0.111. Never poll them in the journal heartbeat.
        if (manager.IsInProgress && !manager.IsCleaningUp && manager.DebugOnlyGetState() is { } state)
            OnStarted(state);
    }
    private static void OnStarted(RunState state)
    {
        try { JournalService.BeginRun(state); }
        catch (Exception ex) { JournalService.Session.MarkPartial(); ModLog.Error("RunLifecycle.Started", ex); }
    }
    internal static void Stop()
    {
        if (_manager != null) _manager.RunStarted -= OnStarted;
        _manager = null;
    }
}
