#nullable enable
using Godot;
using System;
using System.IO;
using System.Text.Json;

namespace BetterSpire2.Core;

public static class ModSettings
{
    private sealed class PositionData
    {
        public float X { get; set; }
        public float Y { get; set; }

        public Vector2 ToVector2() => new(X, Y);

        public static PositionData? FromVector2(Vector2? value)
        {
            return value.HasValue ? new PositionData { X = value.Value.X, Y = value.Value.Y } : null;
        }
    }

    private sealed class SettingsData
    {
        public int LayoutVersion { get; set; } = 2;

        public bool MultiHitTotals { get; set; } = true;
        public bool PlayerDamageTotal { get; set; } = true;
        public bool ShowTurnSummary { get; set; } = false;
        public bool SkipSplash { get; set; } = true;
        public bool ShowTeammateHand { get; set; } = true;
        public bool AutoShowTeammateHand { get; set; }
        public bool HideOwnHand { get; set; } = true;
        public bool InstantFastMode { get; set; }
        public bool CompactHandViewer { get; set; }
        public int CardScalePercent { get; set; } = 100;
        public bool ShowClock { get; set; }
        public bool Clock24Hour { get; set; }
        public bool GuardianOnlyDanger { get; set; }
        public bool GuardianShowTeammates { get; set; } = true;
        public bool GuardianShowPets { get; set; } = true;
        public int GuardianOverheadGap { get; set; } = 16;
        public int GuardianOverheadOffsetX { get; set; }
        public int GuardianScalePercent { get; set; } = 100;
        public int GuardianXPercent { get; set; } = 1;
        public int GuardianYPercent { get; set; } = 14;

        public int DamageMeterDockingVersion { get; set; }
        public bool DamageMeterLocked { get; set; }
        public PositionData? DamageMeterNormalizedPosition { get; set; }
        public bool ShowDamageMeter { get; set; } = true;
        public bool DamageMeterIncludeBlock { get; set; } = false;
        public bool DamageMeterShowBars { get; set; } = true;
        public bool DamageMeterOnlyCombat { get; set; } = false;
        public int DamageMeterScalePercent { get; set; } = 100;
        public int DamageMeterXPercent { get; set; } = 100;
        public int DamageMeterYPercent { get; set; } = 12;

        // Legacy pixel layouts retained so existing settings migrate cleanly.
        public PositionData? SettingsMenuPosition { get; set; }
        public PositionData? ClockPosition { get; set; }
        public PositionData? HandViewerPosition { get; set; }
        public PositionData? HandViewerSize { get; set; }

        // Responsive v2 layouts.
        public PositionData? SettingsMenuNormalizedPosition { get; set; }
        public PositionData? ClockNormalizedPosition { get; set; }
        public PositionData? ClockSizeV2 { get; set; }
        public PositionData? HandViewerNormalizedPosition { get; set; }
        public PositionData? HandViewerSizeV2 { get; set; }
        public PositionData? HandViewerCompactSize { get; set; }
        public PositionData? HandViewerExpandedSize { get; set; }
    }

    private static readonly JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };
    private static SettingsData _data = new();
    private static bool _loadedFromFile;
    internal static bool NeedsDamageMeterMigration => _loadedFromFile && _data.DamageMeterDockingVersion < 1;

    private static string SettingsPath => Path.Combine(OS.GetUserDataDir(), "betterspire2_settings.json");

    public static bool MultiHitTotals { get => _data.MultiHitTotals; set => _data.MultiHitTotals = value; }
    public static bool PlayerDamageTotal { get => _data.PlayerDamageTotal; set => _data.PlayerDamageTotal = value; }
    public static bool ShowTurnSummary { get => _data.ShowTurnSummary; set => _data.ShowTurnSummary = value; }
    public static bool SkipSplash { get => _data.SkipSplash; set => _data.SkipSplash = value; }
    public static bool ShowTeammateHand { get => _data.ShowTeammateHand; set => _data.ShowTeammateHand = value; }
    public static bool AutoShowTeammateHand { get => _data.AutoShowTeammateHand; set => _data.AutoShowTeammateHand = value; }
    public static bool HideOwnHand { get => _data.HideOwnHand; set => _data.HideOwnHand = value; }
    public static bool InstantFastMode { get => _data.InstantFastMode; set => _data.InstantFastMode = value; }
    public static bool CompactHandViewer { get => _data.CompactHandViewer; set => _data.CompactHandViewer = value; }
    public static int CardScalePercent { get => _data.CardScalePercent; set => _data.CardScalePercent = Math.Clamp(value, 50, 200); }
    public static bool ShowClock { get => _data.ShowClock; set => _data.ShowClock = value; }
    public static bool Clock24Hour { get => _data.Clock24Hour; set => _data.Clock24Hour = value; }

    public static bool GuardianOnlyDanger { get => _data.GuardianOnlyDanger; set => _data.GuardianOnlyDanger = value; }
    public static bool GuardianShowTeammates { get => _data.GuardianShowTeammates; set => _data.GuardianShowTeammates = value; }
    public static bool GuardianShowPets { get => _data.GuardianShowPets; set => _data.GuardianShowPets = value; }
    public static int GuardianOverheadGap { get => Math.Clamp(_data.GuardianOverheadGap, 8, 160); set => _data.GuardianOverheadGap = Math.Clamp(value, 8, 160); }
    public static int GuardianOverheadOffsetX { get => Math.Clamp(_data.GuardianOverheadOffsetX, -160, 160); set => _data.GuardianOverheadOffsetX = Math.Clamp(value, -160, 160); }
    public static int GuardianScalePercent { get => Math.Clamp(_data.GuardianScalePercent, 75, 150); set => _data.GuardianScalePercent = Math.Clamp(value, 75, 150); }
    public static int GuardianXPercent { get => Math.Clamp(_data.GuardianXPercent, 0, 100); set => _data.GuardianXPercent = Math.Clamp(value, 0, 100); }
    public static int GuardianYPercent { get => Math.Clamp(_data.GuardianYPercent, 0, 100); set => _data.GuardianYPercent = Math.Clamp(value, 0, 100); }

    public static bool DamageMeterLocked { get => _data.DamageMeterLocked; set => _data.DamageMeterLocked = value; }
    public static bool ShowDamageMeter { get => _data.ShowDamageMeter; set => _data.ShowDamageMeter = value; }
    public static bool DamageMeterIncludeBlock { get => _data.DamageMeterIncludeBlock; set => _data.DamageMeterIncludeBlock = value; }
    public static bool DamageMeterShowBars { get => _data.DamageMeterShowBars; set => _data.DamageMeterShowBars = value; }
    public static bool DamageMeterOnlyCombat { get => _data.DamageMeterOnlyCombat; set => _data.DamageMeterOnlyCombat = value; }
    public static int DamageMeterScalePercent { get => Math.Clamp(_data.DamageMeterScalePercent, 75, 150); set => _data.DamageMeterScalePercent = Math.Clamp(value, 75, 150); }
    public static int DamageMeterXPercent { get => Math.Clamp(_data.DamageMeterXPercent, 0, 100); set => _data.DamageMeterXPercent = Math.Clamp(value, 0, 100); }
    public static int DamageMeterYPercent { get => Math.Clamp(_data.DamageMeterYPercent, 0, 100); set => _data.DamageMeterYPercent = Math.Clamp(value, 0, 100); }

    public static long Revision { get; private set; }
    internal static bool LastSaveSucceeded { get; private set; } = true;

    public static OverlayLayoutState GetDamageMeterLayout() => _data.DamageMeterNormalizedPosition is { } position
        ? new OverlayLayoutState(position.ToVector2(), true, true, Vector2.Zero, false)
        : OverlayLayoutState.FromLegacy(null, null);

    public static void ClearDamageMeterPosition()
    {
        _data.DamageMeterNormalizedPosition = null;
        _data.DamageMeterDockingVersion = 1;
    }

    public static void SaveDamageMeterLayout(OverlayLayoutState layout)
    {
        if (!layout.HasPosition) return;
        _data.DamageMeterDockingVersion = 1;
        _data.DamageMeterNormalizedPosition = PositionData.FromVector2(layout.Position);
        _data.DamageMeterXPercent = (int)MathF.Round(layout.Position.X * 100);
        _data.DamageMeterYPercent = (int)MathF.Round(layout.Position.Y * 100);
        Save();
    }

    public static OverlayLayoutState GetSettingsMenuLayout()
    {
        return ReadLayout(_data.SettingsMenuNormalizedPosition, null, _data.SettingsMenuPosition, null);
    }

    public static void SaveSettingsMenuLayout(OverlayLayoutState layout)
    {
        SaveLayout(layout, value => _data.SettingsMenuNormalizedPosition = value, null);
    }

    public static OverlayLayoutState GetClockLayout()
    {
        return ReadLayout(_data.ClockNormalizedPosition, _data.ClockSizeV2, _data.ClockPosition, null);
    }

    public static void SaveClockLayout(OverlayLayoutState layout)
    {
        SaveLayout(layout, value => _data.ClockNormalizedPosition = value, value => _data.ClockSizeV2 = value);
    }

    public static OverlayLayoutState GetHandViewerLayout()
    {
        PositionData? size = _data.CompactHandViewer
            ? _data.HandViewerCompactSize
            : _data.HandViewerExpandedSize ?? _data.HandViewerSizeV2;
        return ReadLayout(
            _data.HandViewerNormalizedPosition,
            size,
            _data.HandViewerPosition,
            _data.CompactHandViewer ? null : _data.HandViewerSize);
    }

    public static void SaveHandViewerLayout(OverlayLayoutState layout)
    {
        SaveLayout(
            layout,
            value => _data.HandViewerNormalizedPosition = value,
            value =>
            {
                if (_data.CompactHandViewer)
                {
                    _data.HandViewerCompactSize = value;
                }
                else
                {
                    _data.HandViewerExpandedSize = value;
                }
            });
    }

    public static void ResetUiLayout()
    {
        ClearDamageMeterPosition();
        _data.CardScalePercent = 100;
        _data.DamageMeterScalePercent = 100;
        _data.DamageMeterXPercent = 100;
        _data.DamageMeterYPercent = 12;
        _data.GuardianXPercent = 1;
        _data.GuardianYPercent = 14;
        _data.GuardianScalePercent = 100;
        _data.GuardianOverheadGap = 16;
        _data.GuardianOverheadOffsetX = 0;
        _data.LayoutVersion = 2;
        _data.SettingsMenuPosition = null;
        _data.ClockPosition = null;
        _data.HandViewerPosition = null;
        _data.HandViewerSize = null;
        _data.SettingsMenuNormalizedPosition = null;
        _data.ClockNormalizedPosition = null;
        _data.ClockSizeV2 = null;
        _data.HandViewerNormalizedPosition = null;
        _data.HandViewerSizeV2 = null;
        _data.HandViewerCompactSize = null;
        _data.HandViewerExpandedSize = null;
        Save();
    }

    private static OverlayLayoutState ReadLayout(
        PositionData? normalizedPosition,
        PositionData? responsiveSize,
        PositionData? legacyPosition,
        PositionData? legacySize)
    {
        if (normalizedPosition != null)
        {
            return new OverlayLayoutState(
                normalizedPosition.ToVector2(),
                true,
                true,
                responsiveSize?.ToVector2() ?? Vector2.Zero,
                responsiveSize != null);
        }

        return OverlayLayoutState.FromLegacy(legacyPosition?.ToVector2(), (responsiveSize ?? legacySize)?.ToVector2());
    }

    private static void SaveLayout(
        OverlayLayoutState layout,
        Action<PositionData?> savePosition,
        Action<PositionData?>? saveSize)
    {
        _data.LayoutVersion = 2;
        savePosition(layout.HasPosition ? PositionData.FromVector2(layout.Position) : null);
        saveSize?.Invoke(layout.HasSize ? PositionData.FromVector2(layout.Size) : null);
        Save();
    }

    private static void Normalize()
    {
        _data.DamageMeterScalePercent = Math.Clamp(_data.DamageMeterScalePercent, 75, 150);
        _data.DamageMeterXPercent = Math.Clamp(_data.DamageMeterXPercent, 0, 100);
        _data.DamageMeterYPercent = Math.Clamp(_data.DamageMeterYPercent, 0, 100);
        _data.CardScalePercent = Math.Clamp(_data.CardScalePercent, 50, 200);
        _data.GuardianScalePercent = Math.Clamp(_data.GuardianScalePercent, 75, 150);
        _data.GuardianOverheadGap = Math.Clamp(_data.GuardianOverheadGap, 8, 160);
        _data.GuardianOverheadOffsetX = Math.Clamp(_data.GuardianOverheadOffsetX, -160, 160);
        _data.GuardianXPercent = Math.Clamp(_data.GuardianXPercent, 0, 100);
        _data.GuardianYPercent = Math.Clamp(_data.GuardianYPercent, 0, 100);
    }

    public static void Load()
    {
        try
        {
            _data = new SettingsData();
            _loadedFromFile = File.Exists(SettingsPath);
            if (_loadedFromFile)
            {
                SettingsData? loaded = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(SettingsPath));
                if (loaded != null)
                {
                    _data = loaded;
                }
            }
            Normalize();
            Revision++;
        }
        catch (Exception ex)
        {
            ModLog.Error("ModSettings.Load", ex);
            _data = new SettingsData();
            _loadedFromFile = false;
            Revision++;
        }
    }

    public static void Save()
    {
        Revision++; // UI state changes even when persistence fails.
        try
        {
            Normalize();
            if (!_loadedFromFile) _data.DamageMeterDockingVersion = 1;
            string json = JsonSerializer.Serialize(_data, _serializerOptions);
            string temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, SettingsPath, overwrite: true);
            LastSaveSucceeded = true;
        }
        catch (Exception ex)
        {
            LastSaveSucceeded = false;
            ModLog.Error("ModSettings.Save", ex);
        }
    }
}
