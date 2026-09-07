#nullable enable
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterSpire2.UI;

public static partial class SettingsMenu
{
    private static VBoxContainer? _partyRoot;
    private static string _partyStamp = "";
    private static ulong _nextPartyRefresh;
    private static void RefreshPartyBindings()
    {
        if (!_isVisible || _activeSection != ModSettingSectionId.Multiplayer || !UiHelpers.IsValid(_partyRoot)) return;
        ulong now = Time.GetTicksMsec();
        if (now < _nextPartyRefresh) return;
        _nextPartyRefresh = now + 500;
        var manager = RunManager.Instance;
        var players = manager?.DebugOnlyGetState()?.Players;
        string stamp = (manager?.NetService?.NetId.ToString() ?? "none") + ":" +
            (manager?.IsSingleplayerOrFakeMultiplayer.ToString() ?? "none") + ":" +
            (players == null ? "none" : string.Join(",", players.Select(player => player.NetId + "=" + PlayerDisplayNames.Nickname(player)))) + ":" +
            PartyManager.Revision + ":" + PartyManager.CanClearDrawings;
        if (_partyStamp == stamp) return;
        _partyStamp = stamp;
        ClearContainer(_partyRoot!);
        BuildPartySection(_partyRoot!);
        _autoFit?.Request();
    }
    private static void BuildActiveSection()
    {
        if (!UiHelpers.IsValid(_contentRoot))
        {
            return;
        }

        _settingBindings.Clear();
        _partyRoot = null; _partyStamp = ""; _nextPartyRefresh = 0;
        ClearContainer(_contentRoot!);
        ModSettingSectionDefinition section = ModSettingsCatalog.Sections.First(item => item.Id == _activeSection);
        BuildSectionIntroduction(_contentRoot!, section);

        foreach (ModSettingDefinition definition in section.Settings)
        {
            _contentRoot!.AddChild(CreateSettingRow(definition), forceReadableName: false, Node.InternalMode.Disabled);
        }

        if (section.Id == ModSettingSectionId.Multiplayer)
        {
            _partyRoot = new VBoxContainer();
            _partyRoot.AddThemeConstantOverride("separation", 8);
            _contentRoot!.AddChild(_partyRoot);
            // Show() sets visibility after initial construction; subsequent heartbeat fills this section.
            RefreshPartyBindings();
        }
        else if (section.Id == ModSettingSectionId.Gameplay)
        {
            BuildResetLayoutRow(_contentRoot!);
        }
        if (UiHelpers.IsValid(_settingsScroll)) _settingsScroll!.ScrollVertical = 0;
        _bindingRevision = -1;
        _autoFit?.Request();
    }

    private static void BuildSectionIntroduction(VBoxContainer parent, ModSettingSectionDefinition section)
    {
        VBoxContainer introduction = new();
        introduction.AddThemeConstantOverride("separation", 3);
        parent.AddChild(introduction, forceReadableName: false, Node.InternalMode.Disabled);

        var heading = UiHelpers.CreateLabel(ModText.T(section.Heading), AccentColor, 18);
        heading.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        introduction.AddChild(heading);
        Label description = UiHelpers.CreateLabel(ModText.T(section.Description), MutedTextColor, 11);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        description.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        introduction.AddChild(description, forceReadableName: false, Node.InternalMode.Disabled);
    }

    private static PanelContainer CreateSettingRow(ModSettingDefinition definition)
    {
        var card = new PanelContainer();
        var style = UiHelpers.CreatePanelStyle(SurfaceColor, SubtleBorderColor, 1, HudTheme.Radius, 0);
        style.ContentMarginLeft = style.ContentMarginRight = 14;
        style.ContentMarginTop = style.ContentMarginBottom = 10;
        card.AddThemeStyleboxOverride("panel", style);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        card.AddChild(row);
        var copy = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        copy.AddThemeConstantOverride("separation", 4);
        row.AddChild(copy);
        var title = UiHelpers.CreateLabel(ModText.T(definition.Label), SecondaryTextColor, 14);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        copy.AddChild(title);
        var description = UiHelpers.CreateLabel("", MutedTextColor, 11);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        description.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        copy.AddChild(description);
        var binding = new SettingRowBinding(definition, title, description);
        if (definition.Kind == ModSettingKind.Toggle)
        {
            var toggle = new HudToggle();
            toggle.Button.Toggled += value => { definition.Apply(value); RefreshBindings(); };
            binding.Toggle = toggle;
            row.AddChild(toggle.Button);
        }
        else
        {
            var stepper = new HBoxContainer
            {
                CustomMinimumSize = new Vector2(CompactHudGeometry.SettingControlWidth, CompactHudGeometry.SettingControlHeight),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd
            };
            stepper.AddThemeConstantOverride("separation", 6);
            binding.Decrease = CreateCompactButton("−");
            binding.Increase = CreateCompactButton("+");
            binding.Value = UiHelpers.CreateLabel("", AccentColor, 12, HorizontalAlignment.Center);
            binding.Value.CustomMinimumSize = new Vector2(60, 0);
            binding.Value.VerticalAlignment = VerticalAlignment.Center;
            binding.Decrease.TooltipText = ModText.T("Decrease") + " · " + ModText.T(definition.Label);
            binding.Increase.TooltipText = ModText.T("Increase") + " · " + ModText.T(definition.Label);
            binding.Decrease.Pressed += () => { definition.Apply(definition.SliderValue - definition.Step); RefreshBindings(); };
            binding.Increase.Pressed += () => { definition.Apply(definition.SliderValue + definition.Step); RefreshBindings(); };
            stepper.AddChild(binding.Decrease);
            stepper.AddChild(binding.Value);
            stepper.AddChild(binding.Increase);
            row.AddChild(stepper);
        }
        _settingBindings.Add(binding);
        binding.Refresh();
        return card;
    }

    private static Button CreateCompactButton(string text)
    {
        var button = new Button
        {
            Text = text, CustomMinimumSize = new Vector2(30, 30),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
        };
        HudTheme.StyleButton(button, fontSize: 14, padding: 4);
        return button;
    }

    private static void BuildResetLayoutRow(VBoxContainer parent)
    {
        PanelContainer card = new();
        card.AddThemeStyleboxOverride("panel", UiHelpers.CreatePanelStyle(RaisedSurfaceColor, SubtleBorderColor, 1, 7, 12f));
        parent.AddChild(card, forceReadableName: false, Node.InternalMode.Disabled);

        VBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 8);
        card.AddChild(row, forceReadableName: false, Node.InternalMode.Disabled);

        VBoxContainer copy = new();
        copy.AddThemeConstantOverride("separation", 2);
        copy.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        copy.AddChild(UiHelpers.CreateLabel(ModText.T("Reset interface layout"), SecondaryTextColor, 15), forceReadableName: false, Node.InternalMode.Disabled);
        Label description = UiHelpers.CreateLabel(ModText.T("Reset saved positions and sizes for every BetterSpire overlay."), MutedTextColor, 11);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        copy.AddChild(description, forceReadableName: false, Node.InternalMode.Disabled);
        row.AddChild(copy, forceReadableName: false, Node.InternalMode.Disabled);

        Button reset = new();
        reset.Text = ModText.T("Reset interface layout");
        reset.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        ApplyButtonStyle(reset);
        reset.Pressed += () =>
        {
            ModSettings.ResetUiLayout();
            TeammateHandViewer.RefreshIfVisible();
            TeammateHandViewer.ResetLayout();
            JournalController.Refresh();
            ClockDisplay.ResetLayout();
            BetterSpire2.DamageMeter.DamageMeterController.ResetLayout();
            DamageTracker.Recalculate();
            RefreshBindings();
            CenterPanel();
            if (ModSettings.LastSaveSucceeded) ShowStatus(ModText.T("Interface layout reset"));
        };
        row.AddChild(reset, forceReadableName: false, Node.InternalMode.Disabled);
    }

    private static void BuildPartySection(VBoxContainer parent)
    {
        try
        {
            RunManager? manager = RunManager.Instance;
            if (manager == null || manager.IsSingleplayerOrFakeMultiplayer)
            {
                return;
            }

            INetGameService? netService = manager.NetService;
            RunState? state = manager.DebugOnlyGetState();
            if (netService == null || state?.Players == null || state.Players.Count <= 1)
            {
                return;
            }

            AddSectionHeader(parent, ModText.T("Party"));
            ulong localNetId = netService.NetId;
            foreach (Player player in state.Players)
            {
                parent.AddChild(CreatePartyRow(player, localNetId), forceReadableName: false, Node.InternalMode.Disabled);
            }

            Button clearAllButton = new();
            clearAllButton.Text = ModText.T("Clear all drawings");
            clearAllButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
            ApplyButtonStyle(clearAllButton, danger: true);
            clearAllButton.Disabled = !PartyManager.CanClearDrawings;
            clearAllButton.Pressed += PartyManager.ClearAllDrawings;
            parent.AddChild(clearAllButton, forceReadableName: false, Node.InternalMode.Disabled);
        }
        catch (Exception ex)
        {
            ModLog.Error("SettingsMenu.BuildPartySection", ex);
        }
    }

    private static PanelContainer CreatePartyRow(Player player, ulong localNetId)
    {
        bool isLocalPlayer = player.NetId == localNetId;
        string playerName = PlayerDisplayNames.Nickname(player);
        string? characterName = player.Character?.Title?.GetFormattedText();
        string displayName = string.IsNullOrEmpty(playerName)
            ? characterName ?? $"{ModText.T("Player")} {player.NetId}"
            : !string.IsNullOrEmpty(characterName)
                ? playerName + " (" + characterName + ")"
                : playerName;

        PanelContainer card = new();
        card.AddThemeStyleboxOverride("panel", UiHelpers.CreatePanelStyle(SurfaceColor, SubtleBorderColor, 1, 6, 9f));
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 10);
        card.AddChild(row, forceReadableName: false, Node.InternalMode.Disabled);

        string localSuffix = isLocalPlayer ? " · " + ModText.T("You") : string.Empty;
        Label label = UiHelpers.CreateLabel(displayName + localSuffix, isLocalPlayer ? SuccessColor : SecondaryTextColor, 13);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.ClipText = true;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.TooltipText = label.Text;
        label.MouseFilter = Control.MouseFilterEnum.Pass;
        row.AddChild(label, forceReadableName: false, Node.InternalMode.Disabled);

        if (!isLocalPlayer)
        {
            Button drawingButton = new();
            UpdateDrawingButtonText(drawingButton, player.NetId);

            drawingButton.Pressed += () =>
            {
                PartyManager.ToggleDrawingMute(player.NetId);
                if (PartyManager.IsDrawingMuted(player.NetId))
                {
                    PartyManager.ClearDrawingsForPlayer(player.NetId);
                }
                UpdateDrawingButtonText(drawingButton, player.NetId);
            };
            row.AddChild(drawingButton, forceReadableName: false, Node.InternalMode.Disabled);
        }

        return card;
    }

    private static void UpdateDrawingButtonText(Button button, ulong netId)
    {
        bool muted = PartyManager.IsDrawingMuted(netId);
        button.Text = ModText.T(muted ? "Show drawings" : "Hide drawings");
        button.TooltipText = ModText.T(muted ? "Drawings muted for this session." : "Drawings visible. Hiding is local to your client.");
        HudTheme.StyleButton(button, selected: muted);
    }

    private static void AddSectionHeader(VBoxContainer parent, string text)
    {
        VBoxContainer header = new();
        header.AddThemeConstantOverride("separation", 4);
        header.AddChild(UiHelpers.CreateLabel(text, AccentColor, 16), forceReadableName: false, Node.InternalMode.Disabled);
        HSeparator separator = new();
        separator.AddThemeStyleboxOverride("separator", UiHelpers.CreateSeparatorStyle(PanelBorderColor, 1f, 0f));
        header.AddChild(separator, forceReadableName: false, Node.InternalMode.Disabled);
        parent.AddChild(header, forceReadableName: false, Node.InternalMode.Disabled);
    }

    private static void ClearContainer(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }
}
