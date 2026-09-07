#nullable enable
using System;
using System.Collections.Generic;

namespace BetterSpire2.HandViewer;

internal static class HandViewerSelection
{
    internal static int Preserve(IReadOnlyList<ulong> ids, ulong? selected, int fallback)
    {
        if (selected.HasValue)
            for (int i = 0; i < ids.Count; i++) if (ids[i] == selected.Value) return i;
        return ids.Count == 0 ? 0 : Math.Clamp(fallback, 0, ids.Count - 1);
    }
    internal static int Move(int current, int count, int direction)
    {
        if (count <= 0) return 0;
        long value = (long)current + direction;
        return (int)((value % count + count) % count);
    }
}
