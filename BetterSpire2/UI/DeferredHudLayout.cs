#nullable enable
using System;
using System.Collections.Generic;
using Godot;

namespace BetterSpire2.UI;

/// <summary>
/// Let containers and wrapped labels finish their deferred layout before displaying an overlay.
/// Subscribes to ProcessFrame only while settling (at most six passes per request).
/// No polling, resize loop or scene-tree callback survives Dispose.
/// </summary>
internal sealed class DeferredHudLayout : IDisposable
{
    private readonly Control _owner;
    private readonly Viewport _viewport;
    private readonly SceneTree _tree;
    private readonly Action _fit;
    private readonly List<Control> _watched = new();
    private bool _queued, _running, _disposed;
    private int _remaining;
    private readonly Color _visibleColor;

    internal DeferredHudLayout(Control owner, Action fit)
    {
        _owner = owner; _fit = fit;
        _tree = owner.GetTree(); _viewport = owner.GetViewport();
        _visibleColor = Colors.White;
        _viewport.SizeChanged += Request;
        Watch(owner);
    }
    internal void Watch(Control control)
    {
        if (_watched.Contains(control)) return;
        _watched.Add(control);
        control.Resized += Request;
        control.MinimumSizeChanged += Request;
    }
    internal void Request()
    {
        if (_disposed || _queued || _running || !UiHelpers.IsValid(_owner)) return;
        _remaining = 6;
        _queued = true;
        _tree.ProcessFrame += OnFrame;
    }
    private void OnFrame()
    {
        if (_disposed || !UiHelpers.IsValid(_owner) || !_owner.IsInsideTree())
        { Finish(); return; }
        _running = true;
        try { _fit(); }
        catch (Exception ex)
        { ModLog.Error("HUD.DeferredLayout", ex); _remaining = 1; }
        finally { _running = false; }
        if (--_remaining <= 0) Finish();
    }
    private void Finish()
    {
        if (_queued && UiHelpers.IsValid(_tree)) _tree.ProcessFrame -= OnFrame;
        _queued = false;
        if (UiHelpers.IsValid(_owner)) _owner.Modulate = _visibleColor;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Finish();
        if (UiHelpers.IsValid(_viewport)) _viewport.SizeChanged -= Request;
        foreach (Control control in _watched)
        {
            if (!UiHelpers.IsValid(control)) continue;
            control.Resized -= Request;
            control.MinimumSizeChanged -= Request;
        }
        _watched.Clear();
    }
}
