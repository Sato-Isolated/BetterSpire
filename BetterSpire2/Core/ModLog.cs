#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.IO;

namespace BetterSpire2.Core;

public static class ModLog
{
    private static string? _logPath;

    private static readonly object _lock = new object();

    private static string LogPath => _logPath ?? (_logPath = Path.Combine(OS.GetUserDataDir(), "betterspire2_log.txt"));

    public static void Init()
    {
        try
        {
            File.WriteAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] BetterSpire2 log started\n  OS: {OS.GetName()} / {OS.GetDistributionName()}\n  Godot: {Engine.GetVersionInfo()["string"]}\n");
        }
        catch
        {
        }
    }

    public static void Info(string message)
    {
        try
        {
            lock (_lock)
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
        }
        catch
        {
        }
    }

    public static void Error(string context, Exception ex)
    {
        try
        {
            lock (_lock)
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] ERROR in {context}: {ex}\n");
            }
        }
        catch
        {
        }
    }

    private static readonly Dictionary<string, ulong> LastThrottled = new(StringComparer.Ordinal);

    /// <summary>Per-frame paths: logs at most one error per context every 10 seconds.</summary>
    public static void ErrorThrottled(string context, Exception ex)
    {
        ulong now = Time.GetTicksMsec();
        lock (_lock)
        {
            if (LastThrottled.TryGetValue(context, out ulong last) && now - last < 10000) return;
            LastThrottled[context] = now;
        }
        Error(context, ex);
    }

    /// <summary>Runs one isolated step; a failure is logged and never reaches the caller, which is
    /// often native game code (Harmony prefixes, native events).</summary>
    public static void Guard(string context, Action action)
    {
        try { action(); }
        catch (Exception ex) { Error(context, ex); }
    }
}
