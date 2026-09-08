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
                if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("Journal exceeds 16 MB.");
                var result = JsonSerializer.Deserialize<RunJournal>(File.ReadAllText(path), Options);
                Validate(result);
                return result;
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or ArgumentException)
            { error = ex; }
        }
        if (error != null) throw new InvalidDataException("The journal and its backup cannot be read.", error);
        return null;
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
                c.LatestRound < 0 || c.LatestRound > 1000000 || !Enum.IsDefined(c.Outcome)) ||
            run.Combats.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() != run.Combats.Count)
            throw new InvalidDataException("Invalid journal identities.");
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
                    if (!run.Players.ContainsKey(pair.Key) || stats == null || stats.Sources == null || stats.Sources.Count > 257)
                        throw new InvalidDataException("Invalid player statistics.");
                    CheckLine(stats.Totals);
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
