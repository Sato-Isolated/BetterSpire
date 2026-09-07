#nullable enable
using System;

namespace BetterSpire2.Core;

internal sealed class ModSettingDefinition
{
    private readonly Func<bool>? _getToggle;
    private readonly Action<bool>? _setToggle;
    private readonly Func<int>? _getSlider;
    private readonly Action<int>? _setSlider;
    private readonly Action? _afterChange;

    private ModSettingDefinition(
        string key,
        string label,
        string description,
        ModSettingKind kind,
        Func<bool>? getToggle,
        Action<bool>? setToggle,
        Func<int>? getSlider,
        Action<int>? setSlider,
        Action? afterChange,
        int min,
        int max,
        int step)
    {
        Key = key;
        Label = label;
        Description = description;
        Kind = kind;
        _getToggle = getToggle;
        _setToggle = setToggle;
        _getSlider = getSlider;
        _setSlider = setSlider;
        _afterChange = afterChange;
        Min = min;
        Max = max;
        Step = step;
    }

    internal string Key { get; }
    internal string Label { get; }
    internal string Description { get; }
    internal ModSettingKind Kind { get; }
    internal int Min { get; }
    internal int Max { get; }
    internal int Step { get; }
    internal string Unit => Key is "GuardianOverheadGap" or "GuardianOverheadOffsetX" ? "px" : "%";
    internal bool IsEnabled => ModSettingPolicy.DisabledReason(Key, ReadToggle).Length == 0;
    internal string DisabledReason => ModText.T(ModSettingPolicy.DisabledReason(Key, ReadToggle));
    internal string FormatValue(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + Unit;
    private static bool ReadToggle(string key) => key switch
    {
        "PlayerDamageTotal" => ModSettings.PlayerDamageTotal,
        "ShowDamageMeter" => ModSettings.ShowDamageMeter,
        "ShowTeammateHand" => ModSettings.ShowTeammateHand,
        "CompactHandViewer" => ModSettings.CompactHandViewer,
        "ShowClock" => ModSettings.ShowClock,
        _ => false
    };
    internal bool ToggleValue => _getToggle?.Invoke() == true;
    internal int SliderValue => _getSlider?.Invoke() ?? Min;

    internal void Apply(bool value)
    {
        if (Kind != ModSettingKind.Toggle) throw new InvalidOperationException("Not a toggle: " + Key);
        if (ToggleValue == value) return;
        _setToggle?.Invoke(value);
        SaveAndRefresh();
    }

    internal void Apply(int value)
    {
        if (Kind != ModSettingKind.Slider) throw new InvalidOperationException("Not a slider: " + Key);
        value = Math.Clamp(value, Min, Max);
        if (SliderValue == value) return;
        _setSlider?.Invoke(value);
        SaveAndRefresh();
    }

    private void SaveAndRefresh()
    {
        ModSettings.Save();
        _afterChange?.Invoke();
    }

    internal static ModSettingDefinition Toggle(
        string key,
        string label,
        string description,
        Func<bool> getter,
        Action<bool> setter,
        Action? afterChange = null)
    {
        return new ModSettingDefinition(key, label, description, ModSettingKind.Toggle, getter, setter, null, null, afterChange, 0, 1, 1);
    }

    internal static ModSettingDefinition Slider(
        string key,
        string label,
        string description,
        Func<int> getter,
        Action<int> setter,
        int min,
        int max,
        int step,
        Action? afterChange = null)
    {
        return new ModSettingDefinition(key, label, description, ModSettingKind.Slider, null, null, getter, setter, afterChange, min, max, step);
    }
}
