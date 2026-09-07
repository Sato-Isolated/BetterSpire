using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterSpire2.Journal.Core;
using BetterSpire2.DamageMeter.Core;

internal static class PoisonTests
{
    private static int _checks;
    private static void Check(bool condition, string name)
    {
        _checks++;
        if (!condition) throw new Exception(name);
    }
    private static long Amount(PoisonDamageCredit credit, long damage, string id) =>
        credit.Allocate(damage).Where(s => s.PlayerId == id).Sum(s => s.Amount);
    private static JournalSession Session()
    {
        var s = new JournalSession();
        s.Attach(new RunJournal { Key = "test", DamageMeterCoverageVersion = 1 });
        s.RegisterPlayer(new JournalPlayer { Id = "a", Name = "Same name", IsLocal = true });
        s.RegisterPlayer(new JournalPlayer { Id = "b", Name = "Same name" });
        s.BeginCombat("fight1", "Test", 1, 1, false);
        return s;
    }
    public static async Task Main()
    {
        var solo = new PoisonStackPool();
        solo.ObserveChange("a", 10);
        var frozen = solo.Capture(10);
        Check(Amount(frozen, 10, "a") == 10, "all poison belongs to solo applier");
        Check(Amount(frozen, 3, "a") == 3, "lethal damage uses real HP, not ten stacks");
        Check(Amount(frozen, 20, "a") == 20, "damage modifiers use actual result");
        solo.ObserveChange("b", 10);
        Check(Amount(frozen, 10, "a") == 10, "tick snapshot cannot mutate after another application");

        var pool = new PoisonStackPool();
        pool.ObserveChange("a", 6); pool.ObserveChange("b", 4);
        var tick = pool.Capture(10);
        Check(Amount(tick, 10, "a") == 6 && Amount(tick, 10, "b") == 4, "6/4 coop contribution");
        Check(Amount(tick, 3, "a") == 2 && Amount(tick, 3, "b") == 1, "lethal shared tick conserves three HP");
        pool.ObserveChange(null, -1);
        tick = pool.Capture(9);
        Check(Amount(tick, 9, "a") == 5 && Amount(tick, 9, "b") == 4, "fractional decay; integer damage only");
        pool.ObserveChange("b", 3);
        tick = pool.Capture(12);
        Check(Amount(tick, 12, "a") == 5 && Amount(tick, 12, "b") == 7, "new application does not steal older contribution");
        pool.ObserveChange(null, -100);
        Check(pool.Total == 0, "cure/removal clears shares");
        pool.ObserveChange("b", 4);
        Check(Amount(pool.Capture(4), 4, "a") == 0, "reapplication cannot retain previous owner");

        var unknown = new PoisonStackPool();
        unknown.ObserveChange("a", 4);
        tick = unknown.Capture(10);
        Check(Amount(tick, 10, "a") == 4 && Amount(tick, 10, "") == 6, "pre-existing stacks remain unknown");
        unknown.ObserveChange(null, -1);
        tick = unknown.Capture(9);
        Check(tick.Allocate(9).Sum(s => s.Amount) == 9, "unknown share participates in decay");
        Check(new PoisonStackPool().Capture(0).Allocate(5).Single() == new PoisonDamageShare("", 5),
            "empty provenance is not a local-player fallback");

        var rejected = new PoisonStackPool();
        rejected.ObserveChange("a", 0); rejected.ObserveChange("a", 0.9m);
        Check(rejected.Total == 0, "zero and truncated fractional applications cannot create damage");
        rejected.ObserveChange("a", 3.9m); rejected.ObserveChange(null, -1.9m);
        Check(rejected.Total == 2, "same signed truncation as native stack changes");
        rejected.ObserveChange("b", decimal.MaxValue);
        Check(rejected.Total == int.MaxValue, "huge additions bounded");
        Check(rejected.Capture(int.MaxValue).Allocate(long.MaxValue).Sum(s => s.Amount) == long.MaxValue,
            "exact large-damage apportionment without overflow");
        rejected.ObserveChange(null, decimal.MinValue);
        Check(rejected.Total == 0, "large removal bounded");

        var ties = new PoisonStackPool();
        ties.ObserveChange("b", 1); ties.ObserveChange("a", 1);
        Check(Amount(ties.Capture(2), 1, "a") == 1, "stable ID tie, not apply order or display name");
        var independent = new PoisonStackPool(); independent.ObserveChange("b", 7);
        Check(Amount(independent.Capture(7), 7, "a") == 0, "different enemy/power uses a different pool");

        var bounded = new PoisonStackPool();
        for (int i = 0; i < 300; i++) bounded.ObserveChange("p" + i, 1);
        Check(bounded.Capture(300).Allocate(300).Count <= PoisonStackPool.MaxContributors + 1,
            "bounded contributor cardinality");
        Check(Amount(bounded.Capture(300), 300, "") > 0, "overflow provenance stays unknown");

        // One native history event -> journal -> both live indices, not a second collector.
        var session = Session();
        var shared = new PoisonStackPool(); shared.ObserveChange("a", 6); shared.ObserveChange("b", 4);
        var credit = shared.Capture(10);
        credit.Append(session, 1, JournalPhase.Enemy, Stat.DamageDealtHp, 3);
        credit.Append(session, 1, JournalPhase.Enemy, Stat.Overkill, 7);
        var meter = DamageMeterBuilder.BuildLive(session, false, DamageMeterScope.Combat);
        Check(meter.AttributedDamage == 3 && meter.UnattributedDamage == 0, "no overkill inflation in meter");
        Check(meter.Rows.Single(r => r.Id == "a").Damage == 2 && meter.Rows.Single(r => r.Id == "b").Damage == 1,
            "both players receive their poison in meter");
        Check(session.Query(JournalScope.Combat, "fight1", 1, "a").Sources["effect:poison"].Name == "Poison",
            "poison source visible in journal");
        session.AppendUnattributed(1, Stat.DamageDealtHp, 5);
        Check(DamageMeterBuilder.BuildLive(session, false).UnattributedDamage == 5,
            "unrelated source-less effects remain separate");
        session.Complete(CombatOutcome.Won);
        Check(DamageMeterBuilder.BuildLive(session, false).AttributedDamage == 3, "lethal tick retained on seal");
        session.BeginCombat("fight2", "Next", 1, 2, false);
        Check(DamageMeterBuilder.BuildLive(session, false, DamageMeterScope.Combat).AttributedDamage == 0,
            "new fight resets fight damage only");
        Check(DamageMeterBuilder.BuildLive(session, false).AttributedDamage == 3, "run retains previous poison");
        session.BeginCombat("fight1", "Replay", 1, 1, false);
        Check(DamageMeterBuilder.BuildLive(session, false).AttributedDamage == 0, "replay cannot double an old poison tick");

        // Unknown player IDs are not silently discarded or assigned to the host.
        var missing = new PoisonStackPool(); missing.ObserveChange("not-registered", 10);
        missing.Capture(10).Append(session, 1, JournalPhase.Enemy, Stat.DamageDealtHp, 10);
        Check(session.QueryUnattributed(JournalScope.Run, null, 0)[Stat.DamageDealtHp] == 10,
            "unregistered applier retained as unattributed damage");
        bool threw = false;
        try { credit.Append(session, 1, JournalPhase.Enemy, Stat.Healing, 3); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Check(threw, "poison cannot write unrelated statistics");

        var random = new Random(3501);
        for (int test = 0; test < 3000; test++)
        {
            var p = new PoisonStackPool();
            for (int change = 0; change < 30; change++)
            {
                string? id = random.Next(5) == 0 ? null : "p" + random.Next(4);
                p.ObserveChange(id, random.Next(-15, 31));
                long damage = random.NextInt64(0, long.MaxValue);
                var allocated = p.Capture(p.Total).Allocate(damage);
                Check(allocated.Sum(s => s.Amount) == damage, "random conservation");
                Check(allocated.All(s => s.Amount > 0), "no negative or zero shares");
            }
        }
        await AsyncIsolation();
        Console.WriteLine($"PASS {_checks} C# poison provenance, allocation, journal and async isolation checks.");
    }

    private static readonly OneShotDamageScope<string> Scope = new();
    // Mimics a Harmony prefix / native async method / synchronous finalizer.
    private static Task<T> Command<T>(Func<Task<T>> body)
    {
        using var command = Scope.EnterCommand();
        return body();
    }
    private static Task<T> Poison<T>(string source, Func<Task<T>> body)
    {
        using var request = Scope.RequestNext(source);
        return Command(body);
    }
    private static async Task AsyncIsolation()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string?> a = Poison("a", async () =>
        {
            Check(Scope.Current == "a", "poison scope on synchronous command entry");
            await release.Task;
            Check(Scope.Current == "a", "poison scope survives suspension");
            string? nested = await Command(async () => { await Task.Yield(); return Scope.Current; });
            Check(nested == null && Scope.Current == "a", "nested source-less damage masks poison across await");
            string? otherPoison = await Poison("b", async () => { await Task.Yield(); return Scope.Current; });
            Check(otherPoison == "b" && Scope.Current == "a", "nested poison has its own owner");
            return Scope.Current;
        });
        Check(Scope.Current == null, "caller restored before asynchronous damage finishes");
        var unrelated = Command(async () => { await Task.Yield(); return Scope.Current; });
        release.SetResult();
        Check(await a == "a" && await unrelated == null, "unrelated concurrent damage never inherits poison");
        var values = await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Poison(i.ToString(), async () =>
        { await Task.Yield(); return Scope.Current; })));
        Check(values.SequenceEqual(Enumerable.Range(0, 40).Select(i => i.ToString())), "parallel source isolation");
        try { await Poison<int>("a", async () => { await Task.Yield(); throw new InvalidOperationException("native"); }); }
        catch (InvalidOperationException ex) { Check(ex.Message == "native", "native asynchronous exception preserved"); }
        Check(Scope.Current == null, "no leak after asynchronous exception");
        try { Poison<int>("b", () => throw new InvalidOperationException("sync")); }
        catch (InvalidOperationException) { }
        Check(Scope.Current == null, "no leak after synchronous exception");
        using (Scope.RequestNext("a"))
        {
            Check(await Command(() => Task.FromResult(Scope.Current)) == "a", "request claimed once");
            Check(await Command(() => Task.FromResult(Scope.Current)) == null, "request not reused for a second command");
        }
    }
}
