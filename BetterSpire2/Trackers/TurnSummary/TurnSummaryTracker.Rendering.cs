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
			|| !UiHelpers.IsValid(_nextRoundButton) || !UiHelpers.IsValid(_statsTabButton)
			|| !UiHelpers.IsValid(_logTabButton))
			return;

		ClearContainer(_contentContainer!);
		_titleLabel!.Text = GetWindowTitle();
		ApplyTabStyle(_statsTabButton!, _activeTab == Tab.Stats);
		ApplyTabStyle(_logTabButton!, _activeTab == Tab.Log);
		_scopeButton!.Text = _viewMode == HistoryViewMode.Combat ? $"R{GetSelectedRoundNumber()}" : "All";
		_collapseButton!.Text = ModSettings.TurnSummaryCollapsed ? "+" : "\u2212";
		_prevRoundButton!.Disabled = _viewMode == HistoryViewMode.Combat || GetSelectedRoundNumber() <= 1;
		_nextRoundButton!.Disabled = _viewMode == HistoryViewMode.Combat || GetSelectedRoundNumber() >= Math.Max(1, _trackedRound);
		_scrollContainer!.CustomMinimumSize = new Vector2(0f, Math.Max(
			ModSettings.TurnSummaryCollapsed ? 40f : 80f,
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

		if (_activeTab == Tab.Stats)
		{
			RenderStatsContent(body, summary);
		}
		else
		{
			RenderLogContent(body, summary);
		}

		return panel;
	}

	private static void RenderStatsContent(VBoxContainer body, PlayerTurnSummary summary)
	{
		HBoxContainer chips = new();
		chips.AddThemeConstantOverride("separation", 3);
		chips.AddChild(CreateStatChip($"Dealt {summary.DamageDealtTotal}", _dealColor, _chipDealBg), forceReadableName: false, Node.InternalMode.Disabled);
		chips.AddChild(CreateStatChip($"Taken {summary.DamageReceivedTotal}", _takeColor, _chipTakeBg), forceReadableName: false, Node.InternalMode.Disabled);
		chips.AddChild(CreateStatChip($"Blk +{summary.BlockGained}/\u2212{summary.TotalBlockLost}", _blockGainColor, _chipBlockBg), forceReadableName: false, Node.InternalMode.Disabled);
		if (summary.BlockBreaks > 0)
		{
			chips.AddChild(CreateStatChip($"Brk {summary.BlockBreaks}", _breakColor, _chipBreakBg), forceReadableName: false, Node.InternalMode.Disabled);
		}
		body.AddChild(chips, forceReadableName: false, Node.InternalMode.Disabled);

		if (ModSettings.TurnSummaryCollapsed)
		{
			return;
		}

		if (summary.Cards.Count > 0)
		{
			HFlowContainer cardFlow = new();
			cardFlow.AddThemeConstantOverride("h_separation", 4);
			cardFlow.AddThemeConstantOverride("v_separation", 4);
			foreach (PlayedCardSummary card in summary.Cards)
			{
				cardFlow.AddChild(CreateCardThumbnail(card), forceReadableName: false, Node.InternalMode.Disabled);
			}
			body.AddChild(cardFlow, forceReadableName: false, Node.InternalMode.Disabled);
		}
	}

	private static void RenderLogContent(VBoxContainer body, PlayerTurnSummary summary)
	{
		List<TurnTimelineEntry> entries = GetTimelineFor(summary.Creature);

		if (entries.Count == 0)
		{
			body.AddChild(UiHelpers.CreateLabel("No events.", _mutedColor, 11), forceReadableName: false, Node.InternalMode.Disabled);
			return;
		}

		const int maxVisible = 20;
		IEnumerable<TurnTimelineEntry> visible = entries.Count > maxVisible ? entries.Skip(entries.Count - maxVisible) : entries;

		if (entries.Count > maxVisible)
		{
			body.AddChild(UiHelpers.CreateLabel($"\u2026 {entries.Count - maxVisible} earlier", _mutedColor, 10), forceReadableName: false, Node.InternalMode.Disabled);
		}

		foreach (TurnTimelineEntry entry in visible)
		{
			string prefix = _viewMode == HistoryViewMode.Combat ? $"R{entry.RoundNumber}  " : "";
			Label label = UiHelpers.CreateLabel($"{prefix}{entry.Text}", entry.Color, 11);
			label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			body.AddChild(label, forceReadableName: false, Node.InternalMode.Disabled);
		}
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