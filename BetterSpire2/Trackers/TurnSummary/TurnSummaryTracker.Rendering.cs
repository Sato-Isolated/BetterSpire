#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;

namespace BetterSpire2.Trackers;

public static partial class TurnSummaryTracker
{
	private static void RenderContent(List<PlayerTurnSummary> summaries)
	{
		if (!UiHelpers.IsValid(_contentContainer) || !UiHelpers.IsValid(_titleLabel)
			|| !UiHelpers.IsValid(_collapseButton) || !UiHelpers.IsValid(_scrollContainer)
			|| !UiHelpers.IsValid(_prevRoundButton) || !UiHelpers.IsValid(_scopeButton)
			|| !UiHelpers.IsValid(_nextRoundButton))
			return;

		_interactiveControls.Clear();
		RegisterInteractiveControl(_prevRoundButton!);
		RegisterInteractiveControl(_scopeButton!);
		RegisterInteractiveControl(_nextRoundButton!);
		RegisterInteractiveControl(_collapseButton!);

		ClearContainer(_contentContainer!);
		_titleLabel!.Text = GetWindowTitle();
		_scopeButton!.Text = _viewMode == HistoryViewMode.Combat ? $"R{GetSelectedRoundNumber()}" : "All";
		_collapseButton!.Text = GetModeToggleText();
		_prevRoundButton!.Disabled = _viewMode == HistoryViewMode.Combat || GetSelectedRoundNumber() <= 1;
		_nextRoundButton!.Disabled = _viewMode == HistoryViewMode.Combat || GetSelectedRoundNumber() >= Math.Max(1, _trackedRound);
		_scrollContainer!.CustomMinimumSize = new Vector2(0f, Math.Max(
			ModSettings.TurnSummaryCollapsed ? 44f : 104f,
			(_panel?.Size.Y ?? DefaultPanelHeight) - 44f));

		if (summaries.Count == 0)
		{
			_contentContainer!.AddChild(UiHelpers.CreateLabel("Waiting for data\u2026", _mutedColor, 11), forceReadableName: false, Node.InternalMode.Disabled);
			return;
		}

		foreach (PlayerTurnSummary summary in summaries)
		{
			_contentContainer!.AddChild(CreatePlayerPanel(summary), forceReadableName: false, Node.InternalMode.Disabled);
		}
	}

	private static string GetWindowTitle()
	{
		if (_viewMode == HistoryViewMode.Combat)
		{
			return _trackedRound > 0 ? $"Combat  R1\u2013R{_trackedRound}" : "Combat";
		}

		return $"R{GetSelectedRoundNumber()}";
	}

	private static PanelContainer CreatePlayerPanel(PlayerTurnSummary summary)
	{
		Color nameColor = summary.IsLocal ? _localPlayerColor : _allyColor;
		PanelContainer panel = new();
		panel.AddThemeStyleboxOverride("panel", UiHelpers.CreatePanelStyle(_sectionBg, _sectionBorder, 1, 4, 6f));

		VBoxContainer body = new();
		body.AddThemeConstantOverride("separation", 3);
		panel.AddChild(body, forceReadableName: false, Node.InternalMode.Disabled);

		HBoxContainer nameRow = new();
		nameRow.AddThemeConstantOverride("separation", 6);
		Label nameLabel = UiHelpers.CreateLabel(summary.DisplayName, nameColor, 13);
		nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		nameRow.AddChild(nameLabel, forceReadableName: false, Node.InternalMode.Disabled);
		if (summary.IsLocal)
		{
			nameRow.AddChild(UiHelpers.CreateLabel("YOU", _localPlayerColor, 9), forceReadableName: false, Node.InternalMode.Disabled);
		}
		body.AddChild(nameRow, forceReadableName: false, Node.InternalMode.Disabled);
		RenderStatsContent(body, summary);

		return panel;
	}

	private static void RenderStatsContent(VBoxContainer body, PlayerTurnSummary summary)
	{
		if (ModSettings.TurnSummaryCollapsed)
		{
			body.AddChild(CreateCompactSummaryRow(summary), forceReadableName: false, Node.InternalMode.Disabled);
			return;
		}

		body.AddChild(CreateDetailRow("Cards", BuildCardsMetrics(summary)), forceReadableName: false, Node.InternalMode.Disabled);
		body.AddChild(CreateDetailRow("Energy", BuildEnergyMetrics(summary)), forceReadableName: false, Node.InternalMode.Disabled);
		body.AddChild(CreateDetailRow("Damage Out", BuildDamageMetrics(summary.DamageDealtTotal, summary.DamageDealtHp, summary.DamageDealtBlocked, _dealColor)), forceReadableName: false, Node.InternalMode.Disabled);
		body.AddChild(CreateDetailRow("Damage In", BuildDamageMetrics(summary.DamageReceivedTotal, summary.DamageReceivedHp, summary.DamageReceivedBlocked, _takeColor)), forceReadableName: false, Node.InternalMode.Disabled);
		body.AddChild(CreateDetailRow("Block", BuildBlockMetrics(summary)), forceReadableName: false, Node.InternalMode.Disabled);

		string? blockSourcesText = BuildBlockSourcesText(summary);
		if (blockSourcesText != null)
		{
			body.AddChild(CreateDetailTextRow("Sources", blockSourcesText, _mutedColor), forceReadableName: false, Node.InternalMode.Disabled);
		}

		if (summary.Cards.Count > 0)
		{
			body.AddChild(CreateCardsDrawer(summary), forceReadableName: false, Node.InternalMode.Disabled);
		}
	}

	private static HFlowContainer CreateCompactSummaryRow(PlayerTurnSummary summary)
	{
		Color energyNetColor = GetSignedMetricColor(summary.EnergyNet, _blockGainColor, _takeColor);
		Color energyNetBg = summary.EnergyNet < 0 ? _chipTakeBg : _chipBlockBg;

		HFlowContainer chips = new();
		chips.AddThemeConstantOverride("h_separation", 4);
		chips.AddThemeConstantOverride("v_separation", 4);
		chips.AddChild(CreateStatChip($"Cards {summary.CardsDrawn}/{summary.CardsPlayed}", _cardsColor, _chipCardsBg), forceReadableName: false, Node.InternalMode.Disabled);
		chips.AddChild(CreateStatChip($"Energy {FormatSignedValue(summary.EnergyNet)}", energyNetColor, energyNetBg), forceReadableName: false, Node.InternalMode.Disabled);
		chips.AddChild(CreateStatChip($"Out {summary.DamageDealtTotal}", _dealColor, _chipDealBg), forceReadableName: false, Node.InternalMode.Disabled);
		chips.AddChild(CreateStatChip($"In {summary.DamageReceivedTotal}", _takeColor, _chipTakeBg), forceReadableName: false, Node.InternalMode.Disabled);
		chips.AddChild(CreateStatChip($"Block +{summary.BlockGained}/-{summary.TotalBlockLost}", _blockGainColor, _chipBlockBg), forceReadableName: false, Node.InternalMode.Disabled);
		if (summary.BlockBreaks > 0)
		{
			chips.AddChild(CreateStatChip($"Break {summary.BlockBreaks}", _breakColor, _chipBreakBg), forceReadableName: false, Node.InternalMode.Disabled);
		}

		return chips;
	}

	private static PanelContainer CreateDetailRow(string title, params (string Label, string Value, Color Color)[] metrics)
	{
		PanelContainer panel = CreateDetailShell();

		HBoxContainer row = new();
		row.AddThemeConstantOverride("separation", 8);
		row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		panel.AddChild(row, forceReadableName: false, Node.InternalMode.Disabled);

		Label titleLabel = UiHelpers.CreateLabel(title, _mutedColor, 10);
		titleLabel.CustomMinimumSize = new Vector2(78f, 0f);
		row.AddChild(titleLabel, forceReadableName: false, Node.InternalMode.Disabled);

		HFlowContainer metricsFlow = new();
		metricsFlow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		metricsFlow.AddThemeConstantOverride("h_separation", 10);
		metricsFlow.AddThemeConstantOverride("v_separation", 4);
		foreach ((string label, string value, Color color) in metrics)
		{
			metricsFlow.AddChild(CreateDetailMetric(label, value, color), forceReadableName: false, Node.InternalMode.Disabled);
		}
		row.AddChild(metricsFlow, forceReadableName: false, Node.InternalMode.Disabled);

		return panel;
	}

	private static PanelContainer CreateDetailTextRow(string title, string text, Color color)
	{
		PanelContainer panel = CreateDetailShell();

		HBoxContainer row = new();
		row.AddThemeConstantOverride("separation", 8);
		row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		panel.AddChild(row, forceReadableName: false, Node.InternalMode.Disabled);

		Label titleLabel = UiHelpers.CreateLabel(title, _mutedColor, 10);
		titleLabel.CustomMinimumSize = new Vector2(78f, 0f);
		row.AddChild(titleLabel, forceReadableName: false, Node.InternalMode.Disabled);

		Label contentLabel = UiHelpers.CreateLabel(text, color, 10);
		contentLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		contentLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		row.AddChild(contentLabel, forceReadableName: false, Node.InternalMode.Disabled);

		return panel;
	}

	private static string? BuildBlockSourcesText(PlayerTurnSummary summary)
	{
		List<string> sources = summary.BlockSources
			.Take(3)
			.Select(pair => $"{pair.Key} {pair.Value}")
			.ToList();

		if (sources.Count == 0)
		{
			return null;
		}

		return string.Join("  |  ", sources);
	}

	private static PanelContainer CreateDetailShell()
	{
		PanelContainer panel = new();
		panel.AddThemeStyleboxOverride("panel", UiHelpers.CreatePanelStyle(new Color(0.05f, 0.06f, 0.10f, 0.78f), _sectionBorder, 1, 3, 4f));
		return panel;
	}

	private static HBoxContainer CreateDetailMetric(string label, string value, Color color)
	{
		HBoxContainer metric = new();
		metric.AddThemeConstantOverride("separation", 3);
		metric.AddChild(UiHelpers.CreateLabel(label, _mutedColor, 9), forceReadableName: false, Node.InternalMode.Disabled);
		metric.AddChild(UiHelpers.CreateLabel(value, color, 10), forceReadableName: false, Node.InternalMode.Disabled);
		return metric;
	}

	private static (string Label, string Value, Color Color)[] BuildCardsMetrics(PlayerTurnSummary summary)
	{
		List<(string Label, string Value, Color Color)> metrics =
		[
			("Draw", summary.CardsDrawn.ToString(), _cardsColor),
			("Play", summary.CardsPlayed.ToString(), _cardsColor)
		];

		if (summary.CardsDiscarded > 0)
		{
			metrics.Add(("Discard", summary.CardsDiscarded.ToString(), _cardsColor));
		}
		if (summary.CardsGenerated > 0)
		{
			metrics.Add(("Gen", summary.CardsGenerated.ToString(), _cardsColor));
		}
		if (summary.CardsExhausted > 0)
		{
			metrics.Add(("Exhaust", summary.CardsExhausted.ToString(), _cardsColor));
		}

		return [.. metrics];
	}

	private static (string Label, string Value, Color Color)[] BuildEnergyMetrics(PlayerTurnSummary summary)
	{
		return
		[
			("Gain", summary.EnergyGained.ToString(), _blockGainColor),
			("Spent", summary.EnergySpent.ToString(), _takeColor),
			("Net", FormatSignedValue(summary.EnergyNet), GetSignedMetricColor(summary.EnergyNet, _blockGainColor, _takeColor))
		];
	}

	private static (string Label, string Value, Color Color)[] BuildDamageMetrics(int totalDamage, int hpDamage, int blockedDamage, Color totalColor)
	{
		return
		[
			("Total", totalDamage.ToString(), totalColor),
			("HP", hpDamage.ToString(), hpDamage > 0 ? totalColor : _mutedColor),
			("Block", blockedDamage.ToString(), blockedDamage > 0 ? _blockGainColor : _mutedColor)
		];
	}

	private static (string Label, string Value, Color Color)[] BuildBlockMetrics(PlayerTurnSummary summary)
	{
		List<(string Label, string Value, Color Color)> metrics =
		[
			("Gain", summary.BlockGained.ToString(), _blockGainColor),
			("Spent", summary.BlockSpent.ToString(), summary.BlockSpent > 0 ? _blockLossColor : _mutedColor)
		];

		if (summary.BlockLost > 0)
		{
			metrics.Add(("Lose", summary.BlockLost.ToString(), _blockLossColor));
		}
		if (summary.BlockCleared > 0)
		{
			metrics.Add(("Clear", summary.BlockCleared.ToString(), _blockLossColor));
		}
		if (summary.BlockBreaks > 0)
		{
			metrics.Add(("Break", summary.BlockBreaks.ToString(), _breakColor));
		}

		return [.. metrics];
	}

	private static PanelContainer CreateCardsDrawer(PlayerTurnSummary summary)
	{
		bool isExpanded = IsCardSectionExpanded(summary.Creature);

		PanelContainer panel = CreateDetailShell();
		VBoxContainer body = new();
		body.AddThemeConstantOverride("separation", 4);
		panel.AddChild(body, forceReadableName: false, Node.InternalMode.Disabled);

		Button button = CreateCardsDrawerButton(summary.CardsPlayed, isExpanded);
		button.Pressed += () => ToggleCardSection(summary.Creature);
		RegisterInteractiveControl(button);
		body.AddChild(button, forceReadableName: false, Node.InternalMode.Disabled);

		if (!isExpanded)
		{
			return panel;
		}

		HFlowContainer cardFlow = new();
		cardFlow.AddThemeConstantOverride("h_separation", 4);
		cardFlow.AddThemeConstantOverride("v_separation", 4);
		foreach (PlayedCardSummary card in summary.Cards)
		{
			cardFlow.AddChild(CreateCardThumbnail(card), forceReadableName: false, Node.InternalMode.Disabled);
		}
		body.AddChild(cardFlow, forceReadableName: false, Node.InternalMode.Disabled);

		return panel;
	}

	private static Button CreateCardsDrawerButton(int cardsPlayed, bool isExpanded)
	{
		Button button = new();
		button.Text = isExpanded ? $"\u25BE Played Cards ({cardsPlayed})" : $"\u25B8 Played Cards ({cardsPlayed})";
		button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		button.CustomMinimumSize = new Vector2(0f, 24f);
		button.AddThemeColorOverride("font_color", _allyColor);
		button.AddThemeColorOverride("font_disabled_color", new Color(_mutedColor, 0.35f));
		button.AddThemeFontSizeOverride("font_size", 10);

		StyleBoxFlat normalStyle = new();
		normalStyle.BgColor = new Color(0.08f, 0.10f, 0.14f, 0.85f);
		normalStyle.BorderColor = _sectionBorder;
		normalStyle.SetBorderWidthAll(1);
		normalStyle.SetCornerRadiusAll(3);
		normalStyle.SetContentMarginAll(3f);
		button.AddThemeStyleboxOverride("normal", normalStyle);

		StyleBoxFlat hoverStyle = new();
		hoverStyle.BgColor = new Color(0.10f, 0.12f, 0.17f, 0.90f);
		hoverStyle.BorderColor = _localPlayerColor;
		hoverStyle.SetBorderWidthAll(1);
		hoverStyle.SetCornerRadiusAll(3);
		hoverStyle.SetContentMarginAll(3f);
		button.AddThemeStyleboxOverride("hover", hoverStyle);
		button.AddThemeStyleboxOverride("pressed", hoverStyle);

		return button;
	}

	private static string FormatSignedValue(int value)
	{
		return value > 0 ? $"+{value}" : value.ToString();
	}

	private static Color GetSignedMetricColor(int value, Color positiveColor, Color negativeColor)
	{
		if (value > 0)
		{
			return positiveColor;
		}

		if (value < 0)
		{
			return negativeColor;
		}

		return _mutedColor;
	}

	private static PanelContainer CreateStatChip(string text, Color textColor, Color bgColor)
	{
		StyleBoxFlat style = new();
		style.BgColor = bgColor;
		style.SetCornerRadiusAll(3);
		style.ContentMarginLeft = 4f;
		style.ContentMarginRight = 4f;
		style.ContentMarginTop = 1f;
		style.ContentMarginBottom = 1f;
		style.SetBorderWidthAll(0);

		PanelContainer chip = new();
		chip.AddThemeStyleboxOverride("panel", style);

		Label label = new();
		label.Text = text;
		label.AddThemeColorOverride("font_color", textColor);
		label.AddThemeFontSizeOverride("font_size", 11);
		label.MouseFilter = Control.MouseFilterEnum.Ignore;
		chip.AddChild(label, forceReadableName: false, Node.InternalMode.Disabled);

		return chip;
	}

	private static PanelContainer CreateCardThumbnail(PlayedCardSummary card)
	{
		Color borderColor = GetCardTypeColor(card.Card);

		StyleBoxFlat style = new();
		style.BgColor = new Color(0.06f, 0.06f, 0.10f, 0.95f);
		style.BorderColor = borderColor;
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(3);
		style.SetContentMarginAll(0f);

		PanelContainer container = new();
		container.CustomMinimumSize = new Vector2(CardThumbWidth, CardThumbPortraitHeight + 24f);
		container.AddThemeStyleboxOverride("panel", style);

		VBoxContainer col = new();
		col.AddThemeConstantOverride("separation", 0);
		col.MouseFilter = Control.MouseFilterEnum.Ignore;
		container.AddChild(col, forceReadableName: false, Node.InternalMode.Disabled);

		Control portraitClip = new();
		portraitClip.CustomMinimumSize = new Vector2(CardThumbWidth, CardThumbPortraitHeight);
		portraitClip.ClipContents = true;
		portraitClip.MouseFilter = Control.MouseFilterEnum.Ignore;
		col.AddChild(portraitClip, forceReadableName: false, Node.InternalMode.Disabled);

		try
		{
			Texture2D? portrait = card.Card.Portrait;
			if (portrait != null)
			{
				TextureRect portraitRect = new();
				portraitRect.Texture = portrait;
				portraitRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
				portraitRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
				portraitRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
				portraitRect.MouseFilter = Control.MouseFilterEnum.Ignore;
				portraitClip.AddChild(portraitRect, forceReadableName: false, Node.InternalMode.Disabled);
			}
		}
		catch
		{
		}

		PanelContainer footer = new();
		footer.MouseFilter = Control.MouseFilterEnum.Ignore;
		StyleBoxFlat footerStyle = new();
		footerStyle.BgColor = new Color(0.04f, 0.04f, 0.07f, 0.95f);
		footerStyle.SetContentMarginAll(0f);
		footerStyle.ContentMarginLeft = 2f;
		footerStyle.ContentMarginRight = 2f;
		footerStyle.ContentMarginTop = 1f;
		footerStyle.ContentMarginBottom = 1f;
		footer.AddThemeStyleboxOverride("panel", footerStyle);

		VBoxContainer footerCol = new();
		footerCol.AddThemeConstantOverride("separation", 0);
		footerCol.MouseFilter = Control.MouseFilterEnum.Ignore;
		footer.AddChild(footerCol, forceReadableName: false, Node.InternalMode.Disabled);

		Label nameLabel = new();
		nameLabel.Text = card.Name;
		nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		nameLabel.ClipText = true;
		nameLabel.CustomMinimumSize = new Vector2(CardThumbWidth - 4f, 0f);
		nameLabel.AddThemeColorOverride("font_color", borderColor);
		nameLabel.AddThemeFontSizeOverride("font_size", 8);
		nameLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
		footerCol.AddChild(nameLabel, forceReadableName: false, Node.InternalMode.Disabled);

		string? statText = null;
		Color statColor = _mutedColor;

		if (card.TotalDamage > 0)
		{
			statText = card.BlockedDamage > 0
				? $"{card.TotalDamage} ({card.HpDamage}+{card.BlockedDamage})"
				: $"{card.TotalDamage} dmg";
			statColor = _dealColor;
		}
		else if (card.BlockGained > 0)
		{
			statText = $"+{card.BlockGained} blk";
			statColor = _blockGainColor;
		}

		if (statText != null)
		{
			Label statLabel = new();
			statLabel.Text = statText;
			statLabel.HorizontalAlignment = HorizontalAlignment.Center;
			statLabel.ClipText = true;
			statLabel.CustomMinimumSize = new Vector2(CardThumbWidth - 4f, 0f);
			statLabel.AddThemeColorOverride("font_color", statColor);
			statLabel.AddThemeFontSizeOverride("font_size", 8);
			statLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
			footerCol.AddChild(statLabel, forceReadableName: false, Node.InternalMode.Disabled);
		}

		col.AddChild(footer, forceReadableName: false, Node.InternalMode.Disabled);
		return container;
	}

	private static Color GetCardTypeColor(CardModel card)
	{
		try
		{
			string typeName = card.Type.ToString() ?? string.Empty;
			if (typeName.Contains("Attack")) return _cardAttackBorder;
			if (typeName.Contains("Skill")) return _cardSkillBorder;
			if (typeName.Contains("Power")) return _cardPowerBorder;
		}
		catch
		{
		}

		return _cardDefaultBorder;
	}
}