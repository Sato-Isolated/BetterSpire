#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace BetterSpire2.Trackers;

public static partial class TurnSummaryTracker
{
	[Flags]
	private enum ResizeEdge
	{
		None = 0,
		Right = 1,
		Bottom = 2
	}

	private enum HistoryViewMode
	{
		Round,
		Combat
	}

	private enum SupplementalStatKind
	{
		BlockLost,
		BlockCleared,
		EnergyGained
	}

	private sealed class PlayedCardSummary
	{
		public PlayedCardSummary(string name, CardModel card) { Name = name; Card = card; }
		public string Name { get; set; }
		public CardModel Card { get; }
		public int HpDamage { get; set; }
		public int BlockedDamage { get; set; }
		public int TotalDamage => HpDamage + BlockedDamage;
		public int BlockGained { get; set; }
	}

	private sealed class SupplementalStatEntry
	{
		public SupplementalStatEntry(int roundNumber, Creature owner, SupplementalStatKind kind, int amount)
		{
			RoundNumber = roundNumber;
			Owner = owner;
			Kind = kind;
			Amount = amount;
		}

		public int RoundNumber { get; }
		public Creature Owner { get; }
		public SupplementalStatKind Kind { get; }
		public int Amount { get; }
	}

	private sealed class PlayerTurnSummary
	{
		private readonly Dictionary<CardModel, PlayedCardSummary> _cardsByModel = new();
		private readonly Dictionary<string, int> _blockSources = new(StringComparer.Ordinal);

		public PlayerTurnSummary(Creature creature, string displayName, bool isLocal)
		{
			Creature = creature;
			DisplayName = displayName;
			IsLocal = isLocal;
		}

		public Creature Creature { get; }
		public string DisplayName { get; }
		public bool IsLocal { get; }
		public int CardsDrawn { get; set; }
		public int CardsPlayed { get; set; }
		public int CardsDiscarded { get; set; }
		public int CardsGenerated { get; set; }
		public int CardsExhausted { get; set; }
		public int EnergySpent { get; set; }
		public int EnergyGained { get; set; }
		public int DamageDealtTotal { get; set; }
		public int DamageDealtHp { get; set; }
		public int DamageDealtBlocked { get; set; }
		public int DamageReceivedTotal { get; set; }
		public int DamageReceivedHp { get; set; }
		public int DamageReceivedBlocked { get; set; }
		public int BlockGained { get; set; }
		public int BlockSpent { get; set; }
		public int BlockLost { get; set; }
		public int BlockCleared { get; set; }
		public int BlockBreaks { get; set; }
		public List<PlayedCardSummary> Cards { get; } = new();
		public IEnumerable<KeyValuePair<string, int>> BlockSources => _blockSources.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal);
		public int EnergyNet => EnergyGained - EnergySpent;
		public int TotalBlockLost => BlockSpent + BlockLost + BlockCleared;

		public void RegisterPlayedCard(CardModel card)
		{
			PlayedCardSummary summary = GetOrCreatePlayedCard(card);
			summary.Name = GetCardName(card);
		}

		public void RegisterCardDamage(CardModel? card, int hpDamage, int blockedDamage)
		{
			if (card == null) return;
			PlayedCardSummary summary = GetOrCreatePlayedCard(card);
			summary.Name = GetCardName(card);
			summary.HpDamage += hpDamage;
			summary.BlockedDamage += blockedDamage;
		}

		public void RegisterBlockGain(CardPlay? cardPlay, int amount, ValueProp props)
		{
			string source = DescribeBlockSource(cardPlay, props);
			if (_blockSources.TryGetValue(source, out int current))
			{
				_blockSources[source] = current + amount;
			}
			else
			{
				_blockSources[source] = amount;
			}

			CardModel? card = cardPlay?.Card;
			if (card != null)
			{
				PlayedCardSummary summary = GetOrCreatePlayedCard(card);
				summary.Name = GetCardName(card);
				summary.BlockGained += amount;
			}
		}

		private PlayedCardSummary GetOrCreatePlayedCard(CardModel card)
		{
			if (_cardsByModel.TryGetValue(card, out PlayedCardSummary? summary))
				return summary;
			summary = new PlayedCardSummary(GetCardName(card), card);
			_cardsByModel[card] = summary;
			Cards.Add(summary);
			return summary;
		}
	}

	private const float DefaultPanelWidth = 380f;
	private const float DefaultPanelHeight = 280f;
	private const float MinPanelWidth = 300f;
	private const float MinPanelHeight = 140f;
	private const float ResizeMargin = 8f;

	private static readonly Color _panelBg = new(0.05f, 0.06f, 0.09f, 0.94f);
	private static readonly Color _panelBorder = new(0.50f, 0.40f, 0.16f);
	private static readonly Color _titleColor = new(0.86f, 0.74f, 0.34f);
	private static readonly Color _localPlayerColor = new(0.48f, 0.80f, 0.52f);
	private static readonly Color _allyColor = new(0.82f, 0.86f, 0.90f);
	private static readonly Color _mutedColor = new(0.50f, 0.54f, 0.60f);
	private static readonly Color _sectionBorder = new(0.18f, 0.22f, 0.30f, 0.85f);
	private static readonly Color _sectionBg = new(0.07f, 0.08f, 0.12f, 0.94f);
	private static readonly Color _cardsColor = new(0.92f, 0.80f, 0.36f);
	private static readonly Color _dealColor = new(0.94f, 0.60f, 0.26f);
	private static readonly Color _takeColor = new(0.96f, 0.40f, 0.36f);
	private static readonly Color _blockGainColor = new(0.40f, 0.78f, 0.92f);
	private static readonly Color _blockLossColor = new(0.70f, 0.74f, 0.82f);
	private static readonly Color _breakColor = new(0.96f, 0.66f, 0.34f);

	private static readonly Color _chipCardsBg = new(0.20f, 0.17f, 0.06f, 0.88f);
	private static readonly Color _chipDealBg = new(0.22f, 0.14f, 0.05f, 0.88f);
	private static readonly Color _chipTakeBg = new(0.22f, 0.09f, 0.07f, 0.88f);
	private static readonly Color _chipBlockBg = new(0.07f, 0.16f, 0.22f, 0.88f);
	private static readonly Color _chipBreakBg = new(0.22f, 0.14f, 0.05f, 0.88f);

	private static readonly Color _cardAttackBorder = new(0.9f, 0.35f, 0.3f);
	private static readonly Color _cardSkillBorder = new(0.3f, 0.55f, 0.9f);
	private static readonly Color _cardPowerBorder = new(0.9f, 0.75f, 0.2f);
	private static readonly Color _cardDefaultBorder = new(0.55f, 0.55f, 0.55f);
	private const float CardThumbWidth = 52f;
	private const float CardThumbPortraitHeight = 38f;

	private static readonly List<SupplementalStatEntry> _supplementalEntries = new();
	private static readonly HashSet<Creature> _expandedCardSections = new();
	private static readonly List<Control> _interactiveControls = new();

	private static CanvasLayer? _canvasLayer;
	private static PanelContainer? _panel;
	private static ScrollContainer? _scrollContainer;
	private static VBoxContainer? _contentContainer;
	private static Label? _titleLabel;
	private static Button? _prevRoundButton;
	private static Button? _scopeButton;
	private static Button? _nextRoundButton;
	private static Button? _collapseButton;
	private static Label? _gripLabel;

	private static bool _visible;
	private static bool _dragging;
	private static bool _resizing;
	private static ResizeEdge _resizeEdge;
	private static Vector2 _dragOffset;
	private static int _trackedRound;
	private static int _selectedRound;
	private static HistoryViewMode _viewMode = HistoryViewMode.Combat;
	private static CombatManager? _subscribedManager;
	private static CombatHistory? _subscribedHistory;

	public static void OnCombatSetUp()
	{
		try
		{
			CombatManager? manager = CombatManager.Instance;
			CombatState? state = manager?.DebugOnlyGetState();
			ResetCombatTracking();
			_trackedRound = state?.RoundNumber ?? 0;
			_selectedRound = _trackedRound;
			_viewMode = HistoryViewMode.Combat;
			SyncVisibility();
		}
		catch (Exception ex)
		{
			ModLog.Error("TurnSummaryTracker.OnCombatSetUp", ex);
		}
	}

	public static void SyncVisibility()
	{
		try
		{
			CombatManager? manager = CombatManager.Instance;
			CombatState? state = manager?.DebugOnlyGetState();
			if (!ModSettings.ShowTurnSummary || manager == null || state == null)
			{
				Hide();
				return;
			}
			EnsureSubscriptions(manager);
			Refresh();
		}
		catch (Exception ex)
		{
			ModLog.Error("TurnSummaryTracker.SyncVisibility", ex);
		}
	}

	public static void RefreshIfVisible()
	{
		if (_visible) Refresh();
	}

	public static void HandleMouseInput(InputEvent @event)
	{
		if (!_visible || !UiHelpers.IsValid(_panel)) return;

		UpdateGripPosition();

		if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
		{
			HandleMouseButton(mb);
			return;
		}

		if (@event is not InputEventMouseMotion mm) return;

		if (_resizing) { ResizePanel(mm.Position); return; }
		if (_dragging)
		{
			_panel!.Position = mm.Position - _dragOffset;
			UpdateGripPosition();
			return;
		}

		UpdateResizeCursor(mm.Position, _panel!.GetGlobalRect());
	}

	public static bool ShouldTrackBlockChanges(Creature? creature)
	{
		return ShouldTrackSupplementalStat(creature);
	}

	public static void RecordEnergyGained(Creature creature, int amount)
	{
		if (amount <= 0 || !ShouldTrackSupplementalStat(creature)) return;
		AppendSupplementalStat(creature, GetCurrentRoundNumber(), SupplementalStatKind.EnergyGained, amount);
		RefreshIfVisible();
	}

	private static bool ShouldTrackSupplementalStat(Creature? creature)
	{
		if (!ModSettings.ShowTurnSummary || creature == null) return false;
		return CombatManager.Instance?.DebugOnlyGetState() != null && IsTrackedPlayerCreature(creature);
	}

	public static void RecordBlockSpent(Creature creature, int amount)
	{
		if (amount <= 0 || !ShouldTrackBlockChanges(creature)) return;
		RefreshIfVisible();
	}

	public static void RecordBlockLost(Creature creature, int amount)
	{
		if (amount <= 0 || !ShouldTrackBlockChanges(creature)) return;
		AppendSupplementalStat(creature, GetCurrentRoundNumber(), SupplementalStatKind.BlockLost, amount);
		RefreshIfVisible();
	}

	public static void RecordBlockCleared(Creature creature, int amount)
	{
		if (amount <= 0 || !ShouldTrackBlockChanges(creature)) return;
		AppendSupplementalStat(creature, GetCurrentRoundNumber(), SupplementalStatKind.BlockCleared, amount);
		RefreshIfVisible();
	}

	private static bool IsCardSectionExpanded(Creature creature)
	{
		return _expandedCardSections.Contains(creature);
	}

	private static void ToggleCardSection(Creature creature)
	{
		if (!_expandedCardSections.Add(creature))
		{
			_expandedCardSections.Remove(creature);
		}

		Refresh();
	}

	private static void RegisterInteractiveControl(Control control)
	{
		if (!_interactiveControls.Contains(control))
		{
			_interactiveControls.Add(control);
		}
	}

	private static void PruneInteractiveControls()
	{
		for (int index = _interactiveControls.Count - 1; index >= 0; index--)
		{
			if (!UiHelpers.IsValid(_interactiveControls[index]))
			{
				_interactiveControls.RemoveAt(index);
			}
		}
	}

	public static void Hide()
	{
		DetachSubscriptions();
		PersistLayout();
		CleanupUi();
		ResetCombatTracking();
		_trackedRound = 0;
		_selectedRound = 0;
		_viewMode = HistoryViewMode.Combat;
		_dragging = false;
		_resizing = false;
		_visible = false;
	}

	private static void EnsureSubscriptions(CombatManager manager)
	{
		if (ReferenceEquals(_subscribedManager, manager)) return;

		DetachSubscriptions();
		_subscribedManager = manager;
		_subscribedHistory = manager.History;
		manager.TurnStarted -= OnTurnStarted;
		manager.CombatEnded -= OnCombatEnded;
		manager.TurnStarted += OnTurnStarted;
		manager.CombatEnded += OnCombatEnded;
		if (_subscribedHistory != null)
		{
			_subscribedHistory.Changed -= OnHistoryChanged;
			_subscribedHistory.Changed += OnHistoryChanged;
		}
	}

	private static void DetachSubscriptions()
	{
		if (_subscribedHistory != null)
		{
			_subscribedHistory.Changed -= OnHistoryChanged;
			_subscribedHistory = null;
		}
		if (_subscribedManager != null)
		{
			_subscribedManager.TurnStarted -= OnTurnStarted;
			_subscribedManager.CombatEnded -= OnCombatEnded;
			_subscribedManager = null;
		}
	}

	private static void OnHistoryChanged() => Refresh();

	private static void OnTurnStarted(CombatState state)
	{
		try
		{
			CombatManager? manager = CombatManager.Instance;
			if (manager != null && !manager.IsEnemyTurnStarted)
			{
				int previousRound = _trackedRound;
				_trackedRound = state.RoundNumber;
				if (_selectedRound <= 0 || _selectedRound == previousRound)
					_selectedRound = _trackedRound;
			}
			Refresh();
		}
		catch (Exception ex)
		{
			ModLog.Error("TurnSummaryTracker.OnTurnStarted", ex);
		}
	}

	private static void OnCombatEnded(CombatRoom _)
	{
		try
		{
			Hide();
		}
		catch (Exception ex)
		{
			ModLog.Error("TurnSummaryTracker.OnCombatEnded", ex);
		}
	}

	private static void Refresh()
	{
		try
		{
			CombatManager? manager = CombatManager.Instance;
			CombatState? state = manager?.DebugOnlyGetState();
			if (!ModSettings.ShowTurnSummary || manager == null || state == null)
			{
				Hide();
				return;
			}
			if (_trackedRound <= 0) _trackedRound = state.RoundNumber;
			if (_selectedRound <= 0) _selectedRound = _trackedRound;
			EnsureSubscriptions(manager);
			EnsureUi();
			RenderContent(BuildSummaries(state, manager.History));
		}
		catch (Exception ex)
		{
			ModLog.Error("TurnSummaryTracker.Refresh", ex);
		}
	}

	private static void EnsureUi()
	{
		if (_visible && UiHelpers.IsValid(_panel) && UiHelpers.IsValid(_contentContainer)
			&& UiHelpers.IsValid(_titleLabel) && UiHelpers.IsValid(_collapseButton)
			&& UiHelpers.IsValid(_scrollContainer))
		{
			return;
		}
		BuildUi();
	}
}