#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterSpire2.Journal.Core;

internal static class JournalTests
{
    private static int _checks;
    private static void Check(bool value, string name)
    { _checks++; if (!value) throw new Exception("FAIL: " + name); }
    private static void Equal(long expected, long actual, string name) => Check(expected == actual, $"{name}: expected {expected}, got {actual}");
    private static JournalSession Make(string key = "run-A")
    {
        var s = new JournalSession();
        s.Attach(new RunJournal { Key = key, Seed = "SEED", GameStartTime = 123 });
        s.RegisterPlayer(new JournalPlayer { Id = "p1", Name = "Local", IsLocal = true });
        s.RegisterPlayer(new JournalPlayer { Id = "p2", Name = "Ally" });
        s.BeginCombat("c1", "Enemy", 1, 1, false);
        return s;
    }
    private static JournalEvent E(Stat stat, long amount, int round = 1, string id = "p1", JournalPhase phase = JournalPhase.Player)
        => new(id, round, phase, stat, amount);
    private static long Q(JournalSession s, Stat stat, JournalScope scope = JournalScope.Combat, string? key = "c1", int round = 1, string? player = "p1")
        => s.Query(scope, key, round, player).Totals[stat];
    private static void Main()
    {
        var s = Make();
        s.Append(E(Stat.BlockGained, 5, 0, phase: JournalPhase.Setup));
        s.Append(E(Stat.BlockGained, 10));
        s.Append(E(Stat.HpLost, 8, phase: JournalPhase.Enemy));
        s.Append(E(Stat.Healing, 3));
        s.Append(E(Stat.DamageBlocked, 12));
        s.Append(E(Stat.PetHpLost, 9));
        s.Append(E(Stat.HpLost, 30, id: "p2"));
        Equal(8, Q(s, Stat.HpLost), "HP lost not reduced by healing or pets");
        Equal(9, Q(s, Stat.PetHpLost), "pet HP separate");
        Equal(12, Q(s, Stat.DamageBlocked), "blocked damage not HP loss");
        Equal(38, Q(s, Stat.HpLost, player: null), "explicit team aggregate");
        Equal(8, Q(s, Stat.HpLost, JournalScope.Round), "enemy phase same round");
        Equal(10, Q(s, Stat.BlockGained, JournalScope.Round), "setup excluded from round one");
        Equal(5, Q(s, Stat.BlockGained, JournalScope.Round, round: 0), "setup separately queryable");
        Equal(15, Q(s, Stat.BlockGained), "combat includes setup");
        s.Append(E(Stat.HpLost, -8)); s.Append(E(Stat.HpLost, 100, id: "unregistered"));
        Equal(8, Q(s, Stat.HpLost), "invalid observations ignored");
        var copy = s.Query(JournalScope.Combat, "c1", 1, "p1"); copy.Totals.Add(Stat.HpLost, 1000);
        Equal(8, Q(s, Stat.HpLost), "queries cannot mutate original data");
        s.Complete(CombatOutcome.Won);
        s.Append(E(Stat.HpLost, 100)); s.Complete(CombatOutcome.Interrupted);
        Equal(8, Q(s, Stat.HpLost), "sealed combat rejects late observations");
        Check(s.Active!.Outcome == CombatOutcome.Won, "teardown preserves win");
        s.BeginCombat("c2", "Boss", 1, 2, false); s.Append(E(Stat.HpLost, 4));
        Equal(12, Q(s, Stat.HpLost, JournalScope.Run), "run spans multiple combats");
        Equal(4, Q(s, Stat.HpLost, key: "c2"), "combat scopes remain distinct");
        s.BeginCombat("c2", "Boss", 1, 2, false); s.Append(E(Stat.HpLost, 2));
        Equal(10, Q(s, Stat.HpLost, JournalScope.Run), "reload replaces old attempt");
        Equal(2, s.Run!.Combats.Count, "reload does not duplicate combat");
        Equal(1, s.Active!.ReplacedAttempts, "replacement counted for explanation");
        s.BeginCombat("c1", "Enemy", 1, 1, false);
        Equal(1, s.Run.Combats.Count, "save rollback drops future branch");
        Equal(0, Q(s, Stat.HpLost, JournalScope.Run), "no ghost damage after rollback");
        s.BeginCombat("same-floor-second", "Second encounter", 1, 1, false);
        Equal(2, s.Run.Combats.Count, "distinct rooms on same floor retained");
        s.Attach(new RunJournal { Key = "new-run-same-seed", Seed = "SEED" });
        Equal(0, Q(s, Stat.HpLost, JournalScope.Run), "new run resets totals even on same seed");
        s = Make();
        var cursor = new HistoryCursor();
        int visits = 0;
        Check(cursor.Consume(4, _ => { visits++; s.Append(E(Stat.CardsPlayed, 1)); }), "first history import");
        cursor.Consume(4, _ => visits++); cursor.Consume(6, _ => { visits++; s.Append(E(Stat.CardsPlayed, 1)); });
        Equal(6, visits, "repeated refresh reads only appended entries");
        Equal(6, Q(s, Stat.CardsPlayed), "no double counted cards");
        Check(!cursor.Consume(2, _ => throw new Exception("must not revisit")), "unexpected history shrink detected");
        cursor.Reset(); Equal(0, cursor.Processed, "new combat resets cursor");
        var selection = new JournalSelection(); selection.Sync(s.Run);
        Check(selection.PlayerId == "p1", "local player default, not team");
        s.ObserveRound(2); selection.Live(s.Run); selection.SelectScope(JournalScope.Round); selection.Move(s.Run, -1);
        Equal(1, selection.Round, "previous round navigation");
        s.ObserveRound(3); selection.Sync(s.Run);
        Equal(1, selection.Round, "historical selection stays pinned");
        selection.Live(s.Run); Equal(3, selection.Round, "explicit Live resumes following");
        selection.Move(s.Run, 1); Equal(3, selection.Round, "no future-round navigation");
        selection.CyclePlayer(s.Run); Check(selection.PlayerId == "p2", "ally selection");
        selection.CyclePlayer(s.Run); Check(selection.PlayerId == null, "team explicitly selectable");
        selection.CyclePlayer(s.Run); Check(selection.PlayerId == "p1", "cycle back to local");
        selection.SelectScope(JournalScope.Combat);
        var fr = JournalReportBuilder.Build(s, selection, false, 999, true);
        Check(fr.Context.Contains("Combat"), "French report");
        Check(fr.Page < fr.Pages && fr.Rows.Count <= 6, "detail pages clamped");
        var empty = JournalReportBuilder.Build(new JournalSession(), new JournalSelection(), false, 0, true);
        Check(empty.HeroValues.All(v => v == "—"), "no fake zeros without observations");
        s.MarkPartial(); fr = JournalReportBuilder.Build(s, selection, false, 0, true);
        Check(fr.Note.Contains("incomplet"), "partial capture disclosed inside journal");
        var line = new StatLine(); line.Add(Stat.DamageDealtHp, long.MaxValue); line.Add(Stat.DamageDealtHp, 50);
        Equal(long.MaxValue, line[Stat.DamageDealtHp], "saturating counters");
        for (int i = 0; i < 1000; i++) s.Append(new("p1", 1, JournalPhase.Player, Stat.CardsPlayed, 1, "card:" + i, "Card " + i, true));
        Check(s.Query(JournalScope.Combat, "c1", 0, "p1").Sources.Count <= 257, "source cache bounded");
        Equal(1006, Q(s, Stat.CardsPlayed), "bounded source list does not lose aggregate totals");
        selection.SelectScope(JournalScope.Run);
        var runReport = JournalReportBuilder.Build(s, selection, false, 0, false);
        Check(runReport.Rows[0].CombatKey == "c1", "run rows navigate to combat");
        var runDetails = JournalReportBuilder.Build(s, selection, false, 0, false, runDetails: true);
        Check(runDetails.Rows[0].Label.Contains("Block gained") && runDetails.Rows.All(row => row.CombatKey == null),
            "run-wide detailed metrics accessible independently of combat history");
        selection.SelectCombat(s.Run!, "c1");
        Check(selection.Scope == JournalScope.Combat && !selection.FollowLive, "clicking fight selects fixed fight");
        // Randomized scope conservation tests against a direct event sum.
        var rng = new Random(7321); s = Make();
        var events = new List<JournalEvent>();
        for (int i = 0; i < 4000; i++)
        {
            var e = E((Stat)rng.Next(Enum.GetValues<Stat>().Length), rng.Next(1, 50), rng.Next(0, 15), rng.Next(2) == 0 ? "p1" : "p2");
            events.Add(e); s.Append(e);
        }
        foreach (Stat stat in Enum.GetValues<Stat>())
        {
            Equal(events.Where(e => e.Metric == stat).Sum(e => e.Amount), Q(s, stat, player: null), "team conservation " + stat);
            foreach (string id in new[] { "p1", "p2" })
            {
                Equal(events.Where(e => e.Metric == stat && e.PlayerId == id).Sum(e => e.Amount), Q(s, stat, player: id), "player conservation " + stat);
                for (int round = 0; round < 15; round++)
                    Equal(events.Where(e => e.Metric == stat && e.PlayerId == id && e.Round == round).Sum(e => e.Amount),
                        Q(s, stat, JournalScope.Round, round: round, player: id), "round conservation " + stat);
            }
        }
        string dir = Path.Combine(Path.GetTempPath(), "betterspire-journal-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var archive = new JournalArchive(Path.Combine(dir, "journal.json"));
            archive.Save(s.Run!);
            var loaded = archive.Load(); Check(loaded?.Key == s.Run!.Key, "disk round trip identity");
            var restored = new JournalSession(); restored.Attach(loaded!);
            Equal(Q(s, Stat.HpLost), Q(restored, Stat.HpLost), "disk round trip totals");
            archive.Save(s.Run!); File.WriteAllText(archive.FilePath, "broken-json");
            Check(archive.Load()?.Key == s.Run!.Key, "corrupt current file recovers from backup");
            loaded!.Combats.Add(loaded.Combats[0]);
            bool rejected = false;
            try { JournalArchive.Validate(loaded); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "duplicate combat keys rejected");
            Check(!File.Exists(archive.FilePath + ".tmp"), "no abandoned temp file after save");
        }
        finally { Directory.Delete(dir, true); }
        Console.WriteLine($"PASS {_checks} journal assertions, including 4000 randomized observations.");
    }
}
