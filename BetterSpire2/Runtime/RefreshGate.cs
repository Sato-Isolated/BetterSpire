#nullable enable
using System;

namespace BetterSpire2.Runtime;

/// <summary>Coalesces gameplay notifications, with a bounded safety refresh and retry backoff.</summary>
public sealed class RefreshGate
{
    private readonly ulong _minimumInterval, _safetyInterval, _retryInterval;
    private ulong _lastCapture, _retryAfter;
    private long _version = 1, _capturedVersion;
    private bool _captured;
    public long Version => _version;
    public bool IsDirty => !_captured || _version != _capturedVersion;

    public RefreshGate(ulong minimumInterval = 100, ulong safetyInterval = 2000, ulong retryInterval = 1000)
    {
        if (minimumInterval == 0 || safetyInterval < minimumInterval || retryInterval == 0)
            throw new ArgumentOutOfRangeException(nameof(minimumInterval));
        _minimumInterval = minimumInterval; _safetyInterval = safetyInterval; _retryInterval = retryInterval;
    }
    public void Invalidate() { unchecked { _version++; } }
    public bool IsDue(ulong now) => now >= _retryAfter &&
        (!_captured || now - _lastCapture >= (IsDirty ? _minimumInterval : _safetyInterval));
    public void Complete(ulong now, long capturedVersion)
    {
        _lastCapture = now; _capturedVersion = capturedVersion; _captured = true; _retryAfter = 0;
    }
    public void Fail(ulong now) { Invalidate(); _retryAfter = now + _retryInterval; }
    public void Reset()
    {
        _captured = false; _lastCapture = _retryAfter = 0; Invalidate();
    }
}
