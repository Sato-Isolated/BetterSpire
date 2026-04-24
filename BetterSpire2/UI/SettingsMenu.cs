#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace BetterSpire2.UI;

public static class SettingsMenu
{
	private static readonly Color PanelBackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);

	private static readonly Color PanelBorderColor = new Color(0.8f, 0.6f, 0.2f);

	private static readonly Color AccentColor = new Color(0.9f, 0.7f, 0.2f);

	private static readonly Color SecondaryTextColor = new Color(0.9f, 0.9f, 0.9f);

	private static readonly Color MutedTextColor = new Color(0.5f, 0.5f, 0.5f);

	private static PanelContainer? _panel;

	private static CanvasLayer? _canvasLayer;

	private static bool _isVisible;

	private static bool _isDragging;

	private static Vector2 _dragOffset;

	public static void Toggle()
	{
		if (_isVisible)
		{
			Hide();
		}
		else
		{
			Show();
		}
	}

	public static void HandleMouseInput(InputEvent @event)
	{
		if (!_isVisible || !UiHelpers.IsValid(_panel))
		{
			return;
		}

		if (@event is InputEventMouseButton inputEventMouseButton && inputEventMouseButton.ButtonIndex == MouseButton.Left)
		{
			HandleMouseButton(inputEventMouseButton);
		}
		else if (@event is InputEventMouseMotion inputEventMouseMotion && _isDragging)
		{
			_panel!.Position = inputEventMouseMotion.Position - _dragOffset;
		}
	}

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
		PanelContainer panel = new PanelContainer();
		panel.ZIndex = 200;
		panel.AddThemeStyleboxOverride("panel", UiHelpers.CreatePanelStyle(PanelBackgroundColor, PanelBorderColor, 2, 8, 16f));

		VBoxContainer outerRoot = new VBoxContainer();
		outerRoot.AddThemeConstantOverride("separation", 10);
		panel.AddChild(outerRoot, forceReadableName: false, Node.InternalMode.Disabled);

		BuildTitleBar(outerRoot);

		NGame? game = NGame.Instance;
		float maxHeight = (game?.GetViewport()?.GetVisibleRect().Size.Y ?? 1080f) - 160f;

		ScrollContainer scroll = new ScrollContainer();
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scroll.CustomMinimumSize = new Vector2(0f, 0f);
		scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		scroll.AddThemeConstantOverride("margin_right", 6);
		outerRoot.AddChild(scroll, forceReadableName: false, Node.InternalMode.Disabled);

		VBoxContainer root = new VBoxContainer();
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
		HBoxContainer titleBar = new HBoxContainer();
		titleBar.AddThemeConstantOverride("separation", 8);
		parent.AddChild(titleBar, forceReadableName: false, Node.InternalMode.Disabled);

		Label titleLabel = UiHelpers.CreateLabel("BetterSpire2 Lite Settings", AccentColor, 20, HorizontalAlignment.Center);
		titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		titleBar.AddChild(titleLabel, forceReadableName: false, Node.InternalMode.Disabled);

		Button closeButton = new Button();
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
		HBoxContainer columns = new HBoxContainer();
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
		VBoxContainer column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 10);
		column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		return column;
	}

	private static void BuildCombatHudSection(VBoxContainer parent)
	{
		AddSectionHeader(parent, "Combat HUD");
		AddToggle(parent, "Multi-Hit Totals (per enemy)", ModSettings.MultiHitTotals, delegate(bool value)
		{
			UpdateSetting(delegate
			{
				ModSettings.MultiHitTotals = value;
			}, RefreshIntents);
		});
		AddToggle(parent, "Total Incoming Damage (above player)", ModSettings.PlayerDamageTotal, delegate(bool value)
		{
			UpdateSetting(delegate
			{
				ModSettings.PlayerDamageTotal = value;
			}, delegate
			{
				if (value)
				{
					DamageTracker.Recalculate();
				}
				else
				{
					DamageTracker.Hide();
				}
			});
		});
		AddToggle(parent, "Show Expected HP After Damage", ModSettings.ShowExpectedHp, delegate(bool value)
		{
			UpdateSetting(delegate
			{
				ModSettings.ShowExpectedHp = value;
			}, DamageTracker.Recalculate);
		});
		AddToggle(parent, "Show Turn Summary Tracker", ModSettings.ShowTurnSummary, delegate(bool value)
		{
			UpdateSetting(delegate
			{
				ModSettings.ShowTurnSummary = value;
			}, TurnSummaryTracker.SyncVisibility);
		});
	}

	private static void BuildMultiplayerSection(VBoxContainer parent)
	{
		AddSectionHeader(parent, "Multiplayer");
		AddToggle(parent, "Show Teammate Hand (F3)", ModSettings.ShowTeammateHand, delegate(bool value)
		{
			UpdateSetting(delegate
			{
				ModSettings.ShowTeammateHand = value;
			}, delegate
			{
				if (!value)
				{
					DeckTracker.Hide();
				}
			});
		});
		AddToggle(parent, "Keep Open (no click-outside close)", ModSettings.AlwaysShowTeammateHand, delegate(bool value)
		{
			UpdateSetting(delegate
			{
				ModSettings.AlwaysShowTeammateHand = value;
			});
		});
		AddToggle(parent, "Auto-Show When Entering Combat", ModSettings.AutoShowTeammateHand, delegate(bool value)
		{
			UpdateSetting(delegate
			{
				ModSettings.AutoShowTeammateHand = value;
			});
		});
		AddToggle(parent, "Hide Own Hand (teammates only)", ModSettings.HideOwnHand, delegate(bool value)
		{
			UpdateSetting(delegate
			{
				ModSettings.HideOwnHand = value;
			}, DeckTracker.RefreshIfVisible);
		});
		AddToggle(parent, "Compact View (stats only, no cards)", ModSettings.CompactHandViewer, delegate(bool value)
		{
			UpdateSetting(delegate
			{
				ModSettings.CompactHandViewer = value;
			}, DeckTracker.RefreshIfVisible);
		});
		AddStepperRow(parent, "Card Size", ModSettings.CardScalePercent, 50, 200, 10, delegate(int value)
		{
			UpdateSetting(delegate
			{
				ModSettings.CardScalePercent = value;
			}, DeckTracker.RefreshIfVisible);
		});
	}

	private static void BuildGameplaySection(VBoxContainer parent)
	{
		AddSectionHeader(parent, "Gameplay");
		AddToggle(parent, "Instant Fast Mode (combat only)", ModSettings.InstantFastMode, delegate(bool value)
		{
			UpdateSetting(delegate
			{
				ModSettings.InstantFastMode = value;
			}, delegate
			{
				if (!value)
				{
					InstantSpeedHelper.OnCombatEnd();
				}
				else if (CombatManager.Instance?.DebugOnlyGetState() != null)
				{
					InstantSpeedHelper.OnCombatStart();
				}

				ModLog.Info($"Instant Fast Mode (combat-only): {value}");
			});
		});
		AddToggle(parent, "Skip Splash Screen", ModSettings.SkipSplash, delegate(bool value)
		{
			UpdateSetting(delegate
			{
				ModSettings.SkipSplash = value;
			});
		});
		AddToggle(parent, "Show Clock", ModSettings.ShowClock, delegate(bool value)
		{
			UpdateSetting(delegate
			{
				ModSettings.ShowClock = value;
			}, delegate
			{
				ClockDisplay.Toggle(value);
			});
		});
		AddToggle(parent, "  . 24-Hour Format", ModSettings.Clock24Hour, delegate(bool value)
		{
			UpdateSetting(delegate
			{
				ModSettings.Clock24Hour = value;
			}, ClockDisplay.RefreshIfVisible);
		});
	}

	private static void UpdateSetting(Action applyChange, Action? afterSave = null)
	{
		applyChange();
		ModSettings.Save();
		afterSave?.Invoke();
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

	private static void AddSectionHeader(VBoxContainer parent, string text)
	{
		VBoxContainer sectionHeader = new VBoxContainer();
		sectionHeader.AddThemeConstantOverride("separation", 2);
		sectionHeader.AddChild(UiHelpers.CreateLabel(text, new Color(0.7f, 0.55f, 0.2f), 15), forceReadableName: false, Node.InternalMode.Disabled);
		HSeparator separator = new HSeparator();
		separator.AddThemeConstantOverride("separation", 0);
		separator.AddThemeStyleboxOverride("separator", UiHelpers.CreateSeparatorStyle(new Color(0.5f, 0.4f, 0.15f, 0.6f)));
		sectionHeader.AddChild(separator, forceReadableName: false, Node.InternalMode.Disabled);
		parent.AddChild(sectionHeader, forceReadableName: false, Node.InternalMode.Disabled);
	}

	private static void AddToggle(VBoxContainer parent, string label, bool initialValue, Action<bool> onToggle)
	{
		CheckButton checkButton = new CheckButton();
		checkButton.Text = label;
		checkButton.ButtonPressed = initialValue;
		checkButton.AddThemeColorOverride("font_color", SecondaryTextColor);
		checkButton.AddThemeFontSizeOverride("font_size", 16);
		checkButton.Toggled += delegate(bool pressed)
		{
			onToggle(pressed);
		};
		parent.AddChild(checkButton, forceReadableName: false, Node.InternalMode.Disabled);
	}

	private static void AddStepperRow(VBoxContainer parent, string label, int initialValue, int min, int max, int step, Action<int> onChange)
	{
		HBoxContainer row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		row.AddChild(UiHelpers.CreateLabel(label, SecondaryTextColor, 16), forceReadableName: false, Node.InternalMode.Disabled);

		int current = initialValue;
		Button decreaseButton = new Button();
		decreaseButton.Text = "-";
		decreaseButton.CustomMinimumSize = new Vector2(32f, 28f);
		row.AddChild(decreaseButton, forceReadableName: false, Node.InternalMode.Disabled);

		Label valueLabel = new Label();
		valueLabel.Text = $"{initialValue}%";
		valueLabel.HorizontalAlignment = HorizontalAlignment.Center;
		valueLabel.CustomMinimumSize = new Vector2(50f, 0f);
		valueLabel.AddThemeColorOverride("font_color", AccentColor);
		valueLabel.AddThemeFontSizeOverride("font_size", 16);
		row.AddChild(valueLabel, forceReadableName: false, Node.InternalMode.Disabled);

		Button increaseButton = new Button();
		increaseButton.Text = "+";
		increaseButton.CustomMinimumSize = new Vector2(32f, 28f);
		row.AddChild(increaseButton, forceReadableName: false, Node.InternalMode.Disabled);

		decreaseButton.Pressed += delegate
		{
			if (current > min)
			{
				current -= step;
				valueLabel.Text = $"{current}%";
				onChange(current);
			}
		};
		increaseButton.Pressed += delegate
		{
			if (current < max)
			{
				current += step;
				valueLabel.Text = $"{current}%";
				onChange(current);
			}
		};

		parent.AddChild(row, forceReadableName: false, Node.InternalMode.Disabled);
	}

	public static bool IsPointInPanel(Vector2 point)
	{
		return _isVisible && UiHelpers.IsValid(_panel) && _panel!.GetGlobalRect().HasPoint(point);
	}

	public static void Hide()
	{
		PersistPosition();

		if (UiHelpers.IsValid(_canvasLayer))
		{
			_canvasLayer!.QueueFree();
		}

		_canvasLayer = null;
		_panel = null;
		_isVisible = false;
		_isDragging = false;
	}

	private static void RefreshIntents()
	{
		try
		{
			NCombatRoom? instance = NCombatRoom.Instance;
			if (instance == null)
			{
				return;
			}
			foreach (Node child in instance.GetChildren())
			{
				if (child is NCreature nCreature)
				{
					nCreature.RefreshIntents();
				}
			}
		}
		catch (Exception ex)
		{
			ModLog.Error("SettingsMenu.RefreshIntents", ex);
		}
	}

	private static void BuildPartySection(VBoxContainer vbox)
	{
		try
		{
			RunManager instance = RunManager.Instance;
			if (instance == null || instance.IsSinglePlayerOrFakeMultiplayer)
			{
				return;
			}
			INetGameService netService = instance.NetService;
			if (netService == null)
			{
				return;
			}
			RunState? value = Traverse.Create(instance).Property<RunState>("State").Value;
			if (value == null)
			{
				return;
			}
			IReadOnlyList<Player> players = value.Players;
			if (players == null || players.Count <= 1)
			{
				return;
			}
			bool flag = netService.Type == NetGameType.Host;
			ulong netId = netService.NetId;
			_ = flag;
			AddSectionHeader(vbox, "Party");
			foreach (Player item in players)
			{
				bool flag2 = item.NetId == netId;
				string playerName = PlatformUtil.GetPlayerName(PlatformType.Steam, item.NetId);
				string? text = item.Character?.Title?.GetFormattedText();
				string text2 = (string.IsNullOrEmpty(playerName) ? (text ?? $"Player {item.NetId}") : ((!string.IsNullOrEmpty(text)) ? (playerName + " (" + text + ")") : playerName));
				HBoxContainer partyRow = new HBoxContainer();
				partyRow.AddThemeConstantOverride("separation", 8);
				Label playerLabel = UiHelpers.CreateLabel(flag2 ? (text2 + " (You)") : text2, flag2 ? new Color(0.5f, 0.8f, 0.5f) : SecondaryTextColor, 16);
				playerLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				partyRow.AddChild(playerLabel, forceReadableName: false, Node.InternalMode.Disabled);
				if (!flag2)
				{
					Button button = new Button();
					bool flag3 = PartyManager.IsDrawingMuted(item.NetId);
					button.Text = (flag3 ? "Show Drawings" : "Hide Drawings");
					button.AddThemeFontSizeOverride("font_size", 14);
					button.CustomMinimumSize = new Vector2(130f, 0f);
					ulong capturedNetId = item.NetId;
					Button capturedBtn = button;
					button.Pressed += delegate
					{
						PartyManager.ToggleDrawingMute(capturedNetId);
						bool flag4 = PartyManager.IsDrawingMuted(capturedNetId);
						capturedBtn.Text = (flag4 ? "Show Drawings" : "Hide Drawings");
						if (flag4)
						{
							PartyManager.ClearDrawingsForPlayer(capturedNetId);
						}
					};
					partyRow.AddChild(button, forceReadableName: false, Node.InternalMode.Disabled);
				}
				vbox.AddChild(partyRow, forceReadableName: false, Node.InternalMode.Disabled);
			}
			Button button2 = new Button();
			button2.Text = "Clear All Drawings";
			button2.AddThemeFontSizeOverride("font_size", 14);
			button2.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.2f));
			button2.Pressed += delegate
			{
				PartyManager.ClearAllDrawings();
			};
			vbox.AddChild(button2, forceReadableName: false, Node.InternalMode.Disabled);
		}
		catch (Exception ex)
		{
			ModLog.Error("SettingsMenu.BuildPartySection", ex);
		}
	}
}
