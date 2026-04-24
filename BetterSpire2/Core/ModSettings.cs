#nullable enable
using System;
using System.IO;
using System.Text.Json;
using Godot;

namespace BetterSpire2.Core;

public static class ModSettings
{
private sealed class PositionData
{
public float X { get; set; }

public float Y { get; set; }

public Vector2 ToVector2()
{
return new Vector2(X, Y);
}

public static PositionData? FromVector2(Vector2? position)
{
if (!position.HasValue)
{
return null;
}

return new PositionData
{
X = position.Value.X,
Y = position.Value.Y
};
}
}

private sealed class SettingsData
{
public bool MultiHitTotals { get; set; } = true;

public bool PlayerDamageTotal { get; set; } = true;

public bool ShowExpectedHp { get; set; } = true;

public bool ShowTurnSummary { get; set; } = true;

public bool SkipSplash { get; set; } = true;

public bool ShowTeammateHand { get; set; } = true;

public bool AlwaysShowTeammateHand { get; set; }

public bool AutoShowTeammateHand { get; set; }

public bool HideOwnHand { get; set; }

public bool InstantFastMode { get; set; }

public bool CompactHandViewer { get; set; }

public int CardScalePercent { get; set; } = 100;

public bool ShowClock { get; set; }

public bool Clock24Hour { get; set; }

public PositionData? SettingsMenuPosition { get; set; }

public PositionData? ClockPosition { get; set; }

public PositionData? HandViewerPosition { get; set; }

public PositionData? HandViewerSize { get; set; }

public PositionData? TurnSummaryPosition { get; set; }

public PositionData? TurnSummarySize { get; set; }

public bool TurnSummaryCollapsed { get; set; }
}

private static readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions
{
WriteIndented = true
};

private static SettingsData _data = new SettingsData();

private static string SettingsPath => Path.Combine(OS.GetUserDataDir(), "betterspire2_settings.json");

public static bool MultiHitTotals
{
get => _data.MultiHitTotals;
set => _data.MultiHitTotals = value;
}

public static bool PlayerDamageTotal
{
get => _data.PlayerDamageTotal;
set => _data.PlayerDamageTotal = value;
}

public static bool ShowExpectedHp
{
get => _data.ShowExpectedHp;
set => _data.ShowExpectedHp = value;
}

public static bool ShowTurnSummary
{
get => _data.ShowTurnSummary;
set => _data.ShowTurnSummary = value;
}

public static bool SkipSplash
{
get => _data.SkipSplash;
set => _data.SkipSplash = value;
}

public static bool ShowTeammateHand
{
get => _data.ShowTeammateHand;
set => _data.ShowTeammateHand = value;
}

public static bool AlwaysShowTeammateHand
{
get => _data.AlwaysShowTeammateHand;
set => _data.AlwaysShowTeammateHand = value;
}

public static bool AutoShowTeammateHand
{
get => _data.AutoShowTeammateHand;
set => _data.AutoShowTeammateHand = value;
}

public static bool HideOwnHand
{
get => _data.HideOwnHand;
set => _data.HideOwnHand = value;
}

public static bool InstantFastMode
{
get => _data.InstantFastMode;
set => _data.InstantFastMode = value;
}

public static bool CompactHandViewer
{
get => _data.CompactHandViewer;
set => _data.CompactHandViewer = value;
}

public static int CardScalePercent
{
get => _data.CardScalePercent;
set => _data.CardScalePercent = Math.Clamp(value, 50, 200);
}

public static bool ShowClock
{
get => _data.ShowClock;
set => _data.ShowClock = value;
}

public static bool Clock24Hour
{
get => _data.Clock24Hour;
set => _data.Clock24Hour = value;
}

public static Vector2? SettingsMenuPosition
{
get => _data.SettingsMenuPosition?.ToVector2();
set => _data.SettingsMenuPosition = PositionData.FromVector2(value);
}

public static Vector2? ClockPosition
{
get => _data.ClockPosition?.ToVector2();
set => _data.ClockPosition = PositionData.FromVector2(value);
}

public static Vector2? HandViewerPosition
{
get => _data.HandViewerPosition?.ToVector2();
set => _data.HandViewerPosition = PositionData.FromVector2(value);
}

public static Vector2? HandViewerSize
{
get => _data.HandViewerSize?.ToVector2();
set => _data.HandViewerSize = PositionData.FromVector2(value);
}

public static Vector2? TurnSummaryPosition
{
get => _data.TurnSummaryPosition?.ToVector2();
set => _data.TurnSummaryPosition = PositionData.FromVector2(value);
}

public static Vector2? TurnSummarySize
{
get => _data.TurnSummarySize?.ToVector2();
set => _data.TurnSummarySize = PositionData.FromVector2(value);
}

public static bool TurnSummaryCollapsed
{
get => _data.TurnSummaryCollapsed;
set => _data.TurnSummaryCollapsed = value;
}

private static void Normalize()
{
_data.CardScalePercent = Math.Clamp(_data.CardScalePercent, 50, 200);
}

public static void Load()
{
try
{
_data = new SettingsData();
if (!File.Exists(SettingsPath))
{
Normalize();
return;
}

SettingsData? loadedSettings = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(SettingsPath));
if (loadedSettings != null)
{
_data = loadedSettings;
}

Normalize();
}
catch (Exception ex)
{
ModLog.Error("ModSettings.Load", ex);
_data = new SettingsData();
}
}

public static void Save()
{
try
{
Normalize();
File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_data, _serializerOptions));
}
catch (Exception ex)
{
ModLog.Error("ModSettings.Save", ex);
}
}
}
