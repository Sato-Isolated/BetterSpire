#nullable enable
using System;
using System.Collections.Generic;

namespace BetterSpire2.Journal.Core;

/// <summary>Append-only consumption. Native lists also use an identity fence, without rescanning earlier entries.</summary>
public sealed class HistoryCursor
{
    public int Processed { get; private set; }
    private object? _last;
    private bool _reading;
    public void Reset() { Processed = 0; _last = null; }
    public bool Consume(int count, Action<int> visit)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (_reading) return true;
        if (count < Processed) { Processed = count; _last = null; return false; }
        _reading = true;
        try { while (Processed < count) { int index = Processed++; visit(index); } }
        finally { _reading = false; }
        return true;
    }
    public bool Consume<T>(IReadOnlyList<T> entries, Action<int> visit) where T : class
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (_reading) return true;
        int count = entries.Count;
        if (count < Processed || Processed > 0 && !ReferenceEquals(_last, entries[Processed - 1]))
        {
            // A replacement list is not a replay delta. Skip its existing prefix, mark coverage partial,
            // then accept future appended observations. Never silently double-count the replaced list.
            Processed = count; _last = count == 0 ? null : entries[count - 1];
            return false;
        }
        _reading = true;
        try
        {
            while (Processed < count)
            {
                int index = Processed++; _last = entries[index];
                visit(index);
            }
        }
        finally { _reading = false; }
        return true;
    }
}
