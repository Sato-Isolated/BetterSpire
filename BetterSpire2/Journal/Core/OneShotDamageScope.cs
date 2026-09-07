#nullable enable
using System;
using System.Threading;

namespace BetterSpire2.Journal.Core;

/// <summary>
/// Async-local, one-command provenance. A nested damage command masks its parent,
/// even if it deals source-less damage to the very same creature. No thread-static
/// or global "last player" state; a pending request may be claimed only once.
/// </summary>
public sealed class OneShotDamageScope<T> where T : class
{
    private sealed class Request(T value) { public T Value { get; } = value; public int Claimed; }
    private readonly AsyncLocal<Request?> _pending = new();
    private readonly AsyncLocal<T?> _current = new();
    public T? Current => _current.Value;

    public IDisposable RequestNext(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var previous = _pending.Value;
        _pending.Value = new Request(value);
        return new Restore(() => _pending.Value = previous);
    }
    public IDisposable EnterCommand()
    {
        T? previous = _current.Value;
        Request? request = _pending.Value;
        _current.Value = request != null && Interlocked.Exchange(ref request.Claimed, 1) == 0
            ? request.Value : null;
        return new Restore(() => _current.Value = previous);
    }
    private sealed class Restore(Action restore) : IDisposable
    {
        private Action? _restore = restore;
        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }
}
