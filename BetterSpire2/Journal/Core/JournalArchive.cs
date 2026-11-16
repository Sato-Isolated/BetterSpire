#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BetterSpire2.Journal.Core;

/// <summary>One latest run, a recoverable backup, and no writes to the game's save files.</summary>
public sealed class JournalArchive
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false, MaxDepth = 32 };
    private const long MaxBytes = 16 * 1024 * 1024;
    private const long MaxTotalRounds = 65_536;
    public string FilePath { get; }
    public JournalArchive(string path) => FilePath = path;
    public RunJournal? Load()
    {
        Exception? error = null;
        foreach (var path in new[] { FilePath, FilePath + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try
            {
                byte[] bytes = ReadBounded(path);
                ValidateAggregateRoundCount(bytes);
                var result = JsonSerializer.Deserialize<RunJournal>(bytes, Options);
                Validate(result);
                return result;
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or ArgumentException)
            { error = ex; }
        }
        if (error != null) throw new InvalidDataException("The journal and its backup cannot be read.", error);
        return null;
    }

    private static byte[] ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxBytes) throw new InvalidDataException("Journal exceeds 16 MB.");

        using var output = new MemoryStream((int)stream.Length);
        var buffer = new byte[8192];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (output.Length + read > MaxBytes)
                throw new InvalidDataException("Journal exceeds 16 MB.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static void ValidateAggregateRoundCount(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) return;

        long totalRounds = 0;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name != "Combats" || property.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var combat in property.Value.EnumerateArray())
            {
                if (combat.ValueKind != JsonValueKind.Object) continue;
                foreach (var combatProperty in combat.EnumerateObject())
                {
                    if (combatProperty.Name != "Rounds" || combatProperty.Value.ValueKind != JsonValueKind.Array) continue;
                    totalRounds += combatProperty.Value.GetArrayLength();
                    if (totalRounds > MaxTotalRounds)
                        throw new InvalidDataException("Journal exceeds the total round limit.");
                }
            }
        }
    }

    public void Save(RunJournal data)
    {
        Validate(data);
        string json = JsonSerializer.Serialize(data, Options);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new InvalidDataException("Journal exceeds 16 MB.");
        string? dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string temp = FilePath + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            { writer.Write(json); writer.Flush(); stream.Flush(true); }
            // Same filesystem: replace atomically, retain the last valid snapshot.
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
            else File.Move(temp, FilePath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static void Validate(RunJournal? run)
    {
        if (run == null || run.SchemaVersion != 1 || string.IsNullOrWhiteSpace(run.Key) || run.Key.Length > 4096 ||
            run.DamageMeterCoverageVersion is < 0 or > 1 || run.Seed == null || run.Status is not ("ongoing" or "won" or "lost" or "abandoned" or "suspended") ||
            run.Players == null || run.Combats == null || run.Players.Count > 64 || run.Combats.Count > 512)
            throw new InvalidDataException("Invalid journal schema.");
        if (run.Players.Any(p => p.Value == null || p.Key != p.Value.Id || p.Value.Name == null) ||
            run.Combats.Any(c => c == null || c.Rounds == null || string.IsNullOrWhiteSpace(c.Key) || c.Encounter == null || c.Rounds.Count > 4096 ||
                c.LatestRound < 0 || c.LatestRound > 1000000 || !Enum.IsDefined(c.Outcome) ||
                c.ConventionAttributionVersion is < 0 or > 1 || c.PartialReasons == null || c.PartialReasons.Count > 16 ||
                c.PartialReasons.Any(reason => string.IsNullOrWhiteSpace(reason) || reason.Length > 128) ||
                c.PartialReasons.Distinct(StringComparer.Ordinal).Count() != c.PartialReasons.Count) ||
            run.Combats.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() != run.Combats.Count)
            throw new InvalidDataException("Invalid journal identities.");
        long totalRounds = 0;
        foreach (var combat in run.Combats)
        {
            totalRounds += combat.Rounds.Count;
            if (totalRounds > MaxTotalRounds)
                throw new InvalidDataException("Journal exceeds the total round limit.");
        }
        DamageTraceBuffer.Validate(run);
        foreach (var combat in run.Combats)
        {
            if (combat.Rounds.Select(r => r?.Number).Distinct().Count() != combat.Rounds.Count)
                throw new InvalidDataException("Duplicate rounds.");
            foreach (var round in combat.Rounds)
            {
                if (round == null || round.Number < 0 || round.Number > combat.LatestRound || round.Players == null || round.Players.Count > 64)
                    throw new InvalidDataException("Invalid round.");
                CheckLine(round.UnattributedDamage);
                if (round.UnattributedDamage.Values.Keys.Any(k => k is not (Stat.DamageDealtHp or Stat.DamageDealtBlocked or Stat.Overkill)))
                    throw new InvalidDataException("Invalid unattributed damage metric.");
                foreach (var pair in round.Players)
                {
                    var stats = pair.Value;
                    if (!run.Players.ContainsKey(pair.Key) || stats == null || stats.Sources == null || stats.Sources.Count > 257 ||
                        stats.ConventionDamage == null)
                        throw new InvalidDataException("Invalid player statistics.");
                    CheckLine(stats.Totals);
                    CheckLine(stats.ConventionDamage);
                    if (stats.ConventionDamage.Values.Keys.Any(k => k is not (Stat.DamageDealtHp or Stat.DamageDealtBlocked or Stat.Overkill)))
                        throw new InvalidDataException("Invalid convention damage metric.");
                    if (stats.ConventionDamage.Values.Any(p => p.Value > stats.Totals[p.Key]))
                        throw new InvalidDataException("Convention damage exceeds attributed damage.");
                    foreach (var source in stats.Sources)
                    {
                        if (source.Value == null || source.Key != source.Value.Id || source.Value.Name == null)
                            throw new InvalidDataException("Invalid source.");
                        CheckLine(source.Value.Stats);
                    }
                }
            }
        }
    }
    private static void CheckLine(StatLine? line)
    {
        if (line?.Values == null || line.Values.Any(p => !Enum.IsDefined(p.Key) || p.Value < 0))
            throw new InvalidDataException("Invalid metric.");
    }
}
