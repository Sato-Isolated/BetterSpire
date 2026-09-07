#nullable enable
using System;

namespace BetterSpire2.Journal.Core;

/// <summary>Consumes an append-only native list exactly once, without scanning earlier entries.</summary>
public sealed class HistoryCursor
{
    public int Processed { get; private set; }
    public void Reset() => Processed = 0;
    public bool Consume(int count, Action<int> visit)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (count < Processed) { Processed = count; return false; }
        while (Processed < count) { int index = Processed++; visit(index); }
        return true;
    }
}
