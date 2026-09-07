#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using BetterSpire2.DamageMeter.Core;
using BetterSpire2.Journal.Core;

internal static class DamageMeterTests
{
    private static int _checks;
    private static void Check(bool ok, string name)
    { _checks++; if (!ok) throw new Exception("FAIL: " + name); }
    private static void Equal(decimal expected, decimal actual, string name)
        => Check(expected == actual, $"{name}: expected {expected}, got {actual}");
    private static JournalSession Make(int players = 1)
    {
        var s = new JournalSession();
        s.Attach(new RunJournal { Key = "run-A", DamageMeterCoverageVersion = 1 });
        for (int i = 1; i <= players; i++)
            s.RegisterPlayer(new JournalPlayer { Id = "p" + i, Name = "Player " + i, IsLocal = i == 1 });
        s.BeginCombat("c1", "Enemy", 1, 1, false);
        return s;
    }
    private static void Add(JournalSession s, string id, long hp, long block = 0, int round = 1)
    {
        s.Append(new JournalEvent(id, round, JournalPhase.Player, Stat.DamageDealtHp, hp));
        s.Append(new JournalEvent(id, round, JournalPhase.Player, Stat.DamageDealtBlocked, block));
    }
    private static DamageMeterSnapshot Live(JournalSession s, DamageMeterScope scope, bool block = false) =>
        DamageMeterBuilder.BuildLive(s, block, scope);

    private static void CombatScopeTests()
    {
        Check(DamageMeterContext.Select(false, true) == DamageMeterScope.Combat, "fight selects combat");
        Check(DamageMeterContext.Select(true, true) == DamageMeterScope.Run, "map during fight takes priority");
        Check(DamageMeterContext.Select(true, false) == DamageMeterScope.Run, "map between fights selects run");
        Check(DamageMeterContext.Select(false, false) == DamageMeterScope.Run, "outside combat keeps run");
        var s = Make(2);
        Equal(0, Live(s, DamageMeterScope.Combat).AttributedDamage, "first fight starts at zero");
        Add(s, "p1", 10, 2, round: 0); // Setup belongs to this fight, not a separate running total.
        Add(s, "p2", 20, 3, round: 1);
        s.ObserveRound(2); Add(s, "p1", 30, 4, round: 2);
        Equal(60, Live(s, DamageMeterScope.Combat).AttributedDamage, "next round never resets fight damage");
        Equal(69, Live(s, DamageMeterScope.Combat, true).AttributedDamage, "fight block optional");
        s.AppendUnattributed(2, Stat.DamageDealtHp, 9);
        s.AppendUnattributed(2, Stat.DamageDealtBlocked, 3);
        Equal(9, Live(s, DamageMeterScope.Combat).UnattributedDamage, "unattributed fight scoped");
        Equal(12, Live(s, DamageMeterScope.Combat, true).UnattributedDamage, "unattributed fight block optional");
        s.Complete(CombatOutcome.Won);
        Equal(60, Live(s, DamageMeterScope.Combat).AttributedDamage, "victory keeps completed fight until leaving room");
        s.BeginCombat("c2", "Next", 1, 2, false);
        Equal(0, Live(s, DamageMeterScope.Combat).AttributedDamage, "second fight starts at zero");
        Equal(60, Live(s, DamageMeterScope.Run).AttributedDamage, "reset never clears the run");
        Equal(0, Live(s, DamageMeterScope.Combat).UnattributedDamage, "unknown fight total also resets");
        Equal(9, Live(s, DamageMeterScope.Run).UnattributedDamage, "unknown run total retained");
        Add(s, "p2", 100, 11); s.AppendUnattributed(1, Stat.DamageDealtHp, 7);
        long revision = s.Revision;
        for (int i = 0; i < 100; i++)
        {
            Equal(160, Live(s, DamageMeterContext.Select(true, true)).AttributedDamage, "map shows cumulative damage");
            Equal(100, Live(s, DamageMeterContext.Select(false, true)).AttributedDamage, "return to fight restores its total");
        }
        Equal(revision, s.Revision, "map and HUD never mutate counters");
        var combat = Live(s, DamageMeterScope.Combat);
        Equal(1000, combat.Rows.Single(p => p.Id == "p2").ShareTenths, "shares use current fight denominator");
        Equal(0, combat.Rows.Single(p => p.Id == "p1").Damage, "inactive player stays visible at zero in fight");
        s.Complete(CombatOutcome.Won); s.DetachCombat();
        Equal(160, Live(s, DamageMeterScope.Run).AttributedDamage, "leaving room retains run");
        Equal(0, Live(s, DamageMeterScope.Combat).AttributedDamage, "detached fight cannot leak stale combat damage");
        s.BeginCombat("c2", "Next", 1, 2, false); Add(s, "p1", 5);
        Equal(5, Live(s, DamageMeterScope.Combat).AttributedDamage, "replayed fight replaces attempt");
        Equal(65, Live(s, DamageMeterScope.Run).AttributedDamage, "replay not counted twice in run");
        Equal(9, Live(s, DamageMeterScope.Run).UnattributedDamage, "replay removes old unattributed attempt");
        var resumed = new JournalSession();
        resumed.Attach(JsonSerializer.Deserialize<RunJournal>(JsonSerializer.Serialize(s.Run))!);
        Equal(65, Live(resumed, DamageMeterScope.Run).AttributedDamage, "archive restores full run");
        Equal(0, Live(resumed, DamageMeterScope.Combat).AttributedDamage, "archive does not attach old combat automatically");
        resumed.BeginCombat("c2", "Next", 1, 2, true); Add(resumed, "p2", 12);
        Equal(12, Live(resumed, DamageMeterScope.Combat).AttributedDamage, "resumed fight rebuilt once");
        Equal(72, Live(resumed, DamageMeterScope.Run).AttributedDamage, "resumed fight replaces old attempt");
        Check(Live(resumed, DamageMeterScope.Combat).IsPartial, "resumed partial capture is explicit");
        resumed.BeginCombat("c3", "Third", 1, 3, false);
        Check(!Live(resumed, DamageMeterScope.Combat).IsPartial, "old partial fight does not contaminate new fight coverage");
        Check(Live(resumed, DamageMeterScope.Run).IsPartial, "old partial fight remains in run coverage");
        resumed.Attach(new RunJournal { Key = "fresh", DamageMeterCoverageVersion = 1 });
        Equal(0, Live(resumed, DamageMeterScope.Run).AttributedDamage, "new run clears all damage");
        Equal(0, Live(resumed, DamageMeterScope.Combat).AttributedDamage, "new run clears fight index too");
    }

    private static void PlayerNameTests()
    {
        Check(PlayerNameText.Resolved("76561199999999999", "76561199999999999") == null, "Steam ID is not a nickname");
        Check(PlayerNameText.Resolved("[unknown]", "42") == null, "missing Steam profile remains pending");
        Check(PlayerNameText.Resolved(" \n\r\t", "42") == null, "blank nickname remains pending");
        Check(PlayerNameText.Resolved("[Clan] Nick · FR", "42") == "[Clan] Nick · FR", "Steam name has no character parsing");
        Check(PlayerNameText.Clean("\u202eNick\u2028\u2029") == "Nick", "single line without bidi overrides");
        Check(PlayerNameText.Clean("A\u200dB") == "A\u200dB", "emoji joiner preserved");
        var cache = new PlayerNameCache();
        int calls = 0;
        string? Resolve() { calls++; return calls == 1 ? null : "MindLated"; }
        Check(cache.Resolve("local-id-1", 0, Resolve) == null, "missing nickname falls back initially");
        for (ulong now = 1; now < 2000; now++) cache.Resolve("local-id-1", now, Resolve);
        Equal(1, calls, "hover/frame polling never repeatedly queries pending Steam name");
        Check(cache.Resolve("local-id-1", 2000, Resolve) == "MindLated", "late Steam profile replaces fallback");
        for (ulong now = 2001; now < 32000; now++) cache.Resolve("local-id-1", now, Resolve);
        Equal(2, calls, "resolved profile cached for thirty seconds");
        Check(cache.Resolve("local-id-1", 32000, () => "NewNickname") == "NewNickname", "periodic nickname change refresh");
        Check(cache.Resolve("local-id-1", 62000, () => throw new Exception("Steam offline")) == "NewNickname", "temporary Steam failure retains last real name");
        Check(cache.Resolve("remote-id", 0, () => "NewNickname") == "NewNickname", "duplicate names have independent IDs");
        cache.Clear();
        Check(cache.Resolve("local-id-1", 0, () => null) == null, "new run cannot inherit another session nickname cache");
        var session = Make(); Add(session, "p1", 50);
        session.RegisterPlayer(new JournalPlayer { Id = "p1", Name = "MindLated", IsLocal = true });
        var snapshot = Live(session, DamageMeterScope.Combat);
        Check(snapshot.Rows[0].Name == "MindLated" && snapshot.Rows[0].Id == "p1", "solo row shows nickname without changing identity");
        Equal(50, snapshot.Rows[0].Damage, "name refresh never resets damage");
    }
    private static void Main()
    {
        CombatScopeTests();
        PlayerNameTests();
        Equal(0, DamageMeterBuilder.Build(null, false).Rows.Count, "no phantom players before a run");
        var s = Make();
        var zero = DamageMeterBuilder.Build(s.Run, false);
        Check(!zero.IsPartial && !zero.IsMultiplayer, "new full solo run");
        Equal(0, zero.Rows[0].ShareTenths, "zero division guarded");
        Add(s, "p1", 100, 20);
        s.Append(new JournalEvent("p1", 1, JournalPhase.Player, Stat.PetDamageDealtHp, 40));
        s.Append(new JournalEvent("p1", 1, JournalPhase.Player, Stat.Overkill, 999));
        s.Append(new JournalEvent("p1", 1, JournalPhase.Enemy, Stat.HpLost, 42));
        s.Append(new JournalEvent("p1", 1, JournalPhase.Player, Stat.Healing, 42));
        var solo = DamageMeterBuilder.Build(s.Run, false);
        Equal(100, solo.Rows[0].Damage, "HP only; pet subtotal is already included; incoming damage excluded");
        Equal(1000, solo.Rows[0].ShareTenths, "solo is 100 percent");
        Equal(120, DamageMeterBuilder.Build(s.Run, true).Rows[0].Damage, "optional block; never overkill");
        Equal(100, s.Query(JournalScope.Run, null, 0, "p1").Totals[Stat.DamageDealtHp], "matches journal totals");
        long revision = s.Revision;
        for (int i = 0; i < 100; i++) Equal(100, DamageMeterBuilder.Build(s.Run, false).AttributedDamage, "re-render not an accumulator");
        Equal(revision, s.Revision, "render is read-only");
        s.Complete(CombatOutcome.Won);
        s.BeginCombat("c2", "Second", 1, 2, false); Add(s, "p1", 30, 4);
        Equal(130, DamageMeterBuilder.Build(s.Run, false).AttributedDamage, "combat changes keep run total");
        s.AppendUnattributed(1, Stat.DamageDealtHp, 11);
        s.AppendUnattributed(1, Stat.DamageDealtBlocked, 3);
        s.AppendUnattributed(1, Stat.Overkill, 100);
        s.AppendUnattributed(1, Stat.Healing, 999); // invalid metric must not be accepted
        var unknown = DamageMeterBuilder.Build(s.Run, false);
        Equal(130, unknown.AttributedDamage, "unknown damage not credited to the only player");
        Equal(11, unknown.UnattributedDamage, "unknown HP retained");
        Equal(14, DamageMeterBuilder.Build(s.Run, true).UnattributedDamage, "unknown block optional");
        Equal(11, s.QueryUnattributed(JournalScope.Combat, "c2", 0)[Stat.DamageDealtHp], "unknown detail query");
        Equal(0, s.QueryUnattributed(JournalScope.Combat, "c1", 0)[Stat.DamageDealtHp], "unknown scoped to correct combat");
        s.Complete(CombatOutcome.Won); s.AppendUnattributed(1, Stat.DamageDealtHp, 999);
        Equal(11, DamageMeterBuilder.Build(s.Run, false).UnattributedDamage, "sealed combats reject unknown events too");
        s.BeginCombat("c2", "Second", 1, 2, false); Add(s, "p1", 7);
        Equal(107, DamageMeterBuilder.Build(s.Run, false).AttributedDamage, "reload replaces, not adds");
        Equal(0, DamageMeterBuilder.Build(s.Run, false).UnattributedDamage, "reload removes stale unassigned damage");
        s.BeginCombat("c1", "First", 1, 1, false); Add(s, "p1", 8);
        Equal(8, DamageMeterBuilder.Build(s.Run, false).AttributedDamage, "rollback removes future combats");
        s.Attach(new RunJournal { Key = "run-B", DamageMeterCoverageVersion = 1 });
        Equal(0, DamageMeterBuilder.Build(s.Run, false).AttributedDamage, "new run resets");

        var m = Make(3); Add(m, "p1", 600); Add(m, "p2", 250); Add(m, "p3", 150);
        var multi = DamageMeterBuilder.Build(m.Run, false);
        Check(multi.IsMultiplayer, "automatic multiplayer");
        Equal(1000, multi.AttributedDamage, "team sum");
        Equal(600, multi.Rows[0].ShareTenths, "60 percent");
        Equal(250, multi.Rows[1].ShareTenths, "25 percent");
        Equal(150, multi.Rows[2].ShareTenths, "15 percent");
        m.AppendUnattributed(1, Stat.DamageDealtHp, 400);
        Equal(600, DamageMeterBuilder.Build(m.Run, false).Rows[0].ShareTenths, "unknown excluded from team share denominator");
        Add(m, "p2", 0, 1000);
        Check(DamageMeterBuilder.Build(m.Run, true).Rows[0].Id == "p2", "block toggle recomputes ranking, not saved data");
        Check(DamageMeterBuilder.Build(m.Run, false).Rows[0].Id == "p1", "HP mode restored without reset");
        m.RegisterPlayer(new JournalPlayer { Id = "p1", Name = "Same", IsLocal = true });
        m.RegisterPlayer(new JournalPlayer { Id = "p2", Name = "Same" });
        Equal(3, DamageMeterBuilder.Build(m.Run, false).Rows.Count, "duplicate nicknames never merge");
        m.MarkPartial(); Check(DamageMeterBuilder.Build(m.Run, false).IsPartial, "missing observations flagged");
        m.Run!.DamageMeterCoverageVersion = 0;
        Check(DamageMeterBuilder.Build(m.Run, false).IsPartial, "legacy archive does not claim full coverage");

        var tie = Make(4); Add(tie, "p1", 1); Add(tie, "p2", 1); Add(tie, "p3", 1);
        var tied = DamageMeterBuilder.Build(tie.Run, false);
        Equal(1000, tied.Rows.Sum(r => r.ShareTenths), "displayed equal shares sum exactly to 100 percent");
        Equal(334, tied.Rows[0].ShareTenths, "deterministic rounding remainder");
        Equal(1, tied.Rows[1].Rank, "same damage has same rank");
        Equal(4, tied.Rows[3].Rank, "competition ranking after ties");
        Equal(0, tied.Rows[3].ShareTenths, "zero contributor stays zero");
        Check(tied.Rows[0].Id == "p1" && tied.Rows[1].Id == "p2", "identity tie-break stable");
        var rng = new Random(33);
        for (int i = 0; i < 4096; i++)
        {
            decimal[] amounts = Enumerable.Range(0, 1 + rng.Next(64)).Select(_ => (decimal)rng.NextInt64(0, 1000000000000)).ToArray();
            int[] shares = DamageMeterBuilder.AllocateShares(amounts);
            Equal(amounts.Sum() == 0 ? 0 : 1000, shares.Sum(), "random share sum");
            Check(shares.All(p => p >= 0 && p <= 1000), "random shares bounded");
        }
        bool negativeRejected = false;
        try { DamageMeterBuilder.AllocateShares(new[] { -1m, 1m }); } catch (ArgumentOutOfRangeException) { negativeRejected = true; }
        Check(negativeRejected, "negative share input rejected");

        var many = Make(8);
        for (int i = 1; i <= 8; i++) Add(many, "p" + i, i * 100);
        var all = DamageMeterBuilder.Build(many.Run, false);
        var visible = DamageMeterBuilder.VisibleRows(all, true);
        Equal(5, visible.Count, "large party uses four players plus one group row");
        Check(visible.Any(r => r.IsLocal), "local player retained below top four");
        Equal(all.AttributedDamage, visible.Sum(r => r.Damage), "folded rows preserve totals");
        Equal(1000, visible.Sum(r => r.ShareTenths), "folded rows preserve shares");
        Check(visible[^1].IsGroup, "group row not pretending to be a player");
        var huge = Make(64);
        for (int i = 1; i <= 64; i++) Add(huge, "p" + i, long.MaxValue, long.MaxValue);
        Equal((decimal)long.MaxValue * 128, DamageMeterBuilder.Build(huge.Run, true).AttributedDamage, "aggregate does not overflow Int64");

        string folder = Path.Combine(Path.GetTempPath(), "BetterSpireMeterTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var archive = new JournalArchive(Path.Combine(folder, "journal.json"));
            archive.Save(m.Run!);
            var resumed = new JournalSession(); resumed.Attach(archive.Load()!);
            Equal(DamageMeterBuilder.Build(m.Run, false).AttributedDamage,
                DamageMeterBuilder.Build(resumed.Run, false).AttributedDamage, "saved run resumes same total");
            Equal(400, DamageMeterBuilder.Build(resumed.Run, false).UnattributedDamage, "unknown damage persists");
            var invalid = archive.Load()!;
            invalid.Combats[0].Rounds[0].UnattributedDamage.Values[Stat.Healing] = 1;
            bool rejected = false;
            try { JournalArchive.Validate(invalid); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "archive rejects invalid unassigned metric");
            string legacy = "{\"Key\":\"legacy\",\"Players\":{},\"Combats\":[]}";
            var old = JsonSerializer.Deserialize<RunJournal>(legacy)!;
            JournalArchive.Validate(old);
            Check(old.DamageMeterCoverageVersion == 0 && DamageMeterBuilder.Build(old, false).IsPartial, "old JSON accepted with honest coverage");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

        float[] widths = { 320, 640, 800, 1280, 1920, 2560, 3440 };
        float[] heights = { 240, 480, 720, 1080, 1440 };
        foreach (float w in widths) foreach (float h in heights)
        foreach (int scale in new[] { 75, 100, 150 }) foreach (int rows in new[] { 1, 4, 5 })
        foreach (int x in new[] { 0, 100 }) foreach (int y in new[] { 0, 100 })
        {
            var p = DamageMeterLayout.Place(w, h, rows, rows > 1, true, scale, x, y);
            Check(p.X >= 0 && p.Y >= 0 && p.Scale > 0, "layout nonnegative");
            Check(p.X + p.Width * p.Scale <= w + .01 && p.Y + p.Height * p.Scale <= h + .01, "meter stays inside viewport");
        }
        Equal(0, (decimal)DamageMeterLayout.Place(float.NaN, 1080, 4, true, true, 100, 100, 12).Scale, "invalid viewport hides meter");
        Check(DamageMeterText.Name("Nick · Clan", true) == "Nick · Clan", "literal Steam nickname dot preserved");
        Check(DamageMeterText.Name("[Clan] Nick", true) == "[Clan] Nick", "plain labels keep literal brackets");
        Check(!DamageMeterText.Name("Nick\n\r\t\u202eNAME", true).Any(char.IsControl), "no multiline names");
        Check(DamageMeterText.Name(new string('A', 99), true).Length <= 21, "names bounded");
        Check(DamageMeterText.Share(600, true) == "60 %", "French percent");
        Check(DamageMeterText.Number(1000000, true) == "1 M", "huge value keeps HUD compact");
        Console.WriteLine($"PASS {_checks} C# damage meter checks (pure core and journal, not game rendering).");
    }
}
