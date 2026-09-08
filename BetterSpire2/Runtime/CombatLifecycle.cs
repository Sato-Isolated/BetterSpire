#nullable enable
using System;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Rooms;

namespace BetterSpire2.Runtime;

/// <summary>Native lifecycle subscriptions, independent of whether an overlay is visible.</summary>
internal static class CombatLifecycle
{
    private static CombatManager? _manager;
    private static CombatState? _state;
    private static CombatId? _combatId;

    internal static CombatState? CurrentState => _manager != null &&
        _manager.CurrentCombatId == _combatId && (_manager.IsStarting || _manager.IsInProgress)
            ? _state : null;

    internal static void EnsureAttached()
    {
        var manager = CombatManager.Instance;
        if (ReferenceEquals(manager, _manager)) return;
        Stop();
        if (manager == null) return;
        _manager = manager;
        manager.CombatSetUp += OnSetUp;
        manager.CombatBegan += OnBegan;
        manager.CombatEnded += OnEnded;
        // Only bootstrap uses the debug accessor: initialization may occur mid-combat.
        // Ordinary updates use the state supplied by native events and its combat id.
        if ((manager.IsStarting || manager.IsInProgress) && !manager.IsOverOrEnding &&
            manager.DebugOnlyGetState() is { } state)
            OnSetUp(state);
    }

    private static void OnSetUp(CombatState state)
    {
        _state = state;
        _combatId = _manager?.CurrentCombatId;
        Safe("InstantSpeed", InstantSpeedHelper.OnCombatStart);
        Safe("Guardian", DamageTracker.OnCombatSetUp);
        Safe("Journal", JournalController.OnCombatSetUp);
        Safe("HandViewer", TeammateHandViewer.OnCombatSetUp);
        Safe("Clock", ClockDisplay.SyncVisibility);
    }

    private static void OnBegan(CombatState state)
    {
        _state = state;
        _combatId = _manager?.CurrentCombatId;
    }

    private static void OnEnded(CombatRoom room)
    {
        // Never let a delayed old-combat event tear down a newly running combat.
        if (_manager == null || _manager.IsStarting || _manager.IsInProgress ||
            !ReferenceEquals(_state?.RunState.CurrentRoom, room)) return;
        Safe("Guardian.End", DamageTracker.Hide);
        Safe("HandViewer.End", TeammateHandViewer.Hide);
        Safe("InstantSpeed.End", InstantSpeedHelper.OnCombatEnd);
        ForgetCombat();
    }

    internal static void ForgetCombat()
    {
        _state = null; _combatId = null;
        Native.NativeCombatHooks.ForgetCombat();
    }

    internal static void Stop()
    {
        if (_manager != null)
        {
            _manager.CombatSetUp -= OnSetUp;
            _manager.CombatBegan -= OnBegan;
            _manager.CombatEnded -= OnEnded;
        }
        _manager = null;
        ForgetCombat();
    }

    private static void Safe(string module, Action action)
    {
        try { action(); }
        catch (Exception ex) { ModLog.Error("CombatLifecycle." + module, ex); }
    }
}
