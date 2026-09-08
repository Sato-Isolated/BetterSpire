using System;
using System.Linq;
using System.Reflection;
using BetterSpire2.Guardian.Game;
using BetterSpire2.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.TestSupport;

internal static class GameBoundaryTests
{
    private static int _checks;
    private static int Main()
    {
        try
        {
            Ordering();
            VictoryTarget();
            Lifecycle();
            Observer();
            Console.WriteLine($"PASS {_checks} source-linked game-boundary checks (test doubles, not in-game).");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("FAIL " + ex); return 1; }
        finally { CombatLifecycle.Stop(); }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); _checks++; }

    private static void Ordering()
    {
        string[] party = ["A", "B", "C"];
        Check(ForecastTurnOrder.Participants(party, _ => true).SequenceEqual(party), "normal turn keeps party order");
        Check(ForecastTurnOrder.Participants(party, p => p == "B").SequenceEqual(new[] { "B" }), "only extra-turn participant");
        Check(ForecastTurnOrder.Participants(party, _ => false).Length == 0, "no player-side participants");
        Check(ForecastTurnOrder.AttackHits(new[] { "A", "B" }, 2).SequenceEqual(
            new[] { ("A", 0), ("B", 0), ("A", 1), ("B", 1) }), "hits precede targets");
        Check(!ForecastTurnOrder.AttackHits(party, 0).Any(), "zero hits");
        Check(!ForecastTurnOrder.AttackHits(Array.Empty<string>(), 3).Any(), "no targets");
        bool rejected = false;
        try { _ = ForecastTurnOrder.AttackHits(party, -1).ToArray(); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "negative hit count rejected");
    }

    private static void VictoryTarget()
    {
        var patch = typeof(BetterSpire2.Patches.Combat.CombatManager_EndCombatInternal_Patch);
        var target = (MethodBase)patch.GetMethod("TargetMethod", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        Check(target.IsPrivate && target.GetParameters().Length == 1 &&
            target.GetParameters()[0].ParameterType == typeof(CombatTurnState), "resolver selects runtime overload, not wrapper");
        int endings = JournalController.Endings, speedEnds = InstantSpeedHelper.Ends;
        patch.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        Check(JournalController.Endings == endings + 1 && InstantSpeedHelper.Ends == speedEnds + 1,
            "early victory prefix drains journal and restores speed");
    }

    private static void Lifecycle()
    {
        CombatLifecycle.Stop();
        var manager = CombatManager.Instance = new();
        CombatLifecycle.EnsureAttached();
        CombatLifecycle.EnsureAttached();
        Check(manager.SetupSubscribers == 1, "one native lifecycle subscription");
        var room = new CombatRoom();
        var state = new CombatState(); state.RunState.CurrentRoom = room;
        int setups = DamageTracker.Setups;
        InstantSpeedHelper.ThrowOnStart = true;
        manager.SetUp(state, 1);
        InstantSpeedHelper.ThrowOnStart = false;
        Check(DamageTracker.Setups == setups + 1 && JournalController.Setups > 0, "setup failure isolated and reentry idempotent");
        Check(ReferenceEquals(CombatLifecycle.CurrentState, state), "setup state is available before IsInProgress");
        int setupEnds = InstantSpeedHelper.Ends;
        manager.PublishOldEnd(room);
        Check(InstantSpeedHelper.Ends == setupEnds && CombatLifecycle.CurrentState == state,
            "delayed same-room end cannot tear down a new combat during setup");
        manager.Begin();
        Check(manager.DebugReads == 0, "ordinary native lifecycle never reads debug state");
        int ends = InstantSpeedHelper.Ends;
        manager.PublishOldEnd(new CombatRoom());
        Check(InstantSpeedHelper.Ends == ends && CombatLifecycle.CurrentState == state, "old end cannot hide live combat");
        manager.End(room);
        Check(InstantSpeedHelper.Ends == ends + 1 && CombatLifecycle.CurrentState == null, "native end restores speed and forgets state");
        manager.SetUp(state, 2); manager.Begin();
        manager.CurrentCombatId = new(3);
        Check(CombatLifecycle.CurrentState == null, "identity mismatch rejects cached state");
        CombatLifecycle.Stop();
        Check(manager.SetupSubscribers == 0, "lifecycle detaches on stop");
        manager.State = state; manager.IsInProgress = true; manager.IsOverOrEnding = false;
        CombatLifecycle.EnsureAttached();
        Check(manager.DebugReads == 1 && CombatLifecycle.CurrentState == state, "late initialization bootstraps once");
        CombatLifecycle.EnsureAttached();
        Check(manager.DebugReads == 1, "no repeated debug polling");
        CombatLifecycle.ForgetCombat();
        Check(CombatLifecycle.CurrentState == null, "reset forgets state without leaking it");
        CombatLifecycle.Stop();
    }

    private static void Observer()
    {
        var manager = CombatManager.Instance = new();
        var state = new CombatState();
        var creature = new Creature { Monster = new MonsterModel { NextMove = new() } };
        var player = new Player();
        var card = new CardModel(); var relic = new RelicModel();
        player.PlayerCombatState!.Hand.Cards.Add(card); player.Relics.Add(relic);
        state.Creatures.Add(creature); state.Players.Add(player);
        manager.SetUp(state, 10); manager.Begin();
        int changes = 0;
        TestMode.IsOn = false;
        using var observer = new ForecastEventObserver(() => changes++);
        observer.Observe(state);
        Check(manager.StateTracker.SubscriberCount == 1 && manager.ReadySubscribers == 1 && manager.UndoSubscribers == 1,
            "native state and both ready events attached");
        int before = changes;
        creature.ChangeHp(10, 9);
        Check(changes == before + 1, "immediate stale fence before deferred tracker");
        before = changes; card.ChangeCost(); card.Forge();
        Check(changes == before + 2, "cost and forge invalidate immediately");
        before = changes; manager.StateTracker.Publish(state);
        Check(changes == before + 1, "native deferred notification invalidates");
        int generation = observer.BindingGeneration;
        for (int i = 0; i < 100; i++)
        { manager.StateTracker.Publish(state); observer.Observe(state); }
        Check(observer.BindingGeneration == generation, "value notifications never rebuild delegates");
        var replacement = new CardModel();
        player.PlayerCombatState!.Hand.Cards[0] = replacement;
        manager.StateTracker.Publish(state); observer.Observe(state);
        Check(observer.BindingGeneration == generation + 1, "same-size native hand replacement rebinds");
        before = changes; card.Forge(); replacement.Forge();
        Check(changes == before + 1, "old card detached and replacement observed");
        card = replacement;
        var replacementRelic = new RelicModel();
        player.Relics[0] = replacementRelic;
        manager.StateTracker.Publish(state); observer.Observe(state);
        Check(observer.BindingGeneration == generation + 2, "same-size relic replacement rebinds");
        relic = replacementRelic;
        before = changes; manager.StateTracker.Publish(new CombatState());
        Check(changes == before, "old state notification ignored");
        manager.CurrentCombatId = new(11); manager.StateTracker.Publish(state);
        Check(changes == before, "old combat identity ignored");
        manager.CurrentCombatId = new(10); manager.IsOverOrEnding = true; manager.StateTracker.Publish(state);
        Check(changes == before, "ending guard separate from live identity");
        manager.IsOverOrEnding = false;
        manager.Ready(player); manager.Undo(player);
        Check(changes == before + 2, "ready and undo invalidate");
        before = changes; relic.ChangeStatus();
        Check(changes == before + 1, "relic supplementation retained");
        before = changes; creature.Monster!.NextMove = new(); observer.Observe(state);
        Check(changes == before + 1, "move identity supplementation retained");
        observer.Dispose(); before = changes;
        manager.StateTracker.Publish(state); manager.Ready(player); manager.Undo(player); card.Forge(); creature.ChangeHp(9, 8);
        Check(changes == before && manager.StateTracker.SubscriberCount == 0 && manager.ReadySubscribers == 0 && manager.UndoSubscribers == 0,
            "dispose detaches native and direct signals");
        TestMode.IsOn = true;
        observer.Observe(state);
        Check(manager.StateTracker.SubscriberCount == 0, "backend TestMode must not subscribe to native UI tracker");
        observer.Dispose(); TestMode.IsOn = false;
    }
}
