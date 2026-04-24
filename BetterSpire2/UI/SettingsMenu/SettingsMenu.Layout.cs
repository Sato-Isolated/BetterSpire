#nullable enable
using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace BetterSpire2.UI;

public static partial class SettingsMenu
{
	private static void Show()
	{
		if (_isVisible)
		{
			Hide();
		}

		NGame? instance = NGame.Instance;
		if (instance == null)
		{
			return;
		}

		_canvasLayer = UiHelpers.CreateCanvasLayer(10);
		_panel = BuildPanel();
		_canvasLayer.AddChild(_panel, forceReadableName: false, Node.InternalMode.Disabled);
		instance.AddChild(_canvasLayer, forceReadableName: false, Node.InternalMode.Disabled);
		ApplyInitialPosition(instance);
		_isVisible = true;
	}

	private static PanelContainer BuildPanel()
	{
		PanelContainer panel = new();
		panel.ZIndex = 200;
		panel.AddThemeStyleboxOverride("panel", UiHelpers.CreatePanelStyle(PanelBackgroundColor, PanelBorderColor, 2, 8, 16f));

		VBoxContainer outerRoot = new();
		outerRoot.AddThemeConstantOverride("separation", 10);
		panel.AddChild(outerRoot, forceReadableName: false, Node.InternalMode.Disabled);

		BuildTitleBar(outerRoot);

		NGame? game = NGame.Instance;
		float maxHeight = (game?.GetViewport()?.GetVisibleRect().Size.Y ?? 1080f) - 160f;

		ScrollContainer scroll = new();
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scroll.CustomMinimumSize = new Vector2(0f, 0f);
		scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		scroll.AddThemeConstantOverride("margin_right", 6);
		outerRoot.AddChild(scroll, forceReadableName: false, Node.InternalMode.Disabled);

		VBoxContainer root = new();
		root.AddThemeConstantOverride("separation", 10);
		root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scroll.AddChild(root, forceReadableName: false, Node.InternalMode.Disabled);

		BuildSettingsColumns(root);
		BuildPartySection(root);

		outerRoot.AddChild(UiHelpers.CreateLabel("Drag to move | Click outside to close", MutedTextColor, 14, HorizontalAlignment.Center), forceReadableName: false, Node.InternalMode.Disabled);

		panel.Ready += () =>
		{
			if (UiHelpers.IsValid(scroll) && UiHelpers.IsValid(panel))
			{
				float contentHeight = root.GetCombinedMinimumSize().Y;
				scroll.CustomMinimumSize = new Vector2(0f, Math.Min(contentHeight, maxHeight));
			}
		};

		return panel;
	}

	private static void BuildTitleBar(VBoxContainer parent)
	{
		HBoxContainer titleBar = new();
		titleBar.AddThemeConstantOverride("separation", 8);
		parent.AddChild(titleBar, forceReadableName: false, Node.InternalMode.Disabled);

		Label titleLabel = UiHelpers.CreateLabel("BetterSpire2 Lite Settings", AccentColor, 20, HorizontalAlignment.Center);
		titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		titleBar.AddChild(titleLabel, forceReadableName: false, Node.InternalMode.Disabled);

		Button closeButton = new();
		closeButton.Text = "X";
		closeButton.CustomMinimumSize = new Vector2(28f, 28f);
		closeButton.AddThemeStyleboxOverride("normal", UiHelpers.CreatePanelStyle(new Color(0.3f, 0.1f, 0.1f), new Color(0.6f, 0.2f, 0.2f), 1, 4, 2f));
		closeButton.AddThemeStyleboxOverride("hover", UiHelpers.CreatePanelStyle(new Color(0.5f, 0.15f, 0.15f), new Color(0.8f, 0.3f, 0.3f), 1, 4, 2f));
		closeButton.AddThemeStyleboxOverride("pressed", UiHelpers.CreatePanelStyle(new Color(0.5f, 0.15f, 0.15f), new Color(0.8f, 0.3f, 0.3f), 1, 4, 2f));
		closeButton.AddThemeColorOverride("font_color", new Color(0.9f, 0.4f, 0.4f));
		closeButton.AddThemeFontSizeOverride("font_size", 14);
		closeButton.Pressed += Hide;
		titleBar.AddChild(closeButton, forceReadableName: false, Node.InternalMode.Disabled);
	}

	private static void BuildSettingsColumns(VBoxContainer parent)
	{
		HBoxContainer columns = new();
		columns.AddThemeConstantOverride("separation", 24);
		parent.AddChild(columns, forceReadableName: false, Node.InternalMode.Disabled);

		VBoxContainer leftColumn = CreateColumn();
		VBoxContainer rightColumn = CreateColumn();
		columns.AddChild(leftColumn, forceReadableName: false, Node.InternalMode.Disabled);
		columns.AddChild(rightColumn, forceReadableName: false, Node.InternalMode.Disabled);

		BuildCombatHudSection(leftColumn);
		BuildMultiplayerSection(leftColumn);
		BuildGameplaySection(rightColumn);
	}

	private static VBoxContainer CreateColumn()
	{
		VBoxContainer column = new();
		column.AddThemeConstantOverride("separation", 10);
		column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		return column;
	}

	private static void HandleMouseButton(InputEventMouseButton inputEvent)
	{
		Vector2 mousePosition = inputEvent.Position;
		Rect2 panelRect = _panel!.GetGlobalRect();

		if (inputEvent.Pressed)
		{
			if (panelRect.HasPoint(mousePosition))
			{
				_isDragging = true;
				_dragOffset = mousePosition - _panel.Position;
				return;
			}

			if (!DeckTracker.IsPointInPanel(mousePosition))
			{
				_isDragging = false;
				Hide();
			}
			return;
		}

		if (_isDragging)
		{
			_isDragging = false;
			PersistPosition();
		}
	}

	private static void ApplyInitialPosition(NGame game)
	{
		Vector2? savedPosition = ModSettings.SettingsMenuPosition;
		if (savedPosition.HasValue)
		{
			_panel!.Position = savedPosition.Value;
			return;
		}

		Vector2 viewportSize = game.GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920f, 1080f);
		_panel!.CallDeferred("set_position", new Vector2(viewportSize.X / 2f - 200f, viewportSize.Y / 2f - 200f));
	}

	private static void PersistPosition()
	{
		if (!UiHelpers.IsValid(_panel))
		{
			return;
		}

		ModSettings.SettingsMenuPosition = _panel!.Position;
		ModSettings.Save();
	}
}