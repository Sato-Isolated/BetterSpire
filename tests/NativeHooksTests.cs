#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BetterSpire2.Runtime;
using BetterSpire2.Runtime.Native;
using BetterSpire2.Guardian.Game;
using BetterSpire2.Journal.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;

internal static class NativeHooksTests
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    private static BetterSpireCombatObserver Observer(CombatState state) =>
        (BetterSpireCombatObserver)ModHelper.Provide(state).Single();
    private static void FireAll(BetterSpireCombatObserver observer)
    {
        var methods = typeof(BetterSpireCombatObserver).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType == typeof(Task)).ToArray();
        Check(methods.Length == 13, "only thirteen notification hooks implemented");
        foreach (var method in methods)
        {
            object?[] args = method.GetParameters().Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
            var task = (Task)method.Invoke(observer, args)!;
            Check(task.IsCompletedSuccessfully, method.Name + " completes synchronously without throwing or requesting choices");
        }
    }
    private static int Main()
    {
        try
        {
            var manager = CombatManager.Instance = new() { CurrentCombatId = new(1), IsStarting = true, IsOverOrEnding = true };
            var state = CombatLifecycle.CurrentState = new CombatState();
            NativeCombatHooks.Start(); NativeCombatHooks.Start();
            Check(ModHelper.Registrations == 1, "one process-wide combat subscription");
            Check(ModelDb.Lookups == 0, "registration never touches ModelDb before game initialization");
            ModelDb.Initialize();
            var observer = Observer(state);
            Check(observer.IsMutable && !ModelDb.Canonical!.IsMutable, "canonical instance stays detached and immutable");
            Check(observer.ShouldReceiveCombatHooks, "native setup exception supported even if IsOverOrEnding");
            Check(ReferenceEquals(observer, Observer(state)), "one reused observer per combat");
            Check(ModelDb.Lookups == 1, "no repeated ModelDb access on hook dispatch");
            Check(!ModHelper.Provide(new CombatState()).Any(), "foreign combat enumeration produces no observer");
            int calls = 0;
            void Count(CombatState s) { Check(ReferenceEquals(s, CombatLifecycle.CurrentState), "current state only"); calls++; }
            NativeCombatHooks.StateChanged += Count;
            for (int i = 0; i < 1000; i++) FireAll(observer);
            Check(calls == 0, "no UI/user callbacks from native hooks");
            Check(!ModelDb.Canonical!.Pending && ModelDb.Canonical.State == null, "canonical remains untouched");
            NativeCombatHooks.FlushPending();
            Check(calls == 1, "thirteen thousand notifications coalesced into one flush");
            NativeCombatHooks.FlushPending();
            Check(calls == 1, "no repeated notification while clean");
            manager.IsStarting = false; manager.IsOverOrEnding = false; manager.IsInProgress = true;
            var command = new AttackCommand { Sentinel = 42 };
            var ctx = new MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext();
            ctx.Models.Add(ModelDb.Canonical);
            observer.BeforeAttack(command); observer.AfterAttack(ctx, command);
            Check(command.Sentinel == 42 && ctx.Models.Count == 1 && ReferenceEquals(ctx.Models[0], ModelDb.Canonical),
                "attack and native choice-context stack untouched");
            NativeCombatHooks.FlushPending();
            Check(calls == 2, "normal combat hook invalidation");

            // Both native history and hooks can request reads: the real production cursor
            // visits the same native-like result just once, never once per notification.
            var cursor = new HistoryCursor();
            var entries = new List<object> { new object() }; int total = 0;
            void Drain(CombatState _) => cursor.Consume(entries, _ => total += 12);
            NativeCombatHooks.StateChanged += Drain;
            Drain(state); // history notification first
            observer.BeforeAttack(command); observer.AfterAttack(ctx, command); NativeCombatHooks.FlushPending();
            Check(total == 12, "native event plus hooks do not reread a processed result");
            entries.Add(new object()); observer.BeforeAttack(command); NativeCombatHooks.FlushPending();
            Check(total == 24, "later native result read once");
            NativeCombatHooks.StateChanged -= Drain;

            void Throw(CombatState _) => throw new InvalidOperationException("consumer failure");
            int survivor = 0; void Survive(CombatState _) => survivor++;
            NativeCombatHooks.StateChanged += Throw; NativeCombatHooks.StateChanged += Survive;
            observer.BeforeAttack(command); NativeCombatHooks.FlushPending();
            Check(survivor == 1 && ModLog.Errors == 1, "one faulty consumer cannot block later consumer or game");
            NativeCombatHooks.StateChanged -= Throw; NativeCombatHooks.StateChanged -= Survive;
            int before = calls;
            manager.CurrentCombatId = new(2); FireAll(observer); NativeCombatHooks.FlushPending();
            Check(calls == before && !observer.ShouldReceiveCombatHooks, "old combat ID rejected, even if state reused");
            var next = Observer(state);
            Check(!ReferenceEquals(observer, next), "new identity gets its own mutable observer");
            FireAll(observer); NativeCombatHooks.FlushPending(); Check(calls == before, "late old coroutine cannot affect new combat");
            FireAll(next); NativeCombatHooks.FlushPending(); Check(calls == before + 1, "new combat observed");
            before = calls;
            manager.IsOverOrEnding = true; FireAll(next); NativeCombatHooks.FlushPending();
            Check(calls == before && !ModHelper.Provide(state).Any(), "ending notifications ignored; early history fences retained");
            manager.IsOverOrEnding = false;
            next = Observer(state); FireAll(next);
            NativeCombatHooks.Stop(); NativeCombatHooks.FlushPending();
            Check(!ModHelper.Provide(state).Any() && calls == before, "stop drops queued notification and disables retained provider");
            NativeCombatHooks.Start();
            Check(ModHelper.Registrations == 1, "restart does not register a duplicate provider");
            var restarted = Observer(state); FireAll(next); NativeCombatHooks.FlushPending();
            Check(calls == before, "old observer rejected after restart with same state and ID");
            FireAll(restarted); NativeCombatHooks.FlushPending(); Check(calls == before + 1, "restart works");
            before = calls;
            NativeCombatHooks.ForgetCombat(); FireAll(restarted); NativeCombatHooks.FlushPending();
            Check(calls == before, "reset drops old observer and pending work");
            var replacementManager = new CombatManager { IsInProgress = true, CurrentCombatId = new(2) };
            var old = Observer(state); CombatManager.Instance = replacementManager;
            FireAll(old); NativeCombatHooks.FlushPending(); Check(calls == before, "manager replacement fenced despite equal ID");

            // The capability audit exempts exactly our sealed observer, not arbitrary mods
            // and not every victory hook or every type with an allowed simple name.
            var warnings = new List<string>();
            var fresh = Observer(state);
            CapabilityAudit.Inspect([fresh], warnings.Add);
            Check(warnings.Count == 0 && !CapabilityAudit.HasUnresolvedDeathReaction([fresh], new()), "audited observer does not make all forecasts uncertain");
            CapabilityAudit.Inspect([new MegaCrit.Sts2.Core.Models.Relics.BurningBlood()], warnings.Add);
            Check(warnings.Count == 0, "audited post-victory-only heal outside forecast");
            CapabilityAudit.Inspect([new MegaCrit.Sts2.Core.Models.Relics.UnknownVictoryRelic()], warnings.Add);
            Check(warnings.Count == 1, "unrecognized victory effect remains uncertain");
            warnings.Clear(); CapabilityAudit.Inspect([new External.BurningBlood()], warnings.Add);
            Check(warnings.Count == 1 && CapabilityAudit.HasUnresolvedDeathReaction([new External.BurningBlood()], new()), "external names cannot spoof allowlist");
            NativeCombatHooks.StateChanged -= Count;
            NativeCombatHooks.Stop(); NativeCombatHooks.Start(); ModelDb.Canonical = null;
            Check(!ModHelper.Provide(state).Any(), "missing ModelDb fails closed without constructing gameplay models");
            Check(ModLog.Errors == 1, "provider failure logging deferred outside native dispatch");
            NativeCombatHooks.FlushPending(); NativeCombatHooks.FlushPending();
            Check(ModLog.Errors == 2, "failure logged once; no hot-loop logging");
            NativeCombatHooks.Start(); Check(!ModHelper.Provide(state).Any(), "faulted observer disabled; original event fallback unaffected");
            Console.WriteLine($"PASS {checks} native hook lifecycle/readonly/coalescing/receiver-coverage checks (source-linked doubles, not a multiplayer session).");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("FAIL " + ex); return 1; }
        finally { NativeCombatHooks.Stop(); }
    }
}
