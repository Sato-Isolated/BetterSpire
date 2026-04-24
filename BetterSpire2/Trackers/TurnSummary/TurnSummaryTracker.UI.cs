#nullable enable
using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace BetterSpire2.Trackers;

public static partial class TurnSummaryTracker
{
	private static void BuildUi()
	{
		CleanupUi();

		NGame? instance = NGame.Instance;
		if (instance == null) return;

		_canvasLayer = UiHelpers.CreateCanvasLayer(12);

		_panel = new PanelContainer();
		_panel.ZIndex = 220;
		_panel.ClipContents = true;
		_panel.CustomMinimumSize = new Vector2(MinPanelWidth, MinPanelHeight);
		_panel.AddThemeStyleboxOverride("panel", UiHelpers.CreatePanelStyle(_panelBg, _panelBorder, 1, 6, 8f));

		Vector2 size = ClampPanelSize(ModSettings.TurnSummarySize ?? new Vector2(DefaultPanelWidth, DefaultPanelHeight));
		_panel.Size = size;

		VBoxContainer root = new();
		root.AddThemeConstantOverride("separation", 4);
		root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_panel.AddChild(root, forceReadableName: false, Node.InternalMode.Disabled);

		HBoxContainer titleBar = new();
		titleBar.AddThemeConstantOverride("separation", 3);
		root.AddChild(titleBar, forceReadableName: false, Node.InternalMode.Disabled);

		_titleLabel = UiHelpers.CreateLabel("R1", _titleColor, 14);
		_titleLabel.CustomMinimumSize = new Vector2(60f, 0f);
		titleBar.AddChild(_titleLabel, forceReadableName: false, Node.InternalMode.Disabled);

		_statsTabButton = CreateTabButton("Stats", _activeTab == Tab.Stats);
		_statsTabButton.Pressed += SwitchToStats;
		titleBar.AddChild(_statsTabButton, forceReadableName: false, Node.InternalMode.Disabled);

		_logTabButton = CreateTabButton("Log", _activeTab == Tab.Log);
		_logTabButton.Pressed += SwitchToLog;
		titleBar.AddChild(_logTabButton, forceReadableName: false, Node.InternalMode.Disabled);

		Control spacer = new();
		spacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		titleBar.AddChild(spacer, forceReadableName: false, Node.InternalMode.Disabled);

		_prevRoundButton = CreateNavButton("\u25C0");
		_prevRoundButton.Pressed += PrevRound;
		titleBar.AddChild(_prevRoundButton, forceReadableName: false, Node.InternalMode.Disabled);

		_scopeButton = CreateNavButton("All");
		_scopeButton.CustomMinimumSize = new Vector2(32f, 22f);
		_scopeButton.Pressed += ToggleViewMode;
		titleBar.AddChild(_scopeButton, forceReadableName: false, Node.InternalMode.Disabled);

		_nextRoundButton = CreateNavButton("\u25B6");
		_nextRoundButton.Pressed += NextRound;
		titleBar.AddChild(_nextRoundButton, forceReadableName: false, Node.InternalMode.Disabled);

		_collapseButton = CreateNavButton(ModSettings.TurnSummaryCollapsed ? "+" : "\u2212");
		_collapseButton.Pressed += ToggleCollapsed;
		titleBar.AddChild(_collapseButton, forceReadableName: false, Node.InternalMode.Disabled);

		_scrollContainer = new ScrollContainer();
		_scrollContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_scrollContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		root.AddChild(_scrollContainer, forceReadableName: false, Node.InternalMode.Disabled);

		_contentContainer = new VBoxContainer();
		_contentContainer.AddThemeConstantOverride("separation", 4);
		_contentContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_scrollContainer.AddChild(_contentContainer, forceReadableName: false, Node.InternalMode.Disabled);

		_gripLabel = new Label();
		_gripLabel.Text = "\u25E2";
		_gripLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.18f));
		_gripLabel.AddThemeFontSizeOverride("font_size", 11);
		_gripLabel.MouseFilter = Control.MouseFilterEnum.Ignore;

		_canvasLayer.AddChild(_panel, forceReadableName: false, Node.InternalMode.Disabled);
		_canvasLayer.AddChild(_gripLabel, forceReadableName: false, Node.InternalMode.Disabled);
		instance.AddChild(_canvasLayer, forceReadableName: false, Node.InternalMode.Disabled);

		_panel.Position = ModSettings.TurnSummaryPosition ?? GetDefaultPosition(instance);
		UpdateGripPosition();
		_visible = true;
	}

	private static Button CreateTabButton(string text, bool active)
	{
		Button button = new();
		button.Text = text;
		button.CustomMinimumSize = new Vector2(40f, 20f);
		button.AddThemeFontSizeOverride("font_size", 11);
		ApplyTabStyle(button, active);
		return button;
	}

	private static void ApplyTabStyle(Button button, bool active)
	{
		Color bg = active ? _tabActiveBg : _tabInactiveBg;
		Color border = active ? _tabActiveBorder : _tabInactiveBorder;
		Color text = active ? _titleColor : _mutedColor;
		button.AddThemeColorOverride("font_color", text);
		button.AddThemeStyleboxOverride("normal", UiHelpers.CreatePanelStyle(bg, border, 1, 3, 3f));
		button.AddThemeStyleboxOverride("hover", UiHelpers.CreatePanelStyle(bg, _titleColor, 1, 3, 3f));
		button.AddThemeStyleboxOverride("pressed", UiHelpers.CreatePanelStyle(bg, _titleColor, 1, 3, 3f));
	}

	private static Button CreateNavButton(string text)
	{
		Button button = new();
		button.Text = text;
		button.CustomMinimumSize = new Vector2(22f, 22f);
		button.AddThemeColorOverride("font_color", _mutedColor);
		button.AddThemeColorOverride("font_disabled_color", new Color(_mutedColor, 0.35f));
		button.AddThemeFontSizeOverride("font_size", 11);
		Color navBg = new(0.08f, 0.09f, 0.12f, 0.8f);
		Color navBorder = new(0.22f, 0.24f, 0.30f);
		button.AddThemeStyleboxOverride("normal", UiHelpers.CreatePanelStyle(navBg, navBorder, 1, 3, 2f));
		button.AddThemeStyleboxOverride("hover", UiHelpers.CreatePanelStyle(navBg, _mutedColor, 1, 3, 2f));
		button.AddThemeStyleboxOverride("pressed", UiHelpers.CreatePanelStyle(navBg, _mutedColor, 1, 3, 2f));
		return button;
	}

	private static Vector2 ClampPanelSize(Vector2 size)
	{
		size.X = Math.Max(MinPanelWidth, size.X);
		size.Y = Math.Max(MinPanelHeight, size.Y);
		return size;
	}

	private static Vector2 GetDefaultPosition(NGame game)
	{
		Vector2 viewportSize = game.GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920f, 1080f);
		return new Vector2(Math.Max(20f, viewportSize.X - 420f), 120f);
	}
}