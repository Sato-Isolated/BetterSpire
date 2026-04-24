#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace BetterSpire2.UI;

public static partial class SettingsMenu
{
	private static void BuildCombatHudSection(VBoxContainer parent)
	{
		AddSectionHeader(parent, "Combat HUD");
		AddToggle(parent, "Multi-Hit Totals (per enemy)", ModSettings.MultiHitTotals, value =>
		{
			UpdateSetting(() =>
			{
				ModSettings.MultiHitTotals = value;
			}, RefreshIntents);
		});
		AddToggle(parent, "Total Incoming Damage (above player)", ModSettings.PlayerDamageTotal, value =>
		{
			UpdateSetting(() =>
			{
				ModSettings.PlayerDamageTotal = value;
			}, () =>
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
		AddToggle(parent, "Show Expected HP After Damage", ModSettings.ShowExpectedHp, value =>
		{
			UpdateSetting(() =>
			{
				ModSettings.ShowExpectedHp = value;
			}, DamageTracker.Recalculate);
		});
		AddToggle(parent, "Show Turn Summary Tracker", ModSettings.ShowTurnSummary, value =>
		{
			UpdateSetting(() =>
			{
				ModSettings.ShowTurnSummary = value;
			}, TurnSummaryTracker.SyncVisibility);
		});
	}

	private static void BuildMultiplayerSection(VBoxContainer parent)
	{
		AddSectionHeader(parent, "Multiplayer");
		AddToggle(parent, "Show Teammate Hand (F3)", ModSettings.ShowTeammateHand, value =>
		{
			UpdateSetting(() =>
			{
				ModSettings.ShowTeammateHand = value;
			}, () =>
			{
				if (!value)
				{
					DeckTracker.Hide();
				}
			});
		});
		AddToggle(parent, "Keep Open (no click-outside close)", ModSettings.AlwaysShowTeammateHand, value =>
		{
			UpdateSetting(() =>
			{
				ModSettings.AlwaysShowTeammateHand = value;
			});
		});
		AddToggle(parent, "Auto-Show When Entering Combat", ModSettings.AutoShowTeammateHand, value =>
		{
			UpdateSetting(() =>
			{
				ModSettings.AutoShowTeammateHand = value;
			});
		});
		AddToggle(parent, "Hide Own Hand (teammates only)", ModSettings.HideOwnHand, value =>
		{
			UpdateSetting(() =>
			{
				ModSettings.HideOwnHand = value;
			}, DeckTracker.RefreshIfVisible);
		});
		AddToggle(parent, "Compact View (stats only, no cards)", ModSettings.CompactHandViewer, value =>
		{
			UpdateSetting(() =>
			{
				ModSettings.CompactHandViewer = value;
			}, DeckTracker.RefreshIfVisible);
		});
		AddStepperRow(parent, "Card Size", ModSettings.CardScalePercent, 50, 200, 10, value =>
		{
			UpdateSetting(() =>
			{
				ModSettings.CardScalePercent = value;
			}, DeckTracker.RefreshIfVisible);
		});
	}

	private static void BuildGameplaySection(VBoxContainer parent)
	{
		AddSectionHeader(parent, "Gameplay");
		AddToggle(parent, "Instant Fast Mode (combat only)", ModSettings.InstantFastMode, value =>
		{
			UpdateSetting(() =>
			{
				ModSettings.InstantFastMode = value;
			}, () =>
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
		AddToggle(parent, "Skip Splash Screen", ModSettings.SkipSplash, value =>
		{
			UpdateSetting(() =>
			{
				ModSettings.SkipSplash = value;
			});
		});
		AddToggle(parent, "Show Clock", ModSettings.ShowClock, value =>
		{
			UpdateSetting(() =>
			{
				ModSettings.ShowClock = value;
			}, () =>
			{
				ClockDisplay.Toggle(value);
			});
		});
		AddToggle(parent, "  . 24-Hour Format", ModSettings.Clock24Hour, value =>
		{
			UpdateSetting(() =>
			{
				ModSettings.Clock24Hour = value;
			}, ClockDisplay.RefreshIfVisible);
		});
	}

	private static void AddSectionHeader(VBoxContainer parent, string text)
	{
		VBoxContainer sectionHeader = new();
		sectionHeader.AddThemeConstantOverride("separation", 2);
		sectionHeader.AddChild(UiHelpers.CreateLabel(text, new Color(0.7f, 0.55f, 0.2f), 15), forceReadableName: false, Node.InternalMode.Disabled);
		HSeparator separator = new();
		separator.AddThemeConstantOverride("separation", 0);
		separator.AddThemeStyleboxOverride("separator", UiHelpers.CreateSeparatorStyle(new Color(0.5f, 0.4f, 0.15f, 0.6f)));
		sectionHeader.AddChild(separator, forceReadableName: false, Node.InternalMode.Disabled);
		parent.AddChild(sectionHeader, forceReadableName: false, Node.InternalMode.Disabled);
	}

	private static void AddToggle(VBoxContainer parent, string label, bool initialValue, Action<bool> onToggle)
	{
		CheckButton checkButton = new();
		checkButton.Text = label;
		checkButton.ButtonPressed = initialValue;
		checkButton.AddThemeColorOverride("font_color", SecondaryTextColor);
		checkButton.AddThemeFontSizeOverride("font_size", 16);
		checkButton.Toggled += pressed =>
		{
			onToggle(pressed);
		};
		parent.AddChild(checkButton, forceReadableName: false, Node.InternalMode.Disabled);
	}

	private static void AddStepperRow(VBoxContainer parent, string label, int initialValue, int min, int max, int step, Action<int> onChange)
	{
		HBoxContainer row = new();
		row.AddThemeConstantOverride("separation", 8);
		row.AddChild(UiHelpers.CreateLabel(label, SecondaryTextColor, 16), forceReadableName: false, Node.InternalMode.Disabled);

		int current = initialValue;
		Button decreaseButton = new();
		decreaseButton.Text = "-";
		decreaseButton.CustomMinimumSize = new Vector2(32f, 28f);
		row.AddChild(decreaseButton, forceReadableName: false, Node.InternalMode.Disabled);

		Label valueLabel = new();
		valueLabel.Text = $"{initialValue}%";
		valueLabel.HorizontalAlignment = HorizontalAlignment.Center;
		valueLabel.CustomMinimumSize = new Vector2(50f, 0f);
		valueLabel.AddThemeColorOverride("font_color", AccentColor);
		valueLabel.AddThemeFontSizeOverride("font_size", 16);
		row.AddChild(valueLabel, forceReadableName: false, Node.InternalMode.Disabled);

		Button increaseButton = new();
		increaseButton.Text = "+";
		increaseButton.CustomMinimumSize = new Vector2(32f, 28f);
		row.AddChild(increaseButton, forceReadableName: false, Node.InternalMode.Disabled);

		decreaseButton.Pressed += () =>
		{
			if (current > min)
			{
				current -= step;
				valueLabel.Text = $"{current}%";
				onChange(current);
			}
		};
		increaseButton.Pressed += () =>
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

			ulong netId = netService.NetId;
			AddSectionHeader(vbox, "Party");
			foreach (Player item in players)
			{
				bool isLocalPlayer = item.NetId == netId;
				string playerName = PlatformUtil.GetPlayerName(PlatformType.Steam, item.NetId);
				string? characterName = item.Character?.Title?.GetFormattedText();
				string displayName = string.IsNullOrEmpty(playerName)
					? characterName ?? $"Player {item.NetId}"
					: !string.IsNullOrEmpty(characterName)
						? playerName + " (" + characterName + ")"
						: playerName;

				HBoxContainer partyRow = new();
				partyRow.AddThemeConstantOverride("separation", 8);
				Label playerLabel = UiHelpers.CreateLabel(isLocalPlayer ? displayName + " (You)" : displayName, isLocalPlayer ? new Color(0.5f, 0.8f, 0.5f) : SecondaryTextColor, 16);
				playerLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				partyRow.AddChild(playerLabel, forceReadableName: false, Node.InternalMode.Disabled);

				if (!isLocalPlayer)
				{
					Button button = new();
					bool isMuted = PartyManager.IsDrawingMuted(item.NetId);
					button.Text = isMuted ? "Show Drawings" : "Hide Drawings";
					button.AddThemeFontSizeOverride("font_size", 14);
					button.CustomMinimumSize = new Vector2(130f, 0f);
					ulong capturedNetId = item.NetId;
					Button capturedBtn = button;
					button.Pressed += () =>
					{
						PartyManager.ToggleDrawingMute(capturedNetId);
						bool muted = PartyManager.IsDrawingMuted(capturedNetId);
						capturedBtn.Text = muted ? "Show Drawings" : "Hide Drawings";
						if (muted)
						{
							PartyManager.ClearDrawingsForPlayer(capturedNetId);
						}
					};
					partyRow.AddChild(button, forceReadableName: false, Node.InternalMode.Disabled);
				}

				vbox.AddChild(partyRow, forceReadableName: false, Node.InternalMode.Disabled);
			}

			Button clearAllButton = new();
			clearAllButton.Text = "Clear All Drawings";
			clearAllButton.AddThemeFontSizeOverride("font_size", 14);
			clearAllButton.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.2f));
			clearAllButton.Pressed += () =>
			{
				PartyManager.ClearAllDrawings();
			};
			vbox.AddChild(clearAllButton, forceReadableName: false, Node.InternalMode.Disabled);
		}
		catch (Exception ex)
		{
			ModLog.Error("SettingsMenu.BuildPartySection", ex);
		}
	}
}