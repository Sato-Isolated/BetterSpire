#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace BetterSpire2.Runtime;

internal enum ProbeSection { Forecast, GuardianUi, OverheadPlacement, Journal, DamageMeter, HandViewer, JournalCheckpoint, IntentLabel }

/// <summary>Opt-in main-thread measurements. No frame listener, stopwatch or report allocation while disabled.</summary>
internal static class PerformanceProbe
{
    private struct Counter { internal long Calls, Ticks, MaxTicks, Bytes; }
    private static readonly Counter[] Counters = new Counter[8];
    private static readonly long[] FrameHistogram = new long[2001]; // 0.25 ms bins, last bin >= 500 ms.
    private static SceneTree? _tree;
    private static bool _enabled;
    private static int _generation;
    private static long _frames, _frameTicks, _maxFrameTicks, _lastFrame;
    private static DateTime _startedUtc;
    private static readonly int[] _gcStart = new int[3];
    private static readonly int[] _gcEnd = new int[3];

    internal readonly struct Scope : IDisposable
    {
        private readonly ProbeSection _section;
        private readonly int _generation;
        private readonly long _start, _bytes;
        internal Scope(ProbeSection section)
        {
            _section = section; _generation = PerformanceProbe._generation;
            _start = Stopwatch.GetTimestamp(); _bytes = GC.GetAllocatedBytesForCurrentThread();
        }
        public void Dispose()
        {
            if (_start == 0 || !_enabled || _generation != PerformanceProbe._generation) return;
            long elapsed = Stopwatch.GetTimestamp() - _start;
            ref var counter = ref Counters[(int)_section];
            counter.Calls++; counter.Ticks += elapsed; counter.MaxTicks = Math.Max(counter.MaxTicks, elapsed);
            counter.Bytes += Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - _bytes);
        }
    }
    internal static Scope Measure(ProbeSection section) => _enabled ? new Scope(section) : default;
    internal static void Toggle()
    {
        if (_enabled) { Stop(); ModLog.Info("Performance recording stopped. Ctrl+F7 exports the recording."); return; }
        var game = NGame.Instance;
        if (game == null || !GodotObject.IsInstanceValid(game) || !game.IsInsideTree()) return;
        Array.Clear(Counters); Array.Clear(FrameHistogram);
        _frames = _frameTicks = _maxFrameTicks = _lastFrame = 0;
        for (int i = 0; i < 3; i++) _gcEnd[i] = _gcStart[i] = GC.CollectionCount(i);
        _generation++; _startedUtc = DateTime.UtcNow;
        _tree = game.GetTree(); _tree.ProcessFrame += SampleFrame; _enabled = true;
        ModLog.Info("Performance recording started. F7 stops; Ctrl+F7 exports. No on-screen panel is added.");
    }
    private static void SampleFrame()
    {
        long now = Stopwatch.GetTimestamp();
        if (_lastFrame != 0)
        {
            long elapsed = now - _lastFrame;
            _frames++; _frameTicks += elapsed; _maxFrameTicks = Math.Max(_maxFrameTicks, elapsed);
            double ms = Milliseconds(elapsed);
            FrameHistogram[Math.Clamp((int)(ms * 4), 0, FrameHistogram.Length - 1)]++;
        }
        _lastFrame = now;
    }
    internal static void Stop()
    {
        if (_enabled) for (int i = 0; i < 3; i++) _gcEnd[i] = GC.CollectionCount(i);
        if (_tree != null && GodotObject.IsInstanceValid(_tree)) _tree.ProcessFrame -= SampleFrame;
        _tree = null; _enabled = false; _lastFrame = 0;
    }
    internal static void Export()
    {
        if (_startedUtc == default) { ModLog.Info("Press F7 to start a performance recording first."); return; }
        try
        {
            var sections = new object[Counters.Length];
            for (int i = 0; i < sections.Length; i++)
            {
                var c = Counters[i];
                sections[i] = new { Name = ((ProbeSection)i).ToString(), c.Calls,
                    TotalMs = Milliseconds(c.Ticks), MeanMs = c.Calls == 0 ? 0 : Milliseconds(c.Ticks) / c.Calls,
                    MaxMs = Milliseconds(c.MaxTicks), MainThreadAllocatedBytes = c.Bytes };
            }
            var gcCollections = new int[3];
            for (int i = 0; i < 3; i++) gcCollections[i] = (_enabled ? GC.CollectionCount(i) : _gcEnd[i]) - _gcStart[i];
            string path = Path.Combine(OS.GetUserDataDir(), "betterspire_performance.json");
            var report = new { Version = "3.6.0-v111", StartedUtc = _startedUtc,
                Recording = _enabled, FrameCount = _frames,
                MeanFrameMs = _frames == 0 ? 0 : Milliseconds(_frameTicks) / _frames,
                MeanFps = _frameTicks == 0 ? 0 : _frames * (double)Stopwatch.Frequency / _frameTicks,
                P95FrameMsUpperBound = Percentile95(), MaxFrameMs = Milliseconds(_maxFrameTicks),
                GcCollections = gcCollections, Sections = sections,
                Notes = "Whole-frame timing includes the game, other mods, loading and window stalls. Section times overlap (Journal includes meter/checkpoint). Not a GPU profiler; no additive attribution of total frame time." };
            File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            ModLog.Info("Performance report exported: " + path);
        }
        catch (Exception ex) { ModLog.Error("Performance.Export", ex); }
    }
    private static double Percentile95()
    {
        if (_frames == 0) return 0;
        long target = (long)Math.Ceiling(_frames * .95), sum = 0;
        for (int i = 0; i < FrameHistogram.Length; i++)
        {
            sum += FrameHistogram[i];
            if (sum >= target) return i == FrameHistogram.Length - 1 ? Milliseconds(_maxFrameTicks) : (i + 1) / 4d;
        }
        return Milliseconds(_maxFrameTicks);
    }
    private static double Milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
}
