#nullable enable
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using System;

namespace BetterSpire2.UI;

public static partial class SettingsMenu
{
    private const float ViewportMargin = 24f;
    private const float MinimumPanelWidth = 320f;
    private const float MaximumPanelWidth = 720f;
    private const float MinimumPanelHeight = 160f;
    private const float MaximumPanelHeight = 760f;

    private static void Show()
    {
        if (_isVisible)
        {
            Hide();
        }

        NGame? game = NGame.Instance;
        if (game == null)
        {
            return;
        }

        _viewport = game.GetViewport();
        if (!UiHelpers.IsValid(_viewport))
        {
            return;
        }

        _canvasLayer = UiHelpers.CreateCanvasLayer(HudLayers.Settings);
        _panel = BuildPanel();
        _canvasLayer.AddChild(_panel, forceReadableName: false, Node.InternalMode.Disabled);
        game.AddChild(_canvasLayer, forceReadableName: false, Node.InternalMode.Disabled);

        Vector2 defaultSize = GetResponsivePanelSize();
        OverlayLayoutState savedLayout = ModSettings.GetSettingsMenuLayout();
        _settingsAnchor = savedLayout.HasPosition && savedLayout.PositionIsNormalized
            ? savedLayout.Position : new Vector2(.5f, .5f);
        _layoutController = new DockableOverlayController(
            _panel,
            _dragHandle!,
            _viewport!,
            game,
            new Vector2(MinimumPanelWidth, MinimumPanelHeight),
            new Vector2(MaximumPanelWidth, MaximumPanelHeight),
            canResize: false,
            loadLayout: ModSettings.GetSettingsMenuLayout,
            saveLayout: SaveSettingsLayout,
            viewportMargin: ViewportMargin);
        _layoutController.Initialize(defaultSize, static (safeRect, size) =>
            safeRect.Position + (safeRect.Size - size) * 0.5f);
        _isVisible = true;
        _bindingFrench = ModText.IsFrench;
        _bindingRevision = -1;
        RefreshBindings();
        _autoFit = new DeferredHudLayout(_panel, FitSettingsPanel);
        _autoFit.Watch(_outerRoot!);
        _autoFit.Watch(_contentRoot!);
        _autoFit.Request();
    }

    private static void SaveSettingsLayout(OverlayLayoutState layout)
    {
        if (layout.HasPosition && layout.PositionIsNormalized) _settingsAnchor = layout.Position;
        ModSettings.SaveSettingsMenuLayout(layout);
    }

    private static void FitSettingsPanel()
    {
        if (!UiHelpers.IsValid(_panel) || !UiHelpers.IsValid(_outerRoot) || !UiHelpers.IsValid(_contentRoot)
            || _layoutController == null || _layoutController.IsInteracting) return;
        Vector2 size = GetResponsivePanelSize();
        // The scroll container does not propagate the full list's minimum height.
        // Measure chrome and wrapped content separately, AFTER deferred container sorting.
        size.Y = CompactHudGeometry.SettingsHeight(GetViewportSize().Y,
            _outerRoot!.GetCombinedMinimumSize().Y, _contentRoot!.GetCombinedMinimumSize().Y);
        if (!_panel!.Size.IsEqualApprox(size))
            _layoutController.ApplyGeometry(new Vector2(MinimumPanelWidth, MinimumPanelHeight),
                new Vector2(MaximumPanelWidth, MaximumPanelHeight), size, preserveRelativePosition: false);
        // Keep the remembered anchor, not a position normalized against an intermediate oversized frame.
        _layoutController.SetNormalizedPosition(_settingsAnchor);
    }

    private static PanelContainer BuildPanel()
    {
        PanelContainer panel = new();
        panel.ZIndex = 200;
        panel.Modulate = new Color(1, 1, 1, 0); // Reveal only after the first deferred auto-fit.
        panel.AddThemeStyleboxOverride("panel", HudTheme.Panel(0));

        VBoxContainer outerRoot = new();
        _outerRoot = outerRoot;
        outerRoot.AddThemeConstantOverride("separation", 0);
        panel.AddChild(outerRoot, forceReadableName: false, Node.InternalMode.Disabled);

        BuildTitleBar(outerRoot);
        outerRoot.AddChild(CreateSeparator(), forceReadableName: false, Node.InternalMode.Disabled);
        BuildTabBar(outerRoot);

        MarginContainer contentMargin = CreateMarginContainer(18, 18, 12, 12);
        contentMargin.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        outerRoot.AddChild(contentMargin, forceReadableName: false, Node.InternalMode.Disabled);

        ScrollContainer scroll = new();
        _settingsScroll = scroll;
        scroll.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        HudTheme.StyleScrollBar(scroll.GetVScrollBar());
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        contentMargin.AddChild(scroll, forceReadableName: false, Node.InternalMode.Disabled);

        var scrollPadding = CreateMarginContainer(0, CompactHudGeometry.SettingsScrollGutter, 0, 0);
        scrollPadding.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(scrollPadding);
        _contentRoot = new VBoxContainer();
        _contentRoot.AddThemeConstantOverride("separation", 8);
        _contentRoot.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scrollPadding.AddChild(_contentRoot, forceReadableName: false, Node.InternalMode.Disabled);

        BuildActiveSection();
        BuildFooter(outerRoot);

        panel.Ready += () =>
        {
            if (_tabButtons.TryGetValue(_activeSection, out Button? activeButton) && UiHelpers.IsValid(activeButton))
            {
                activeButton.GrabFocus();
            }
        };

        return panel;
    }

    private static void BuildTitleBar(VBoxContainer parent)
    {
        MarginContainer titleMargin = CreateMarginContainer(18, 18, 12, 12);
        parent.AddChild(titleMargin, forceReadableName: false, Node.InternalMode.Disabled);

        HBoxContainer titleBar = new();
        titleBar.AddThemeConstantOverride("separation", 12);
        titleMargin.AddChild(titleBar, forceReadableName: false, Node.InternalMode.Disabled);

        VBoxContainer titleBlock = new();
        titleBlock.AddThemeConstantOverride("separation", 0);
        titleBlock.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titleBlock.MouseFilter = Control.MouseFilterEnum.Stop;
        titleBar.AddChild(titleBlock, forceReadableName: false, Node.InternalMode.Disabled);
        _dragHandle = titleBlock;

        Label titleLabel = UiHelpers.CreateLabel("BetterSpire Guardian", AccentColor, 20);
        titleLabel.ClipText = true;
        titleLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        titleBlock.AddChild(titleLabel, forceReadableName: false, Node.InternalMode.Disabled);
        titleBlock.AddChild(UiHelpers.CreateLabel(ModText.T("Settings"), MutedTextColor, 12), forceReadableName: false, Node.InternalMode.Disabled);

        Button closeButton = new();
        closeButton.Text = "×";
        closeButton.TooltipText = ModText.T("Close");
        closeButton.CustomMinimumSize = new Vector2(32f, 32f);
        closeButton.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        HudTheme.StyleButton(closeButton, danger: true, fontSize: 16, padding: 4);
        closeButton.Pressed += Hide;
        titleBar.AddChild(closeButton, forceReadableName: false, Node.InternalMode.Disabled);
    }

    private static void BuildTabBar(VBoxContainer parent)
    {
        MarginContainer tabMargin = CreateMarginContainer(18, 18, 10, 10);
        tabMargin.AddThemeStyleboxOverride("panel", UiHelpers.CreatePanelStyle(SurfaceColor, SubtleBorderColor, 0, 0, 0f));
        parent.AddChild(tabMargin, forceReadableName: false, Node.InternalMode.Disabled);

        HBoxContainer tabBar = new();
        tabBar.AddThemeConstantOverride("separation", 8);
        tabMargin.AddChild(tabBar, forceReadableName: false, Node.InternalMode.Disabled);

        foreach (ModSettingSectionDefinition section in ModSettingsCatalog.Sections)
        {
            Button tabButton = new();
            tabButton.Text = ModText.T(section.Title);
            tabButton.ClipText = true;
            tabButton.TooltipText = tabButton.Text;
            tabButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            tabButton.CustomMinimumSize = new Vector2(0f, 34f);
            ModSettingSectionId capturedSection = section.Id;
            tabButton.Pressed += () => SetActiveSection(capturedSection);
            _tabButtons[section.Id] = tabButton;
            tabBar.AddChild(tabButton, forceReadableName: false, Node.InternalMode.Disabled);
        }

        RefreshTabStyles();
    }

    private static void BuildFooter(VBoxContainer parent)
    {
        parent.AddChild(CreateSeparator(), forceReadableName: false, Node.InternalMode.Disabled);
        MarginContainer footerMargin = CreateMarginContainer(18, 18, 9, 11);
        parent.AddChild(footerMargin, forceReadableName: false, Node.InternalMode.Disabled);

        VBoxContainer footer = new();
        footer.AddThemeConstantOverride("separation", 4);
        footerMargin.AddChild(footer, forceReadableName: false, Node.InternalMode.Disabled);

        Label hint = UiHelpers.CreateLabel(ModText.T("Drag the title to move") + "  ·  " + ModText.T("F1 closes this panel"), MutedTextColor, 11);
        hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        footer.AddChild(hint, forceReadableName: false, Node.InternalMode.Disabled);

        _statusLabel = UiHelpers.CreateLabel(string.Empty, SuccessColor, 11, HorizontalAlignment.Right);
        _statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _statusLabel.Visible = false;
        footer.AddChild(_statusLabel, forceReadableName: false, Node.InternalMode.Disabled);
    }

    private static MarginContainer CreateMarginContainer(int left, int right, int top, int bottom)
    {
        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", left);
        margin.AddThemeConstantOverride("margin_right", right);
        margin.AddThemeConstantOverride("margin_top", top);
        margin.AddThemeConstantOverride("margin_bottom", bottom);
        return margin;
    }

    private static HSeparator CreateSeparator()
    {
        HSeparator separator = new();
        separator.AddThemeConstantOverride("separation", 0);
        separator.AddThemeStyleboxOverride("separator", UiHelpers.CreateSeparatorStyle(SubtleBorderColor, 1f, 0f));
        return separator;
    }

    private static void SetActiveSection(ModSettingSectionId section)
    {
        if (_activeSection == section && _contentRoot?.GetChildCount() > 0)
        {
            return;
        }

        _activeSection = section;
        RefreshTabStyles();
        BuildActiveSection();
    }

    private static void RefreshTabStyles()
    {
        foreach ((ModSettingSectionId section, Button button) in _tabButtons)
        {
            ApplyButtonStyle(button, selected: section == _activeSection);
        }
    }

    private static Vector2 GetResponsivePanelSize()
    {
        Vector2 viewportSize = GetViewportSize();
        return new Vector2(CompactHudGeometry.SettingsWidth(viewportSize.X),
            CompactHudGeometry.SettingsHeight(viewportSize.Y, 180, 460));
    }

    private static void CenterPanel()
    {
        if (!UiHelpers.IsValid(_panel) || _layoutController == null)
        {
            return;
        }

        _settingsAnchor = new Vector2(.5f, .5f);
        Vector2 defaultSize = GetResponsivePanelSize();
        _layoutController.ResetLayout(defaultSize, static (safeRect, size) =>
            safeRect.Position + (safeRect.Size - size) * 0.5f);
        _autoFit?.Request();
    }

    private static Vector2 GetViewportSize()
    {
        return _viewport?.GetVisibleRect().Size ?? new Vector2(1920f, 1080f);
    }
}
