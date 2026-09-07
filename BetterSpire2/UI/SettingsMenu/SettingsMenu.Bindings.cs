#nullable enable
using System.Collections.Generic;
using Godot;

namespace BetterSpire2.UI;

public static partial class SettingsMenu
{
    private static readonly List<SettingRowBinding> _settingBindings = new();
    private static long _bindingRevision = -1;
    private static bool _bindingFrench, _saveFailureShown;

    internal static void RefreshBindings()
    {
        if (!_isVisible) return;
        RefreshPartyBindings();
        if (_bindingFrench != ModText.IsFrench)
        {
            _bindingFrench = ModText.IsFrench;
            // Rebuild labels in the selected language, preserving the saved layout.
            Hide(); Show();
            return;
        }
        if (_bindingRevision == ModSettings.Revision) return;
        _bindingRevision = ModSettings.Revision;
        foreach (var binding in _settingBindings) binding.Refresh();
        if (!ModSettings.LastSaveSucceeded)
            ShowStatus(ModText.T("Settings could not be saved. Changes are active for this session only."), success: false);
        else if (_saveFailureShown) ShowStatus(ModText.T("Settings saved."));
        _saveFailureShown = !ModSettings.LastSaveSucceeded;
        _autoFit?.Request();
    }

    private sealed class SettingRowBinding
    {
        private readonly ModSettingDefinition _definition;
        private readonly Label _title, _description;
        internal HudToggle? Toggle;
        internal Button? Decrease, Increase;
        internal Label? Value;
        internal SettingRowBinding(ModSettingDefinition definition, Label title, Label description)
        { _definition = definition; _title = title; _description = description; }

        internal void Refresh()
        {
            bool enabled = _definition.IsEnabled;
            _title.AddThemeColorOverride("font_color", enabled ? HudTheme.Text : HudTheme.Disabled);
            _description.Text = ModText.T(_definition.Description) +
                (enabled ? "" : "\n" + _definition.DisabledReason);
            if (Toggle != null)
            {
                Toggle.SetState(_definition.ToggleValue, enabled,
                    enabled ? ModText.T(_definition.Description) : _definition.DisabledReason);
            }
            else if (Decrease != null && Increase != null && Value != null)
            {
                int value = _definition.SliderValue;
                Value.Text = _definition.FormatValue(value);
                Value.AddThemeColorOverride("font_color", enabled ? HudTheme.Accent : HudTheme.Disabled);
                Decrease.Disabled = !enabled || value <= _definition.Min;
                Increase.Disabled = !enabled || value >= _definition.Max;
            }
        }
    }
}
