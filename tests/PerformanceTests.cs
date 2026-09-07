#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BetterSpire2.Runtime;
using BetterSpire2.Journal.Core;
using BetterSpire2.DamageMeter.Core;

internal static class PerformanceTests
{
    private static int _checks;
    private static void Check(bool condition, string label)
    { _checks++; if (!condition) throw new Exception("FAIL: " + label); }
    private static JournalSession Make()
    {
        var session = new JournalSession();
        session.Attach(new RunJournal { Key = "performance-test", DamageMeterCoverageVersion = 1 });
        for (int i = 0; i < 4; i++)
            session.RegisterPlayer(new JournalPlayer { Id = "p" + i, Name = "Player " + i, IsLocal = i == 0 });
        session.BeginCombat("fight-1", "test", 1, 1, false);
        return session;
    }
    private static void Compare(JournalSession session, string label)
    {
        foreach (bool blocked in new[] { false, true })
        {
            var reference = DamageMeterBuilder.Build(session.Run, blocked);
            var indexed = DamageMeterBuilder.BuildLive(session, blocked);
            Check(reference.AttributedDamage == indexed.AttributedDamage, label + " attributed total");
            Check(reference.UnattributedDamage == indexed.UnattributedDamage, label + " unattributed total");
            Check(reference.Rows.SequenceEqual(indexed.Rows), label + " ranks / shares / rows");
            Check(reference.IsPartial == indexed.IsPartial, label + " partial coverage");
            var fight = DamageMeterBuilder.BuildLive(session, blocked, DamageMeterScope.Combat);
            foreach (var row in fight.Rows)
            {
                decimal expected = 0;
                if (session.Active != null)
                    foreach (var round in session.Active.Rounds)
                        if (round.Players.TryGetValue(row.Id, out var player))
                            expected += (decimal)player.Totals[Stat.DamageDealtHp] +
                                (blocked ? player.Totals[Stat.DamageDealtBlocked] : 0);
                Check(row.Damage == expected, label + " combat index " + row.Id);
            }
            decimal unassigned = 0;
            if (session.Active != null)
                foreach (var round in session.Active.Rounds)
                    unassigned += (decimal)round.UnattributedDamage[Stat.DamageDealtHp] +
                        (blocked ? round.UnattributedDamage[Stat.DamageDealtBlocked] : 0);
            Check(fight.UnattributedDamage == unassigned, label + " combat unassigned");
        }
    }
    private static void GateTests()
    {
        var gate = new RefreshGate();
        Check(gate.IsDirty && gate.IsDue(0), "first capture immediate");
        gate.Complete(0, gate.Version);
        for (ulong now = 100; now < 2000; now += 100) Check(!gate.IsDue(now), "stable state does not recompute at " + now);
        Check(gate.IsDue(2000), "bounded two-second fallback");
        gate.Invalidate();
        Check(!gate.IsDue(99) && gate.IsDue(100), "gameplay dirty capture uses 100ms gate");
        for (int i = 0; i < 10000; i++) gate.Invalidate();
        gate.Complete(100, gate.Version);
        Check(!gate.IsDirty && !gate.IsDue(200), "10000 signals coalesce into one capture");
        gate.Invalidate(); long before = gate.Version; gate.Invalidate(); gate.Complete(200, before);
        Check(gate.IsDirty && gate.IsDue(300), "notification during capture remains pending");
        gate.Fail(300); gate.Invalidate();
        Check(!gate.IsDue(1299) && gate.IsDue(1300), "error retry cannot be bypassed by more signals");
        gate.Reset(); Check(gate.IsDue(1301), "new combat resets backoff");
        bool rejected = false;
        try { _ = new RefreshGate(100, 99); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "invalid intervals rejected");
        gate = new RefreshGate(); int captures = 0;
        for (ulong t = 0; t < 60000; t += 100)
            if (gate.IsDue(t)) { captures++; gate.Complete(t, gate.Version); }
        Check(captures == 30, "stable 60s schedule: 30 captures, not an FPS benchmark");
    }
    private static void IndexTests()
    {
        var session = Make(); var random = new Random(3499);
        Stat[] metrics = { Stat.DamageDealtHp, Stat.DamageDealtBlocked, Stat.PetDamageDealtHp, Stat.Overkill,
            Stat.CardsPlayed, Stat.Healing, Stat.HpLost };
        for (int i = 0; i < 6000; i++)
        {
            int round = random.Next(20); Stat metric = metrics[random.Next(metrics.Length)]; long amount = random.Next(1, 800);
            if (i % 7 == 0) session.AppendUnattributed(round, metric, amount);
            else session.Append(new JournalEvent("p" + random.Next(4), round, JournalPhase.Player, metric, amount));
            if (i % 53 == 0) Compare(session, "event " + i);
        }
        session.Append(new JournalEvent("p0", 1, JournalPhase.Player, Stat.DamageDealtHp, long.MaxValue));
        session.Append(new JournalEvent("p0", 1, JournalPhase.Player, Stat.DamageDealtHp, long.MaxValue));
        session.AppendUnattributed(1, Stat.DamageDealtBlocked, long.MaxValue);
        session.AppendUnattributed(1, Stat.DamageDealtBlocked, long.MaxValue);
        Compare(session, "bucket saturation");
        session.Complete(CombatOutcome.Won);
        session.BeginCombat("fight-2", "second", 1, 2, false);
        session.Append(new JournalEvent("p2", 1, JournalPhase.Player, Stat.DamageDealtHp, 25));
        Compare(session, "next fight");
        session.BeginCombat("fight-1", "test", 1, 1, false);
        session.Append(new JournalEvent("p0", 1, JournalPhase.Player, Stat.DamageDealtHp, 9));
        Check(session.Run!.Combats.Count == 1, "replay drops obsolete branch");
        Compare(session, "replayed fight replaces old damage");
        Check(DamageMeterBuilder.BuildLive(session, false).AttributedDamage == 9, "replay not accumulated");
        var restored = new JournalSession();
        restored.Attach(JsonSerializer.Deserialize<RunJournal>(JsonSerializer.Serialize(session.Run))!);
        Compare(restored, "deserialized run index");
        foreach (Stat metric in metrics)
            Check(session.QueryTotals(JournalScope.Run, null, 0, null)[metric] ==
                session.Query(JournalScope.Run, null, 0, null).Totals[metric], "shallow summary matches full merge " + metric);
        session.Attach(new RunJournal { Key = "new-run" }); Compare(session, "new run reset");
    }
    private static void CheckpointTests()
    {
        var session = Make();
        session.Append(new JournalEvent("p0", 1, JournalPhase.Player, Stat.DamageDealtHp, 10, "card:a", "Strike", true));
        var checkpoints = new JournalCheckpoint(); var snapshot = checkpoints.Capture(session.Run!);
        string before = JsonSerializer.Serialize(snapshot);
        session.Append(new JournalEvent("p0", 1, JournalPhase.Player, Stat.DamageDealtHp, 50, "card:a", "Strike", true));
        session.RegisterPlayer(new JournalPlayer { Id = "p0", Name = "Renamed", IsLocal = true });
        Check(JsonSerializer.Serialize(snapshot) == before, "detached active totals, sources and names");
        var next = checkpoints.Capture(session.Run!);
        Check(next.Combats[0].Rounds[0].Players["p0"].Totals[Stat.DamageDealtHp] == 60, "fresh active copy");
        session.Complete(CombatOutcome.Won); var finished = checkpoints.Capture(session.Run!);
        var reused = checkpoints.Capture(session.Run!);
        Check(ReferenceEquals(finished.Combats[0], reused.Combats[0]), "sealed fight copy reused");
        session.MarkPartial(); var partial = checkpoints.Capture(session.Run!);
        Check(partial.Combats[0].Partial && !finished.Combats[0].Partial, "completed metadata change reclones without mutating earlier snapshot");
        session.BeginCombat("fight-1", "replay", 1, 1, false);
        var replay = checkpoints.Capture(session.Run!);
        Check(replay.Combats[0].Rounds.Count == 0 && finished.Combats[0].Rounds.Count == 1, "rollback prunes checkpoint cache");
        string dir = Path.Combine(Path.GetTempPath(), "betterspire-checkpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var archive = new JournalArchive(Path.Combine(dir, "journal.json"));
            var writing = Task.Run(() => { for (int i = 0; i < 10; i++) archive.Save(snapshot); });
            for (int i = 0; i < 3000; i++)
                session.Append(new JournalEvent("p0", 1, JournalPhase.Player, Stat.DamageDealtHp, 1));
            writing.GetAwaiter().GetResult();
            Check(JsonSerializer.Serialize(archive.Load()) == before, "background serialization sees only detached data");
            Check(!File.Exists(archive.FilePath + ".tmp"), "writer left no temporary file");
        }
        finally { Directory.Delete(dir, true); }
    }
    private static void Main()
    {
        GateTests(); IndexTests(); CheckpointTests();
        Console.WriteLine($"PASS {_checks} C# refresh/index/checkpoint assertions. No game rendering or FPS benchmark.");
    }
}
