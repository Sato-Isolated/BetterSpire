#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BetterSpire2.Journal.Core;

public static class PlayerNameText
{
    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var clean = new StringBuilder(Math.Min(value.Length, 256));
        foreach (char c in value)
        {
            if (char.IsControl(c) || c is '\u2028' or '\u2029') continue;
            // Drop directional overrides and invisible formatting, but preserve emoji joiners.
            if (char.GetUnicodeCategory(c) == UnicodeCategory.Format && c is not ('\u200c' or '\u200d')) continue;
            clean.Append(c);
        }
        return clean.ToString().Trim();
    }
    public static string? Resolved(string? value, string platformId)
    {
        string name = Clean(value);
        if (name.Length == 0 || name == platformId ||
            name.Equals("[unknown]", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("[unassigned]", StringComparison.OrdinalIgnoreCase)) return null;
        // Bounded stored names, without cutting a Unicode text element or parsing BBCode.
        var elements = StringInfo.GetTextElementEnumerator(name);
        var result = new StringBuilder();
        int count = 0;
        while (elements.MoveNext() && count++ < 80) result.Append(elements.GetTextElement());
        return result.ToString();
    }
}

/// <summary>Only caches display identities. It never changes player IDs, damage totals or ranking.</summary>
public sealed class PlayerNameCache
{
    public const ulong PendingRetryMilliseconds = 2000;
    public const ulong ResolvedRefreshMilliseconds = 30000;
    private sealed class Entry { public string? Name; public ulong NextLookup; }
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    public void Clear() => _entries.Clear();

    public string? Resolve(string id, ulong now, Func<string?> lookup)
    {
        if (!_entries.TryGetValue(id, out var entry)) _entries[id] = entry = new Entry();
        if (now < entry.NextLookup) return entry.Name;
        string? name = null;
        try { name = lookup(); } catch { /* A temporary platform failure is not a combat failure. */ }
        if (!string.IsNullOrWhiteSpace(name)) entry.Name = name;
        // Keep the last known real nickname if Steam becomes unavailable; retry at a bounded rate.
        ulong delay = string.IsNullOrWhiteSpace(name) ? PendingRetryMilliseconds : ResolvedRefreshMilliseconds;
        entry.NextLookup = now > ulong.MaxValue - delay ? ulong.MaxValue : now + delay;
        return entry.Name;
    }
}
