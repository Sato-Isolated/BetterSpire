#nullable enable
using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;

namespace BetterSpire2.Runtime.Native;

/// <summary>
/// One native combat subscription for the process lifetime. RunState already visits
/// combat listeners, so registering here AND for run hooks would duplicate callbacks.
/// Hook callbacks only set a flag. UI work/history reads happen on the mod heartbeat,
/// never while the game's hook dispatcher owns a PlayerChoiceContext model stack.
/// </summary>
internal static class NativeCombatHooks
{
    private const string SubscriptionId = "com.jdr.betterspire2lite.observer.v111";
    private static bool _registered, _enabled, _faulted;
    private static Exception? _pendingError;
    private static BetterSpireCombatObserver? _current;
    private static AbstractModel[] _listeners = Array.Empty<AbstractModel>();
    internal static event Action<CombatState>? StateChanged;

    internal static void Start()
    {
        if (_faulted) return; // Existing events/history remain the safe fallback.
        _enabled = true;
        if (_registered) return;
        try
        {
            // Do not construct/inject models here. The game initializes ModelDb later
            // from this mod's types, then assigns IDs with its native content sorter.
            ModHelper.SubscribeForCombatStateHooks(SubscriptionId, Provide);
            _registered = true;
        }
        catch (Exception ex) { Fail(ex); }
    }

    private static IEnumerable<AbstractModel> Provide(CombatState state)
    {
        if (!_enabled || _faulted) return Array.Empty<AbstractModel>();
        try
        {
            var manager = CombatManager.Instance;
            if (!IsLive(state, manager)) return Array.Empty<AbstractModel>();
            if (_current == null || !_current.Matches(state, manager!))
            {
                // Native canonical model -> mutable per-combat observer, not a card,
                // power, relic or game command. No saved fields and no network messages.
                var canonical = ModelDb.GetById<BetterSpireCombatObserver>(
                    ModelDb.GetId<BetterSpireCombatObserver>());
                var observer = (BetterSpireCombatObserver)canonical.MutableClone();
                observer.Bind(state, manager!);
                _current = observer;
                _listeners = new AbstractModel[] { observer };
            }
            return _listeners;
        }
        catch (Exception ex) { Fail(ex); return Array.Empty<AbstractModel>(); }
    }

    private static bool IsLive(CombatState state, CombatManager? manager) =>
        manager != null && manager.CurrentCombatId != null &&
        ReferenceEquals(CombatLifecycle.CurrentState, state) &&
        // v111 IsOverOrEnding can be true during setup; setup is a native exception.
        (manager.IsStarting || (manager.IsInProgress && !manager.IsOverOrEnding));

    internal static bool IsActive(BetterSpireCombatObserver observer) =>
        _enabled && !_faulted && ReferenceEquals(observer, _current) &&
        observer.State != null && observer.Matches(observer.State, CombatManager.Instance) &&
        IsLive(observer.State, CombatManager.Instance);

    internal static void Notify(BetterSpireCombatObserver observer)
    {
        try { if (IsActive(observer)) observer.Pending = true; }
        // Never throw, log, touch Godot, drain history or run user callbacks from a hook.
        catch (Exception ex) { Fail(ex); }
    }

    internal static void FlushPending()
    {
        if (_pendingError is { } error)
        {
            _pendingError = null;
            ModLog.Error("NativeHooks.Disabled (native events/history remain active)", error);
        }
        var observer = _current;
        if (observer == null) return;
        if (!IsActive(observer)) { ForgetCombat(); return; }
        if (!observer.Pending) return;
        observer.Pending = false;
        var handlers = StateChanged;
        if (handlers == null) return;
        var state = observer.State!;
        foreach (Action<CombatState> handler in handlers.GetInvocationList())
        {
            // A consumer may end/reset the combat; do not notify the next one for it.
            if (!IsActive(observer)) break;
            try { handler(state); }
            catch (Exception ex) { ModLog.Error("NativeHooks.Consumer", ex); }
        }
    }

    internal static void ForgetCombat()
    {
        _current = null;
        _listeners = Array.Empty<AbstractModel>();
    }

    internal static void Stop()
    {
        _enabled = false;
        ForgetCombat();
        // ModHelper has no unsubscribe. Keep one inert provider; Start re-enables it
        // rather than registering it again. Subscribers detach in their own Dispose.
    }

    private static void Fail(Exception ex)
    {
        _pendingError ??= ex;
        _faulted = true;
        _enabled = false;
        ForgetCombat();
    }
}
