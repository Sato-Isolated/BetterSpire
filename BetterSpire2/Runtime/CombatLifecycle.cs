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

    // The only place that tells features about combat boundaries. Patches call a phase, never a feature;
    // each feature step is isolated so one failure cannot skip the others or reach native game code.

    private static void OnSetUp(CombatState state)
    {
        _state = state;
        _combatId = _manager?.CurrentCombatId;
        Guard("InstantSpeed", InstantSpeedHelper.OnCombatStart);
        Guard("Guardian", DamageTracker.OnCombatSetUp);
        Guard("Journal", JournalController.OnCombatSetUp);
        Guard("HandViewer", TeammateHandViewer.OnCombatSetUp);
        Guard("Clock", ClockDisplay.SyncVisibility);
    }

    /// <summary>Victory or defeat has been decided; runs before native teardown so overlays hide at once.</summary>
    internal static void Ending(bool lost)
    {
        Guard("Guardian.Ending", DamageTracker.Hide);
        Guard("HandViewer.Ending", TeammateHandViewer.Hide);
        Guard("Journal.Ending", () => JournalController.OnCombatEnding(lost));
        Guard("InstantSpeed.Ending", InstantSpeedHelper.OnCombatEnd);
    }

    /// <summary>Before CombatManager.Reset clears native state the journal still needs to read.</summary>
    internal static void BeforeReset() => Guard("Journal.BeforeReset", JournalController.BeforeCombatReset);

    /// <summary>After CombatManager.Reset: nothing of the old combat may survive.</summary>
    internal static void AfterReset()
    {
        Guard("Forget", ForgetCombat);
        Guard("Guardian.Reset", DamageTracker.Hide);
        Guard("HandViewer.Reset", TeammateHandViewer.Stop); // stops, not just hides: the combat objects are gone
        Guard("Journal.Reset", JournalController.Hide);
        Guard("InstantSpeed.Reset", InstantSpeedHelper.OnCombatEnd);
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
        // The journal was already sealed by Ending(); this native event only confirms the teardown.
        Guard("Guardian.End", DamageTracker.Hide);
        Guard("HandViewer.End", TeammateHandViewer.Hide);
        Guard("InstantSpeed.End", InstantSpeedHelper.OnCombatEnd);
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

    private static void Guard(string module, Action action) => ModLog.Guard("CombatLifecycle." + module, action);
}
