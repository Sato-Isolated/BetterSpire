#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Platform;

namespace BetterSpire2.Trackers;

public static partial class DeckTracker
{
	private class HandPanel : PanelContainer
	{
	}

	private class PlayerHandSection
	{
		private class PotionIcon : TextureRect
		{
			private readonly string _potionName;

			private readonly string _potionDesc;

			private Control? _tooltip;

			public PotionIcon(PotionModel potion)
			{
				try
				{
					base.Texture = potion.Image;
					_potionName = potion.Title?.GetFormattedText() ?? "???";
					_potionDesc = potion.DynamicDescription?.GetFormattedText() ?? "";
				}
				catch
				{
					_potionName = "???";
					_potionDesc = "";
				}
				_potionDesc = Regex.Replace(_potionDesc, "\\[/?[^\\]]+\\]", "");
				_potionDesc = Regex.Replace(_potionDesc, "res://\\S+", "").Trim();
				base.CustomMinimumSize = new Vector2(22f, 22f);
				base.ExpandMode = ExpandModeEnum.IgnoreSize;
				base.StretchMode = StretchModeEnum.KeepAspectCentered;
				base.MouseFilter = MouseFilterEnum.Stop;
				base.MouseEntered += ShowTooltip;
				base.MouseExited += HideTooltip;
			}

			private void ShowTooltip()
			{
				HideTooltip();
				try
				{
					PanelContainer panelContainer = new PanelContainer();
					StyleBoxFlat styleBoxFlat = new StyleBoxFlat();
					styleBoxFlat.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
					styleBoxFlat.BorderColor = new Color(0.6f, 0.5f, 0.2f);
					styleBoxFlat.SetBorderWidthAll(1);
					styleBoxFlat.SetCornerRadiusAll(4);
					styleBoxFlat.SetContentMarginAll(8f);
					panelContainer.AddThemeStyleboxOverride("panel", styleBoxFlat);
					VBoxContainer vBoxContainer = new VBoxContainer();
					vBoxContainer.AddThemeConstantOverride("separation", 4);
					panelContainer.AddChild(vBoxContainer, forceReadableName: false, InternalMode.Disabled);
					Label label = new Label();
					label.Text = _potionName;
					label.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.3f));
					label.AddThemeFontSizeOverride("font_size", 13);
					vBoxContainer.AddChild(label, forceReadableName: false, InternalMode.Disabled);
					if (!string.IsNullOrEmpty(_potionDesc))
					{
						Label label2 = new Label();
						label2.Text = _potionDesc;
						label2.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
						label2.AddThemeFontSizeOverride("font_size", 11);
						label2.AutowrapMode = TextServer.AutowrapMode.WordSmart;
						label2.CustomMinimumSize = new Vector2(200f, 0f);
						vBoxContainer.AddChild(label2, forceReadableName: false, InternalMode.Disabled);
					}
					panelContainer.ZIndex = 300;
					if (_canvasLayer != null && GodotObject.IsInstanceValid(_canvasLayer))
					{
						_canvasLayer.AddChild(panelContainer, forceReadableName: false, InternalMode.Disabled);
						panelContainer.Position = base.GlobalPosition + new Vector2(0f, 26f);
						_tooltip = panelContainer;
					}
				}
				catch (Exception ex)
				{
					ModLog.Error("PotionIcon tooltip show", ex);
				}
			}

			private void HideTooltip()
			{
				try
				{
					if (_tooltip != null && GodotObject.IsInstanceValid(_tooltip))
					{
						_tooltip.QueueFree();
					}
					_tooltip = null;
				}
				catch
				{
				}
			}
		}

		private readonly Player _player;

		private CardPile? _hand;

		private readonly List<Control> _cardControls = new List<Control>();

		private readonly List<CardModel> _subscribedCards = new List<CardModel>();

		private Container? _cardRow;

		private Label? _nameLabel;

		private HBoxContainer? _statsRow;

		private HBoxContainer? _potionRow;

		private readonly ulong _localNetId;

		private readonly string? _debugSuffix;

		private const int MaxHandSize = 12;

		private const int BaseCardWidth = 58;

		private const int BasePortraitHeight = 44;

		private static readonly Color AttackColor = new Color(0.9f, 0.35f, 0.3f);

		private static readonly Color SkillColor = new Color(0.3f, 0.55f, 0.9f);

		private static readonly Color PowerColor = new Color(0.9f, 0.75f, 0.2f);

		private static readonly Color StatusColor = new Color(0.5f, 0.5f, 0.5f);

		private static readonly Color CurseColor = new Color(0.7f, 0.2f, 0.7f);

		private static readonly Color DefaultCardColor = new Color(0.7f, 0.7f, 0.7f);

		private static readonly Texture2D? IconVuln = GD.Load<Texture2D>("res://images/powers/vulnerable_power.png");

		private static readonly Texture2D? IconWeak = GD.Load<Texture2D>("res://images/powers/weak_power.png");

		private static readonly Texture2D? IconDefend = GD.Load<Texture2D>("res://images/packed/intents/intent_defend.png");

		private static float CardScale => (float)ModSettings.CardScalePercent / 100f;

		private static int CardWidth => (int)(BaseCardWidth * CardScale);

		private static int PortraitHeight => (int)(BasePortraitHeight * CardScale);

		public bool IsHandNull => _hand == null;

		private static int ScaledFont(int baseSize)
		{
			return Math.Max(7, (int)(baseSize * CardScale));
		}

		public PlayerHandSection(Player player, ulong localNetId, string? debugSuffix = null)
		{
			_player = player;
			_hand = player.PlayerCombatState?.Hand;
			_localNetId = localNetId;
			_debugSuffix = debugSuffix;
		}

		public void AddTo(VBoxContainer parent)
		{
			parent.AddChild(new HSeparator(), forceReadableName: false, Node.InternalMode.Disabled);
			bool isLocalPlayer = _player.NetId == _localNetId;
			string playerName = PlatformUtil.GetPlayerName(PlatformType.Steam, _player.NetId);
			string? characterName = _player.Character?.Title?.GetFormattedText();
			string displayName = !string.IsNullOrEmpty(playerName) ? playerName : (characterName ?? "Player");
			if (!string.IsNullOrEmpty(characterName) && !string.IsNullOrEmpty(playerName))
			{
				displayName = playerName + " (" + characterName + ")";
			}
			if (isLocalPlayer && _debugSuffix == null)
			{
				displayName += " (You)";
			}
			if (_debugSuffix != null)
			{
				displayName += _debugSuffix;
			}

			_nameLabel = new Label();
			UpdateNameLabel(displayName);
			_nameLabel.AddThemeColorOverride("font_color", isLocalPlayer ? new Color(0.5f, 0.8f, 0.5f) : new Color(0.9f, 0.9f, 0.9f));
			_nameLabel.AddThemeFontSizeOverride("font_size", 14);
			parent.AddChild(_nameLabel, forceReadableName: false, Node.InternalMode.Disabled);

			HBoxContainer statsContainer = new HBoxContainer();
			statsContainer.AddThemeConstantOverride("separation", 6);
			parent.AddChild(statsContainer, forceReadableName: false, Node.InternalMode.Disabled);

			_statsRow = new HBoxContainer();
			_statsRow.AddThemeConstantOverride("separation", 10);
			statsContainer.AddChild(_statsRow, forceReadableName: false, Node.InternalMode.Disabled);

			_potionRow = new HBoxContainer();
			_potionRow.AddThemeConstantOverride("separation", 4);
			statsContainer.AddChild(_potionRow, forceReadableName: false, Node.InternalMode.Disabled);

			RefreshPotions();
			if (!ModSettings.CompactHandViewer)
			{
				HFlowContainer cardFlow = new HFlowContainer();
				cardFlow.AddThemeConstantOverride("h_separation", 6);
				cardFlow.AddThemeConstantOverride("v_separation", 6);
				_cardRow = cardFlow;
				parent.AddChild(_cardRow, forceReadableName: false, Node.InternalMode.Disabled);
			}

			RefreshCards();
			SubscribeHand();
			_player.PotionProcured += OnPotionChanged;
			_player.PotionDiscarded += OnPotionChanged;
			_player.UsedPotionRemoved += OnPotionChanged;
			if (_player.Creature != null)
			{
				_player.Creature.PowerApplied += OnPowerChanged;
				_player.Creature.PowerRemoved += OnPowerChanged;
				_player.Creature.PowerIncreased += OnPowerStackChanged;
				_player.Creature.PowerDecreased += OnPowerStackChanged;
			}
		}

		public void Cleanup()
		{
			UnsubscribeHand();
			_player.PotionProcured -= OnPotionChanged;
			_player.PotionDiscarded -= OnPotionChanged;
			_player.UsedPotionRemoved -= OnPotionChanged;
			if (_player.Creature != null)
			{
				_player.Creature.PowerApplied -= OnPowerChanged;
				_player.Creature.PowerRemoved -= OnPowerChanged;
				_player.Creature.PowerIncreased -= OnPowerStackChanged;
				_player.Creature.PowerDecreased -= OnPowerStackChanged;
			}
			UnsubscribeCardUpgrades();
			ClearCards();
		}

		private void SubscribeHand()
		{
			if (_hand != null)
			{
				_hand.ContentsChanged += OnHandChanged;
				_hand.CardAdded += OnCardAddedOrRemoved;
				_hand.CardRemoved += OnCardAddedOrRemoved;
			}
		}

		private void UnsubscribeHand()
		{
			if (_hand != null)
			{
				_hand.ContentsChanged -= OnHandChanged;
				_hand.CardAdded -= OnCardAddedOrRemoved;
				_hand.CardRemoved -= OnCardAddedOrRemoved;
			}
		}

		public void RefreshHandReference()
		{
			CardPile? cardPile = _player.PlayerCombatState?.Hand;
			if (cardPile != _hand || _hand == null)
			{
				UnsubscribeHand();
				_hand = cardPile;
				SubscribeHand();
				RefreshCards();
			}
		}

		private void OnHandChanged()
		{
			try
			{
				RefreshCards();
			}
			catch (Exception ex)
			{
				ModLog.Error("PlayerHandSection.OnHandChanged", ex);
			}
		}

		private void OnCardAddedOrRemoved(CardModel _)
		{
			try
			{
				RefreshCards();
			}
			catch (Exception ex)
			{
				ModLog.Error("PlayerHandSection.OnCardAddedOrRemoved", ex);
			}
		}

		private void OnPotionChanged(PotionModel _)
		{
			try
			{
				RefreshPotions();
			}
			catch (Exception ex)
			{
				ModLog.Error("PlayerHandSection.OnPotionChanged", ex);
			}
		}

		private void OnPowerChanged(PowerModel _)
		{
			try
			{
				RefreshCards();
			}
			catch (Exception ex)
			{
				ModLog.Error("PlayerHandSection.OnPowerChanged", ex);
			}
		}

		private void OnPowerStackChanged(PowerModel _, int _2, bool _3)
		{
			try
			{
				RefreshCards();
			}
			catch (Exception ex)
			{
				ModLog.Error("PlayerHandSection.OnPowerStackChanged", ex);
			}
		}

		private void OnPowerStackChanged(PowerModel _, bool _2)
		{
			try
			{
				RefreshCards();
			}
			catch (Exception ex)
			{
				ModLog.Error("PlayerHandSection.OnPowerStackChanged", ex);
			}
		}

		private void UnsubscribeCardUpgrades()
		{
			foreach (CardModel subscribedCard in _subscribedCards)
			{
				try
				{
					subscribedCard.Upgraded -= OnCardUpgraded;
				}
				catch
				{
				}
			}
			_subscribedCards.Clear();
		}

		private void SubscribeCardUpgrades()
		{
			if (_hand?.Cards == null)
			{
				return;
			}

			foreach (CardModel card in _hand.Cards)
			{
				try
				{
					card.Upgraded += OnCardUpgraded;
					_subscribedCards.Add(card);
				}
				catch
				{
				}
			}
		}

		private void OnCardUpgraded()
		{
			try
			{
				RefreshCards();
			}
			catch (Exception ex)
			{
				ModLog.Error("PlayerHandSection.OnCardUpgraded", ex);
			}
		}

		private void RefreshCards()
		{
			if (_hand == null)
			{
				return;
			}

			IReadOnlyList<CardModel>? cards = _hand.Cards;
			UnsubscribeCardUpgrades();
			int cardCount = cards?.Count ?? 0;
			int totalDamage = 0;
			int totalBlock = 0;
			int weakStacks = 0;
			int vulnerableStacks = 0;

			if (cardCount > 0 && cards != null)
			{
				foreach (CardModel card in cards)
				{
					try
					{
						TryUpdateDynamicVarPreview(card);
						if (!TryGetDynamicVarPreviewValue(card, "Damage", out int damagePreview) && !TryGetDynamicVarPreviewValue(card, "CalculatedDamage", out damagePreview))
						{
							damagePreview = 0;
						}
						totalDamage += damagePreview;

						if (card.GetType().Name != "SecondWind")
						{
							if (!TryGetDynamicVarPreviewValue(card, "Block", out int blockPreview) && !TryGetDynamicVarPreviewValue(card, "CalculatedBlock", out blockPreview))
							{
								blockPreview = 0;
							}
							totalBlock += blockPreview;
						}

						if (TryGetDynamicVarBaseValue(card, "WeakPower", out int weakValue))
						{
							weakStacks += weakValue;
						}
						if (TryGetDynamicVarBaseValue(card, "VulnerablePower", out int vulnerableValue))
						{
							vulnerableStacks += vulnerableValue;
						}
						if (TryGetDynamicVarBaseValue(card, "Power", out int powerValue) && powerValue > 0)
						{
							weakStacks += powerValue;
							vulnerableStacks += powerValue;
						}
					}
					catch
					{
					}
				}
			}

			UpdateNameLabel(null, cardCount, totalDamage, totalBlock, weakStacks, vulnerableStacks);
			SubscribeCardUpgrades();
			if (ModSettings.CompactHandViewer || _cardRow == null || !GodotObject.IsInstanceValid(_cardRow))
			{
				return;
			}

			ClearCards();
			if (cardCount == 0)
			{
				return;
			}

			int displayedCardCount = Math.Min(cardCount, MaxHandSize);
			for (int index = 0; index < displayedCardCount; index++)
			{
				try
				{
					CardModel cardModel = cards![index];
					if (cardModel != null)
					{
						Control control = CreateCompactCard(cardModel);
						_cardRow.AddChild(control, forceReadableName: false, Node.InternalMode.Disabled);
						_cardControls.Add(control);
					}
				}
				catch (Exception ex)
				{
					ModLog.Error($"PlayerHandSection: failed to create card {index}", ex);
				}
			}

			if (cardCount > MaxHandSize)
			{
				Label overflowLabel = new Label();
				overflowLabel.Text = $"+{cardCount - MaxHandSize}";
				overflowLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
				overflowLabel.AddThemeFontSizeOverride("font_size", 12);
				_cardRow.AddChild(overflowLabel, forceReadableName: false, Node.InternalMode.Disabled);
				_cardControls.Add(overflowLabel);
			}
		}

		private static void TryUpdateDynamicVarPreview(CardModel card)
		{
			try
			{
				var dynamicVars = card.DynamicVars;
				if (dynamicVars != null)
				{
					card.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, dynamicVars);
				}
			}
			catch (Exception ex)
			{
				ModLog.Error("UpdateDynVarPreview", ex);
			}
		}

		private static bool TryGetDynamicVarPreviewValue(CardModel card, string key, out int value)
		{
			value = 0;
			try
			{
				var dynamicVars = card.DynamicVars;
				if (dynamicVars == null || !dynamicVars.TryGetValue(key, out var dynamicVar) || dynamicVar == null)
				{
					return false;
				}
				try
				{
					value = (int)dynamicVar.PreviewValue;
					return true;
				}
				catch
				{
					try
					{
						value = dynamicVar.IntValue;
						return true;
					}
					catch
					{
						return false;
					}
				}
			}
			catch
			{
				return false;
			}
		}

		private static bool TryGetDynamicVarBaseValue(CardModel card, string key, out int value)
		{
			value = 0;
			try
			{
				var dynamicVars = card.DynamicVars;
				if (dynamicVars == null || !dynamicVars.TryGetValue(key, out var dynamicVar) || dynamicVar == null)
				{
					return false;
				}
				try
				{
					value = dynamicVar.IntValue;
					return true;
				}
				catch
				{
					try
					{
						value = (int)dynamicVar.BaseValue;
						return true;
					}
					catch
					{
						return false;
					}
				}
			}
			catch
			{
				return false;
			}
		}

		private void RefreshPotions()
		{
			if (_potionRow == null || !GodotObject.IsInstanceValid(_potionRow))
			{
				return;
			}

			foreach (Node child in _potionRow.GetChildren())
			{
				child.QueueFree();
			}

			try
			{
				IEnumerable<PotionModel> potions = _player.Potions;
				if (potions == null)
				{
					return;
				}

				bool hasPotions = false;
				foreach (PotionModel potion in potions)
				{
					if (potion != null)
					{
						if (!hasPotions)
						{
							Label label = new Label();
							label.Text = "Potions:";
							label.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
							label.AddThemeFontSizeOverride("font_size", 11);
							label.MouseFilter = Control.MouseFilterEnum.Ignore;
							_potionRow.AddChild(label, forceReadableName: false, Node.InternalMode.Disabled);
							hasPotions = true;
						}
						_potionRow.AddChild(new PotionIcon(potion), forceReadableName: false, Node.InternalMode.Disabled);
					}
				}
			}
			catch (Exception ex)
			{
				ModLog.Error("RefreshPotions", ex);
			}
		}

		private static Control CreateCompactCard(CardModel card)
		{
			try
			{
				Texture2D? portrait = null;
				string cardName = "???";
				string costText = "";
				Color typeColor = DefaultCardColor;

				try
				{
					portrait = card.Portrait;
				}
				catch
				{
				}

				try
				{
					string title = card.Title;
					if (!string.IsNullOrEmpty(title))
					{
						cardName = title;
					}
					if (card.IsUpgraded && cardName != "???")
					{
						cardName += "+";
					}
				}
				catch
				{
				}

				try
				{
					CardEnergyCost energyCost = card.EnergyCost;
					if (energyCost != null)
					{
						if (energyCost.CostsX)
						{
							costText = "X";
						}
						else if (energyCost.Canonical >= 0)
						{
							costText = energyCost.Canonical.ToString();
						}
					}
				}
				catch
				{
				}

				try
				{
					string typeName = card.Type.ToString() ?? "";
					if (typeName.Contains("Attack"))
					{
						typeColor = AttackColor;
					}
					else if (typeName.Contains("Skill"))
					{
						typeColor = SkillColor;
					}
					else if (typeName.Contains("Power"))
					{
						typeColor = PowerColor;
					}
					else if (typeName.Contains("Status"))
					{
						typeColor = StatusColor;
					}
					else if (typeName.Contains("Curse"))
					{
						typeColor = CurseColor;
					}
				}
				catch
				{
				}

				return BuildCompactCard(portrait, cardName, costText, ResolveDescription(card), typeColor);
			}
			catch (Exception ex)
			{
				ModLog.Error("CreateCompactCard", ex);
			}

			return CreateCardFallback(card);
		}

		private static Control BuildCompactCard(Texture2D? portrait, string cardName, string costText, string description, Color typeColor)
		{
			CardPanel cardPanel = new CardPanel();
			cardPanel.CustomMinimumSize = new Vector2(CardWidth, PortraitHeight + 16);
			cardPanel.CardName = cardName;
			cardPanel.CardDescription = description;
			cardPanel.WireUpTooltip();

			StyleBoxFlat panelStyle = new StyleBoxFlat();
			panelStyle.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.95f);
			panelStyle.BorderColor = typeColor;
			panelStyle.SetBorderWidthAll(1);
			panelStyle.SetCornerRadiusAll(4);
			panelStyle.SetContentMarginAll(0f);
			cardPanel.AddThemeStyleboxOverride("panel", panelStyle);

			VBoxContainer content = new VBoxContainer();
			content.AddThemeConstantOverride("separation", 0);
			content.MouseFilter = Control.MouseFilterEnum.Pass;
			cardPanel.AddChild(content, forceReadableName: false, Node.InternalMode.Disabled);

			Control portraitContainer = new Control();
			portraitContainer.CustomMinimumSize = new Vector2(CardWidth, PortraitHeight);
			portraitContainer.ClipContents = true;
			portraitContainer.MouseFilter = Control.MouseFilterEnum.Pass;
			content.AddChild(portraitContainer, forceReadableName: false, Node.InternalMode.Disabled);

			if (portrait != null)
			{
				TextureRect portraitNode = new TextureRect();
				portraitNode.Texture = portrait;
				portraitNode.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
				portraitNode.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
				portraitNode.SetAnchorsPreset(Control.LayoutPreset.FullRect);
				portraitNode.MouseFilter = Control.MouseFilterEnum.Ignore;
				portraitContainer.AddChild(portraitNode, forceReadableName: false, Node.InternalMode.Disabled);
			}

			if (!string.IsNullOrEmpty(costText))
			{
				PanelContainer costContainer = new PanelContainer();
				costContainer.MouseFilter = Control.MouseFilterEnum.Ignore;
				StyleBoxFlat costStyle = new StyleBoxFlat();
				costStyle.BgColor = new Color(0f, 0f, 0f, 0.75f);
				costStyle.SetCornerRadiusAll(3);
				costStyle.ContentMarginLeft = 3f;
				costStyle.ContentMarginRight = 3f;
				costStyle.ContentMarginTop = 1f;
				costStyle.ContentMarginBottom = 1f;
				costContainer.AddThemeStyleboxOverride("panel", costStyle);
				costContainer.Position = new Vector2(2f, 1f);
				Label costLabel = new Label();
				costLabel.Text = costText;
				costLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
				costLabel.AddThemeColorOverride("font_color", Colors.White);
				costLabel.AddThemeFontSizeOverride("font_size", ScaledFont(10));
				costContainer.AddChild(costLabel, forceReadableName: false, Node.InternalMode.Disabled);
				portraitContainer.AddChild(costContainer, forceReadableName: false, Node.InternalMode.Disabled);
			}

			PanelContainer footer = new PanelContainer();
			footer.MouseFilter = Control.MouseFilterEnum.Pass;
			StyleBoxFlat footerStyle = new StyleBoxFlat();
			footerStyle.BgColor = new Color(0.05f, 0.05f, 0.08f, 0.95f);
			footerStyle.SetContentMarginAll(0f);
			footerStyle.ContentMarginLeft = 3f;
			footerStyle.ContentMarginRight = 3f;
			footerStyle.ContentMarginTop = 2f;
			footerStyle.ContentMarginBottom = 2f;
			footer.AddThemeStyleboxOverride("panel", footerStyle);

			Label cardNameLabel = new Label();
			cardNameLabel.Text = cardName;
			cardNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
			cardNameLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
			cardNameLabel.AddThemeColorOverride("font_color", typeColor);
			cardNameLabel.AddThemeFontSizeOverride("font_size", ScaledFont(9));
			cardNameLabel.ClipText = true;
			cardNameLabel.CustomMinimumSize = new Vector2(CardWidth - 6, 0f);
			footer.AddChild(cardNameLabel, forceReadableName: false, Node.InternalMode.Disabled);
			content.AddChild(footer, forceReadableName: false, Node.InternalMode.Disabled);
			return cardPanel;
		}

		private static PanelContainer CreateCardFallback(CardModel card)
		{
			string cardName = "???";
			try
			{
				string title = card.Title;
				if (!string.IsNullOrEmpty(title))
				{
					cardName = title;
				}
			}
			catch
			{
			}

			PanelContainer panelContainer = new PanelContainer();
			StyleBoxFlat styleBoxFlat = new StyleBoxFlat();
			styleBoxFlat.BgColor = new Color(0.15f, 0.15f, 0.2f, 0.9f);
			styleBoxFlat.BorderColor = DefaultCardColor;
			styleBoxFlat.SetBorderWidthAll(1);
			styleBoxFlat.SetCornerRadiusAll(4);
			styleBoxFlat.ContentMarginLeft = 4f;
			styleBoxFlat.ContentMarginRight = 4f;
			styleBoxFlat.ContentMarginTop = 2f;
			styleBoxFlat.ContentMarginBottom = 2f;
			panelContainer.AddThemeStyleboxOverride("panel", styleBoxFlat);

			Label label = new Label();
			label.Text = cardName;
			label.AddThemeColorOverride("font_color", DefaultCardColor);
			label.AddThemeFontSizeOverride("font_size", ScaledFont(11));
			panelContainer.AddChild(label, forceReadableName: false, Node.InternalMode.Disabled);
			return panelContainer;
		}

		private void ClearCards()
		{
			foreach (Control cardControl in _cardControls)
			{
				if (GodotObject.IsInstanceValid(cardControl))
				{
					cardControl.QueueFree();
				}
			}
			_cardControls.Clear();
		}

		private void UpdateNameLabel(string? baseName = null, int? count = null, int totalDamage = 0, int totalBlock = 0, int weakStacks = 0, int vulnStacks = 0)
		{
			if (_nameLabel == null || !GodotObject.IsInstanceValid(_nameLabel))
			{
				return;
			}

			int cardCount = count ?? (_hand?.Cards?.Count).GetValueOrDefault();
			if (baseName != null)
			{
				_nameLabel.Text = $"{baseName} — Hand ({cardCount})";
			}
			else
			{
				string currentText = _nameLabel.Text;
				int separatorIndex = currentText.IndexOf('—');
				if (separatorIndex > 0)
				{
					_nameLabel.Text = currentText.Substring(0, separatorIndex) + $"— Hand ({cardCount})";
				}
			}

			if (_statsRow == null || !GodotObject.IsInstanceValid(_statsRow))
			{
				return;
			}

			foreach (Node child in _statsRow.GetChildren())
			{
				child.QueueFree();
			}

			if (totalDamage > 0)
			{
				AddStatEntry(_statsRow, null, $"⚔ {totalDamage}", AttackColor);
			}
			if (totalBlock > 0)
			{
				AddStatEntry(_statsRow, IconDefend, $"{totalBlock}", SkillColor);
			}
			if (weakStacks > 0)
			{
				AddStatEntry(_statsRow, IconWeak, $"{weakStacks}", new Color(0.4f, 0.8f, 0.3f));
			}
			if (vulnStacks > 0)
			{
				AddStatEntry(_statsRow, IconVuln, $"{vulnStacks}", new Color(1f, 0.6f, 0.2f));
			}
		}

		private static void AddStatEntry(HBoxContainer row, Texture2D? icon, string text, Color color)
		{
			HBoxContainer entry = new HBoxContainer();
			entry.AddThemeConstantOverride("separation", 2);
			if (icon != null)
			{
				TextureRect iconNode = new TextureRect();
				iconNode.Texture = icon;
				iconNode.CustomMinimumSize = new Vector2(14f, 14f);
				iconNode.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
				iconNode.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
				iconNode.MouseFilter = Control.MouseFilterEnum.Ignore;
				entry.AddChild(iconNode, forceReadableName: false, Node.InternalMode.Disabled);
			}

			Label label = new Label();
			label.Text = text;
			label.AddThemeColorOverride("font_color", color);
			label.AddThemeFontSizeOverride("font_size", 12);
			label.MouseFilter = Control.MouseFilterEnum.Ignore;
			entry.AddChild(label, forceReadableName: false, Node.InternalMode.Disabled);
			row.AddChild(entry, forceReadableName: false, Node.InternalMode.Disabled);
		}
	}

	private class CardPanel : PanelContainer
	{
		public string CardName = "";

		public string CardDescription = "";

		public void WireUpTooltip()
		{
			base.MouseFilter = MouseFilterEnum.Stop;
			base.MouseEntered += OnMouseEntered;
			base.MouseExited += OnMouseExited;
		}

		private void OnMouseEntered()
		{
			try
			{
				IHoverTip hoverTip = CreateHoverTip(CardName, CardDescription);
				NHoverTipSet hoverTipSet = NHoverTipSet.CreateAndShow(this, hoverTip);
				hoverTipSet.GetParent()?.RemoveChild(hoverTipSet);
				_canvasLayer?.AddChild(hoverTipSet, forceReadableName: false, InternalMode.Disabled);
				hoverTipSet.ZIndex = 500;
				hoverTipSet.GlobalPosition = GetViewport().GetMousePosition() + new Vector2(16f, 0f);
			}
			catch (Exception ex)
			{
				ModLog.Error("CardPanel tooltip show", ex);
			}
		}

		private void OnMouseExited()
		{
			try
			{
				NHoverTipSet.Remove(this);
			}
			catch (Exception ex)
			{
				ModLog.Error("CardPanel tooltip hide", ex);
			}
		}
	}
}