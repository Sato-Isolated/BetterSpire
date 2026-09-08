#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BetterSpire2.Journal.Core;
using BetterSpire2.DamageMeter.Core;

internal static class NativeJournalTests
{
    private static int _checks;
    private static void Check(bool condition, string label)
    { _checks++; if (!condition) throw new InvalidOperationException(label); }
    private static JournalSession New()
    {
        var session = new JournalSession();
        session.Attach(new RunJournal { Key = "native-journal", DamageMeterCoverageVersion = 1 });
        session.RegisterPlayer(new JournalPlayer { Id = "a", Name = "Alice", IsLocal = true });
        session.RegisterPlayer(new JournalPlayer { Id = "b", Name = "Bob" });
        session.BeginCombat("c1", "Native test", 1, 1, false);
        return session;
    }
    private static DamageAttribution Record(JournalSession session, long hp, long block, long overkill,
        string? dealer = "a", bool pet = false, PoisonDamageCredit? credit = null) =>
        DamageAccounting.Record(session, 1, JournalPhase.Player, hp, block, overkill, dealer,
            "effect:unknown", "", false, pet, false, credit);
    private static DamageTraceEntry Trace(DamageAttribution credit, long hp, long block = 0, long overkill = 0) =>
        new() { Round = 1, Phase = JournalPhase.Player, Dealer = "Alice", Target = "Enemy",
            TargetId = "actor:0", Hp = hp, Blocked = block, Overkill = overkill, CountsForMeter = true,
            Attribution = credit.Kind, Shares = credit.Shares.ToArray() };
    private static void Accounting()
    {
        var s = New();
        var direct = Record(s, 12, 8, 30);
        Check(DamageMeterBuilder.BuildLive(s, false).AttributedDamage == 12, "overkill excluded from HP damage");
        Check(DamageMeterBuilder.BuildLive(s, true).AttributedDamage == 20, "enemy block included only on request");
        Check(direct.Shares.Single().Overkill == 30, "overkill remains available for details");
        Record(s, 10, 0, 0, pet: true);
        Check(DamageMeterBuilder.BuildLive(s, false).AttributedDamage == 22, "pet counted once");
        Check(s.QueryTotals(JournalScope.Combat, "c1", 0, "a")[Stat.PetDamageDealtHp] == 10, "pet subtotal preserved");
        Record(s, 5, 2, 0, dealer: null);
        var frame = DamageMeterBuilder.BuildLive(s, true);
        Check(frame.UnattributedDamage == 7 && frame.AttributedDamage == 30, "unknown damage is separate");
        var pool = new PoisonStackPool();
        pool.ObserveChange("a", 3); pool.ObserveChange("b", 2); pool.ObserveChange(null, 1);
        var poison = pool.Capture(6);
        var random = new Random(111);
        for (int i = 0; i < 4000; i++)
        {
            long hp = random.Next(1000), block = random.Next(100), overkill = random.Next(2000);
            var allocation = Record(s, hp, block, overkill, dealer: null, credit: poison);
            Check(allocation.Kind == DamageAttributionKind.PoisonConvention, "poison convention disclosed");
            Check(allocation.Shares.Sum(x => x.Hp) == hp, "poison HP conservation");
            Check(allocation.Shares.Sum(x => x.Blocked) == block, "poison block conservation");
            Check(allocation.Shares.Sum(x => x.Overkill) == overkill, "poison overkill conservation");
        }
        foreach (bool block in new[] { false, true })
        {
            var indexed = DamageMeterBuilder.BuildLive(s, block);
            var aggregate = DamageMeterBuilder.Build(s.Run, block);
            Check(indexed.Rows.SequenceEqual(aggregate.Rows), "incremental totals equal persisted aggregates");
            Check(indexed.UnattributedDamage == aggregate.UnattributedDamage, "unknown index matches archive");
        }
        var total = DamageMeterBuilder.BuildLive(s, false).AttributedDamage;
        s.Complete(CombatOutcome.Won);
        Record(s, 500, 0, 0);
        Check(DamageMeterBuilder.BuildLive(s, false).AttributedDamage == total, "sealed combat does not accept late damage");
        s.BeginCombat("c2", "Second", 1, 2, false);
        Check(DamageMeterBuilder.BuildLive(s, false, DamageMeterScope.Combat).AttributedDamage == 0, "new fight resets fight only");
        Check(DamageMeterBuilder.BuildLive(s, false).AttributedDamage == total, "run total retained");
        s.BeginCombat("c1", "Retry", 1, 1, false); Record(s, 7, 0, 0);
        Check(DamageMeterBuilder.BuildLive(s, false).AttributedDamage == 7, "retry replaces branch");
    }
    private static void Revisions()
    {
        var s = New(); long meter = s.DamageRevision, journal = s.Revision;
        foreach (var metric in new[] { Stat.CardsDrawn, Stat.EnergyGained, Stat.Overkill, Stat.PetDamageDealtHp, Stat.Healing })
            s.Append(new JournalEvent("a", 1, JournalPhase.Player, metric, 1));
        Check(s.Revision > journal && s.DamageRevision == meter, "unrelated events do not invalidate meter");
        Record(s, 1, 0, 0); Check(s.DamageRevision > meter, "HP changes invalidate meter"); meter = s.DamageRevision;
        s.RecordDamageTrace(Trace(new(DamageAttributionKind.NativeDealer, new[] { new DamageShare("a", 1, 0, 0) }), 1));
        Check(s.DamageRevision == meter, "trace is not another damage writer");
        s.RegisterPlayer(new JournalPlayer { Id = "a", Name = "Renamed", IsLocal = true });
        Check(s.DamageRevision > meter, "renames invalidate meter"); meter = s.DamageRevision;
        s.MarkPartial(); Check(s.DamageRevision > meter, "coverage invalidates meter");
    }
    private static void Cursor()
    {
        var cursor = new HistoryCursor(); var list = new List<object> { new(), new() }; int count = 0;
        Check(cursor.Consume(list, _ => { count++; cursor.Consume(list, _ => count += 1000); }), "initial cursor");
        Check(count == 2, "reentrant notification does not double count");
        Check(cursor.Consume(list, _ => count++), "unchanged cursor"); Check(count == 2, "repeat drain idempotent");
        list[1] = new object();
        Check(!cursor.Consume(list, _ => count++), "same-count replacement detected"); Check(count == 2, "replacement not double-counted");
        list.Add(new object()); Check(cursor.Consume(list, _ => count++), "future append resumes"); Check(count == 3, "future append only");
        list.Clear(); Check(!cursor.Consume(list, _ => count++), "unexpected clear detected");
        cursor.Reset(); list.Add(new object()); cursor.Consume(list, _ => count++); Check(count == 4, "new combat resets fence");
    }
    private static void Traces()
    {
        var s = New(); var a = Record(s, 12, 8, 30); var input = Trace(a, 12, 8, 30);
        s.RecordDamageTrace(input);
        input.Shares[0] = new DamageShare("a", 900, 0, 0);
        Check(s.Active!.DamageTrace[0].Shares[0].Hp == 12, "trace detaches caller array");
        var checkpoint = new JournalCheckpoint(); var first = checkpoint.Capture(s.Run!); string firstJson = JsonSerializer.Serialize(first);
        for (int i = 0; i < 3000; i++) { var credit = Record(s, 1, 0, 0); s.RecordDamageTrace(Trace(credit, 1)); }
        Check(s.Active.DamageTraceTruncated && s.Active.DamageTrace.Count <= 2048, "per-combat bounded trace");
        Check(s.Active.LastDamageSequence == 3001, "monotonic trace sequence survives pruning");
        Check(DamageMeterBuilder.BuildLive(s, false).AttributedDamage == 3012, "pruning preserves exact totals");
        Check(JsonSerializer.Serialize(first) == firstJson, "checkpoint does not share mutable trace arrays");
        s.Complete(CombatOutcome.Won); var completed = checkpoint.Capture(s.Run!);
        var reused = checkpoint.Capture(s.Run!);
        Check(ReferenceEquals(completed.Combats[0], reused.Combats[0]), "completed trace checkpoint cached");
        for (int fight = 2; fight <= 12; fight++)
        {
            s.BeginCombat("c" + fight, "Fight", 1, fight, false);
            for (int i = 0; i < 700; i++) { var credit = Record(s, 1, 0, 0); s.RecordDamageTrace(Trace(credit, 1)); }
            s.Complete(CombatOutcome.Won);
        }
        Check(s.Run!.Combats.Sum(c => c.DamageTrace.Count) <= 8192, "run trace budget");
        Check(DamageMeterBuilder.BuildLive(s, false).AttributedDamage == 10712, "run pruning preserves damage");
        var newer = checkpoint.Capture(s.Run);
        Check(newer.Combats[0].DamageTrace.Count == s.Run.Combats[0].DamageTrace.Count, "pruned completed trace refreshes checkpoint cache");
        Check(completed.Combats[0].DamageTrace.Count > newer.Combats[0].DamageTrace.Count, "older checkpoint stays immutable after pruning");
        JournalArchive.Validate(newer);
        var selection = new JournalSelection(); selection.Reset(s.Run); selection.SelectCombat(s.Run, "c12");
        var report = JournalReportBuilder.Build(s, selection, false, 0, true, timeline: true);
        Check(report.Rows.Any(r => r.Detail?.Contains("Alice") == true), "timeline includes detailed attribution");
        Check(report.Rows.All(r => r.CombatKey == null), "trace rows are not navigation targets");
        string path = Path.Combine(Path.GetTempPath(), "betterspire-native-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            new JournalArchive(path).Save(newer); var restored = new JournalArchive(path).Load()!;
            Check(restored.Combats.Last().DamageTrace.Last().Shares.Single().Hp == 1, "trace JSON round trip");
            Check(restored.Combats[0].DamageTraceTruncated, "truncation persisted");
        }
        finally { foreach (string suffix in new[] { "", ".bak", ".tmp" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
        var legacy = New(); legacy.Active!.DamageTraceVersion = 0;
        var legacySelection = new JournalSelection(); legacySelection.Reset(legacy.Run); legacySelection.SelectCombat(legacy.Run!, "c1");
        var oldReport = JournalReportBuilder.Build(legacy, legacySelection, false, 0, true, timeline: true);
        Check(oldReport.Note.Contains("Ancien combat"), "legacy archive not falsely presented as full timeline");
        bool rejected = false;
        try { legacy.RecordDamageTrace(Trace(new(DamageAttributionKind.NativeDealer, new[] { new DamageShare("a", 5, 0, 0) }), 6)); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "nonconserving attribution rejected");
    }
    private static void Main()
    {
        Accounting(); Revisions(); Cursor(); Traces();
        Console.WriteLine($"PASS {_checks} native-journal accounting, revision, cursor, trace, persistence and timeline checks (no game runtime).");
    }
}
