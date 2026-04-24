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

public static class TurnSummaryTracker
{
	// ── Enums ──

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

	private enum TimelineEntryKind
	{
		Generic,
		BlockLost,
		BlockCleared
	}

	private enum Tab
	{
		Stats,
		Log
	}

	// ── Data classes ──

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

	private sealed class TurnTimelineEntry
	{
		public TurnTimelineEntry(long order, int roundNumber, Creature owner, string text, Color color, TimelineEntryKind kind = TimelineEntryKind.Generic, int amount = 0)
		{
			Order = order;
			RoundNumber = roundNumber;
			Owner = owner;
			Text = text;
			Color = color;
			Kind = kind;
			Amount = amount;
		}

		public long Order { get; }
		public int RoundNumber { get; }
		public Creature Owner { get; }
		public string Text { get; }
		public Color Color { get; }
		public TimelineEntryKind Kind { get; }
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
		public int CardsPlayed { get; set; }
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

	// ── Layout constants ──

	private const float DefaultPanelWidth = 380f;
	private const float DefaultPanelHeight = 280f;
	private const float MinPanelWidth = 300f;
	private const float MinPanelHeight = 140f;
	private const float ResizeMargin = 8f;

	// ── Colors ──

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

	private static readonly Color _tabActiveBg = new(0.20f, 0.17f, 0.08f, 0.92f);
	private static readonly Color _tabActiveBorder = new(0.86f, 0.74f, 0.34f);
	private static readonly Color _tabInactiveBg = new(0.08f, 0.09f, 0.12f, 0.85f);
	private static readonly Color _tabInactiveBorder = new(0.24f, 0.26f, 0.32f);

	// ── Timeline storage ──

	private static readonly List<TurnTimelineEntry> _timelineEntries = new();

	// ── UI references ──

	private static CanvasLayer? _canvasLayer;
	private static PanelContainer? _panel;
	private static ScrollContainer? _scrollContainer;
	private static VBoxContainer? _contentContainer;
	private static Label? _titleLabel;
	private static Button? _prevRoundButton;
	private static Button? _scopeButton;
	private static Button? _nextRoundButton;
	private static Button? _collapseButton;
	private static Button? _statsTabButton;
	private static Button? _logTabButton;
	private static Label? _gripLabel;

	// ── State ──

	private static bool _visible;
	private static bool _dragging;
	private static bool _resizing;
	private static ResizeEdge _resizeEdge;
	private static Vector2 _dragOffset;
	private static int _trackedRound;
	private static int _selectedRound;
	private static int _processedHistoryCount;
	private static long _timelineSequence;
	private static HistoryViewMode _viewMode = HistoryViewMode.Round;
	private static Tab _activeTab = Tab.Stats;
	private static CombatManager? _subscribedManager;
	private static CombatHistory? _subscribedHistory;

	// ═══════════════════════════════════════════
	//  Public API
	// ═══════════════════════════════════════════

	public static void OnCombatSetUp()
	{
		try
		{
			CombatManager? manager = CombatManager.Instance;
			CombatState? state = manager?.DebugOnlyGetState();
			ResetCombatTracking();
			_trackedRound = state?.RoundNumber ?? 0;
			_selectedRound = _trackedRound;
			_viewMode = HistoryViewMode.Round;
			_activeTab = Tab.Stats;
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
		AppendTimeline(creature, GetCurrentRoundNumber(), $"Lost {amount} block", _blockLossColor, TimelineEntryKind.BlockLost, amount);
		RefreshIfVisible();
	}

	public static void RecordBlockCleared(Creature creature, int amount)
	{
		if (amount <= 0 || !ShouldTrackBlockChanges(creature)) return;
		AppendTimeline(creature, GetCurrentRoundNumber(), $"Cleared {amount} block", _blockLossColor, TimelineEntryKind.BlockCleared, amount);
		RefreshIfVisible();
	}

	public static void Hide()
	{
		DetachSubscriptions();
		PersistLayout();
		CleanupUi();
		ResetCombatTracking();
		_trackedRound = 0;
		_selectedRound = 0;
		_viewMode = HistoryViewMode.Round;
		_activeTab = Tab.Stats;
		_dragging = false;
		_resizing = false;
		_visible = false;
	}

	// ═══════════════════════════════════════════
	//  Subscriptions
	// ═══════════════════════════════════════════

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
		_processedHistoryCount = 0;
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
		catch (Exception ex) { ModLog.Error("TurnSummaryTracker.OnTurnStarted", ex); }
	}

	private static void OnCombatEnded(CombatRoom _)
	{
		try { Hide(); }
		catch (Exception ex) { ModLog.Error("TurnSummaryTracker.OnCombatEnded", ex); }
	}

	// ═══════════════════════════════════════════
	//  Refresh & Data
	// ═══════════════════════════════════════════

	private static void Refresh()
	{
		try
		{
			CombatManager? manager = CombatManager.Instance;
			CombatState? state = manager?.DebugOnlyGetState();
			if (!ModSettings.ShowTurnSummary || manager == null || state == null) { Hide(); return; }
			if (_trackedRound <= 0) _trackedRound = state.RoundNumber;
			if (_selectedRound <= 0) _selectedRound = _trackedRound;
			EnsureSubscriptions(manager);
			ProcessPendingHistoryEntries(manager.History);
			EnsureUi();
			RenderContent(BuildSummaries(state, manager.History));
		}
		catch (Exception ex) { ModLog.Error("TurnSummaryTracker.Refresh", ex); }
	}

	private static void EnsureUi()
	{
		if (_visible && UiHelpers.IsValid(_panel) && UiHelpers.IsValid(_contentContainer)
			&& UiHelpers.IsValid(_titleLabel) && UiHelpers.IsValid(_collapseButton)
			&& UiHelpers.IsValid(_scrollContainer) && UiHelpers.IsValid(_statsTabButton)
			&& UiHelpers.IsValid(_logTabButton))
			return;
		BuildUi();
	}

	// ═══════════════════════════════════════════
	//  UI Construction
	// ═══════════════════════════════════════════

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

		// ── Title bar ──
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

		// ── Scroll area ──
		_scrollContainer = new ScrollContainer();
		_scrollContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_scrollContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		root.AddChild(_scrollContainer, forceReadableName: false, Node.InternalMode.Disabled);

		_contentContainer = new VBoxContainer();
		_contentContainer.AddThemeConstantOverride("separation", 4);
		_contentContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_scrollContainer.AddChild(_contentContainer, forceReadableName: false, Node.InternalMode.Disabled);

		// ── Grip ──
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

	// ═══════════════════════════════════════════
	//  Rendering
	// ═══════════════════════════════════════════

	private static void RenderContent(List<PlayerTurnSummary> summaries)
	{
		if (!UiHelpers.IsValid(_contentContainer) || !UiHelpers.IsValid(_titleLabel)
			|| !UiHelpers.IsValid(_collapseButton) || !UiHelpers.IsValid(_scrollContainer)
			|| !UiHelpers.IsValid(_prevRoundButton) || !UiHelpers.IsValid(_scopeButton)
			|| !UiHelpers.IsValid(_nextRoundButton) || !UiHelpers.IsValid(_statsTabButton)
			|| !UiHelpers.IsValid(_logTabButton))
			return;

		ClearContainer(_contentContainer!);

		// Title
		_titleLabel!.Text = GetWindowTitle();

		// Tabs
		ApplyTabStyle(_statsTabButton!, _activeTab == Tab.Stats);
		ApplyTabStyle(_logTabButton!, _activeTab == Tab.Log);

		// Nav buttons
		_scopeButton!.Text = _viewMode == HistoryViewMode.Combat ? $"R{GetSelectedRoundNumber()}" : "All";
		_collapseButton!.Text = ModSettings.TurnSummaryCollapsed ? "+" : "\u2212";
		_prevRoundButton!.Disabled = _viewMode == HistoryViewMode.Combat || GetSelectedRoundNumber() <= 1;
		_nextRoundButton!.Disabled = _viewMode == HistoryViewMode.Combat || GetSelectedRoundNumber() >= Math.Max(1, _trackedRound);

		// Scroll sizing
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
			return _trackedRound > 0 ? $"Combat  R1\u2013R{_trackedRound}" : "Combat";
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

		// Player name row
		HBoxContainer nameRow = new();
		nameRow.AddThemeConstantOverride("separation", 6);
		Label nameLabel = UiHelpers.CreateLabel(summary.DisplayName, nameColor, 13);
		nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		nameRow.AddChild(nameLabel, forceReadableName: false, Node.InternalMode.Disabled);
		if (summary.IsLocal)
			nameRow.AddChild(UiHelpers.CreateLabel("YOU", _localPlayerColor, 9), forceReadableName: false, Node.InternalMode.Disabled);
		body.AddChild(nameRow, forceReadableName: false, Node.InternalMode.Disabled);

		// Dispatch to active tab
		if (_activeTab == Tab.Stats)
			RenderStatsContent(body, summary);
		else
			RenderLogContent(body, summary);

		return panel;
	}

	// ── Stats tab ──

	private static void RenderStatsContent(VBoxContainer body, PlayerTurnSummary summary)
	{
		// Stat chips
		HBoxContainer chips = new();
		chips.AddThemeConstantOverride("separation", 3);
		chips.AddChild(CreateStatChip($"Dealt {summary.DamageDealtTotal}", _dealColor, _chipDealBg), forceReadableName: false, Node.InternalMode.Disabled);
		chips.AddChild(CreateStatChip($"Taken {summary.DamageReceivedTotal}", _takeColor, _chipTakeBg), forceReadableName: false, Node.InternalMode.Disabled);
		chips.AddChild(CreateStatChip($"Blk +{summary.BlockGained}/\u2212{summary.TotalBlockLost}", _blockGainColor, _chipBlockBg), forceReadableName: false, Node.InternalMode.Disabled);
		if (summary.BlockBreaks > 0)
			chips.AddChild(CreateStatChip($"Brk {summary.BlockBreaks}", _breakColor, _chipBreakBg), forceReadableName: false, Node.InternalMode.Disabled);
		body.AddChild(chips, forceReadableName: false, Node.InternalMode.Disabled);

		if (ModSettings.TurnSummaryCollapsed) return;

		// Card thumbnails
		if (summary.Cards.Count > 0)
		{
			HFlowContainer cardFlow = new();
			cardFlow.AddThemeConstantOverride("h_separation", 4);
			cardFlow.AddThemeConstantOverride("v_separation", 4);
			foreach (PlayedCardSummary card in summary.Cards)
				cardFlow.AddChild(CreateCardThumbnail(card), forceReadableName: false, Node.InternalMode.Disabled);
			body.AddChild(cardFlow, forceReadableName: false, Node.InternalMode.Disabled);
		}

	}

	// ── Log tab ──

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
			body.AddChild(UiHelpers.CreateLabel($"\u2026 {entries.Count - maxVisible} earlier", _mutedColor, 10), forceReadableName: false, Node.InternalMode.Disabled);

		foreach (TurnTimelineEntry entry in visible)
		{
			string prefix = _viewMode == HistoryViewMode.Combat ? $"R{entry.RoundNumber}  " : "";
			Label label = UiHelpers.CreateLabel($"{prefix}{entry.Text}", entry.Color, 11);
			label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			body.AddChild(label, forceReadableName: false, Node.InternalMode.Disabled);
		}
	}

	// ── Stat chip helper ──

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

		// Portrait
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
		catch { }

		// Footer: name + damage
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
			string typeName = card.Type.ToString() ?? "";
			if (typeName.Contains("Attack")) return _cardAttackBorder;
			if (typeName.Contains("Skill")) return _cardSkillBorder;
			if (typeName.Contains("Power")) return _cardPowerBorder;
		}
		catch { }
		return _cardDefaultBorder;
	}

	// ═══════════════════════════════════════════
	//  Data collection
	// ═══════════════════════════════════════════

	private static List<PlayerTurnSummary> BuildSummaries(CombatState state, CombatHistory history)
	{
		ulong localNetId = GetLocalNetId();
		Dictionary<Creature, PlayerTurnSummary> summaries = state.PlayerCreatures
			.Where(IsTrackedPlayerCreature)
			.ToDictionary(c => c, c => new PlayerTurnSummary(c, GetDisplayName(c.Player, localNetId), c.Player?.NetId == localNetId && localNetId != 0));

		foreach (CombatHistoryEntry entry in history.Entries)
		{
			if (!IsRoundIncluded(entry.RoundNumber)) continue;

			switch (entry)
			{
				case CardPlayFinishedEntry cpf when summaries.TryGetValue(cpf.Actor, out PlayerTurnSummary? cardSummary):
					cardSummary.CardsPlayed++;
					cardSummary.RegisterPlayedCard(cpf.CardPlay.Card);
					break;

				case DamageReceivedEntry dre:
					if (summaries.TryGetValue(dre.Receiver, out PlayerTurnSummary? receivedSummary))
					{
						receivedSummary.DamageReceivedTotal += dre.Result.TotalDamage;
						receivedSummary.DamageReceivedHp += dre.Result.UnblockedDamage;
						receivedSummary.DamageReceivedBlocked += dre.Result.BlockedDamage;
						receivedSummary.BlockSpent += dre.Result.BlockedDamage;
						if (dre.Result.WasBlockBroken)
							receivedSummary.BlockBreaks++;
					}
					if (dre.Dealer != null && summaries.TryGetValue(dre.Dealer, out PlayerTurnSummary? dealtSummary))
					{
						dealtSummary.DamageDealtTotal += dre.Result.TotalDamage;
						dealtSummary.DamageDealtHp += dre.Result.UnblockedDamage;
						dealtSummary.DamageDealtBlocked += dre.Result.BlockedDamage;
						dealtSummary.RegisterCardDamage(dre.CardSource, dre.Result.UnblockedDamage, dre.Result.BlockedDamage);
					}
					break;

				case BlockGainedEntry bge when summaries.TryGetValue(bge.Receiver, out PlayerTurnSummary? blockSummary):
					blockSummary.BlockGained += bge.Amount;
					blockSummary.RegisterBlockGain(bge.CardPlay, bge.Amount, bge.Props);
					break;
			}
		}

		foreach (TurnTimelineEntry entry in _timelineEntries)
		{
			if (!IsRoundIncluded(entry.RoundNumber) || !summaries.TryGetValue(entry.Owner, out PlayerTurnSummary? summary))
				continue;

			switch (entry.Kind)
			{
				case TimelineEntryKind.BlockLost:
					summary.BlockLost += entry.Amount;
					break;
				case TimelineEntryKind.BlockCleared:
					summary.BlockCleared += entry.Amount;
					break;
			}
		}

		return summaries.Values.OrderByDescending(s => s.IsLocal).ThenBy(s => s.DisplayName, StringComparer.Ordinal).ToList();
	}

	private static void ProcessPendingHistoryEntries(CombatHistory history)
	{
		if (_processedHistoryCount < 0) _processedHistoryCount = 0;

		int index = 0;
		foreach (CombatHistoryEntry entry in history.Entries)
		{
			if (index++ < _processedHistoryCount) continue;
			AppendTimelineFromHistory(entry);
		}
		_processedHistoryCount = index;
	}

	private static void AppendTimelineFromHistory(CombatHistoryEntry entry)
	{
		switch (entry)
		{
			case CardPlayFinishedEntry cpf when IsTrackedPlayerCreature(cpf.Actor):
				AppendTimeline(cpf.Actor, entry.RoundNumber, $"Played {GetCardName(cpf.CardPlay.Card)}", _cardsColor);
				break;

			case DamageReceivedEntry dre:
				if (dre.Dealer != null && IsTrackedPlayerCreature(dre.Dealer))
					AppendTimeline(dre.Dealer, entry.RoundNumber, $"Hit {GetCreatureName(dre.Receiver)} for {FormatDamageBreakdown(dre.Result)}", _dealColor);
				if (IsTrackedPlayerCreature(dre.Receiver))
				{
					AppendTimeline(dre.Receiver, entry.RoundNumber, $"Took {FormatDamageBreakdown(dre.Result)} from {GetCreatureName(dre.Dealer)}", _takeColor);
					if (dre.Result.WasBlockBroken)
						AppendTimeline(dre.Receiver, entry.RoundNumber, "Block broken", _breakColor);
				}
				break;

			case BlockGainedEntry bge when IsTrackedPlayerCreature(bge.Receiver):
				AppendTimeline(bge.Receiver, entry.RoundNumber, $"Gained {bge.Amount} block from {DescribeBlockSource(bge.CardPlay, bge.Props)}", _blockGainColor);
				break;
		}
	}

	private static void AppendTimeline(Creature creature, int roundNumber, string text, Color color, TimelineEntryKind kind = TimelineEntryKind.Generic, int amount = 0)
	{
		if (!IsTrackedPlayerCreature(creature)) return;
		_timelineEntries.Add(new TurnTimelineEntry(++_timelineSequence, Math.Max(1, roundNumber), creature, text, color, kind, amount));
	}

	private static List<TurnTimelineEntry> GetTimelineFor(Creature creature)
	{
		return _timelineEntries.Where(e => ReferenceEquals(e.Owner, creature) && IsRoundIncluded(e.RoundNumber)).OrderBy(e => e.Order).ToList();
	}

	// ═══════════════════════════════════════════
	//  Helpers
	// ═══════════════════════════════════════════

	private static ulong GetLocalNetId()
	{
		try { return (RunManager.Instance?.NetService?.NetId).GetValueOrDefault(); }
		catch { return 0uL; }
	}

	private static int GetCurrentRoundNumber()
	{
		return CombatManager.Instance?.DebugOnlyGetState()?.RoundNumber ?? Math.Max(1, _trackedRound);
	}

	private static int GetSelectedRoundNumber()
	{
		int maxRound = Math.Max(1, _trackedRound);
		return _selectedRound <= 0 ? maxRound : Math.Clamp(_selectedRound, 1, maxRound);
	}

	private static bool IsRoundIncluded(int roundNumber)
	{
		if (roundNumber <= 0) return false;
		return _viewMode == HistoryViewMode.Combat || roundNumber == GetSelectedRoundNumber();
	}

	private static string FormatDamageBreakdown(DamageResult result)
	{
		if (result.UnblockedDamage > 0 && result.BlockedDamage > 0)
			return $"{result.TotalDamage} ({result.UnblockedDamage}hp, {result.BlockedDamage}blk)";
		if (result.UnblockedDamage > 0) return $"{result.UnblockedDamage}hp";
		if (result.BlockedDamage > 0) return $"{result.BlockedDamage}blk";
		return "0";
	}

	private static string DescribeBlockSource(CardPlay? cardPlay, ValueProp props)
	{
		CardModel? card = cardPlay?.Card;
		if (card != null)
		{
			string type = card.Type.ToString().ToLowerInvariant();
			return type switch
			{
				"attack" => GetCardName(card) + " (atk)",
				"skill" => GetCardName(card) + " (skill)",
				"power" => GetCardName(card) + " (pwr)",
				_ => GetCardName(card)
			};
		}
		if (props.HasFlag(ValueProp.Unpowered)) return "Other";
		return "Power / Relic";
	}

	private static string GetDisplayName(Player? player, ulong localNetId)
	{
		if (player == null) return "Player";
		string playerName = PlatformUtil.GetPlayerName(PlatformType.Steam, player.NetId);
		string? characterName = player.Character?.Title?.GetFormattedText();
		string displayName = !string.IsNullOrEmpty(playerName) ? playerName : (characterName ?? "Player");
		if (!string.IsNullOrEmpty(playerName) && !string.IsNullOrEmpty(characterName))
			displayName = $"{playerName} ({characterName})";
		if (localNetId != 0 && player.NetId == localNetId)
			displayName += " (You)";
		return displayName;
	}

	private static string GetCreatureName(Creature? creature)
	{
		if (creature == null) return "Unknown";
		if (creature.IsPlayer) return GetDisplayName(creature.Player, GetLocalNetId());
		return creature.Monster?.Id.Entry ?? "Enemy";
	}

	private static string GetCardName(CardModel? card)
	{
		if (card == null) return "Unknown";
		string? title = card.Title;
		return !string.IsNullOrWhiteSpace(title) ? title : card.Id.Entry;
	}

	private static void ResetCombatTracking()
	{
		_timelineEntries.Clear();
		_processedHistoryCount = 0;
		_timelineSequence = 0;
	}

	private static bool IsTrackedPlayerCreature(Creature creature)
	{
		return creature.IsPlayer && !creature.IsPet && creature.Player != null;
	}

	// ═══════════════════════════════════════════
	//  Mouse & Resize
	// ═══════════════════════════════════════════

	private static void HandleMouseButton(InputEventMouseButton inputEvent)
	{
		Vector2 mousePosition = inputEvent.Position;
		Rect2 panelRect = _panel!.GetGlobalRect();

		if (inputEvent.Pressed)
		{
			if (!panelRect.HasPoint(mousePosition)) return;
			if (IsPointInInteractiveControl(mousePosition)) return;

			ResizeEdge resizeEdge = GetResizeEdge(mousePosition, panelRect);
			if (resizeEdge != ResizeEdge.None)
			{
				_resizing = true;
				_resizeEdge = resizeEdge;
				return;
			}

			_dragging = true;
			_dragOffset = mousePosition - _panel.Position;
			return;
		}

		if (_dragging || _resizing) PersistLayout();
		_dragging = false;
		_resizing = false;
	}

	private static bool IsPointInInteractiveControl(Vector2 point)
	{
		return IsPointInControl(_collapseButton, point)
			|| IsPointInControl(_prevRoundButton, point)
			|| IsPointInControl(_scopeButton, point)
			|| IsPointInControl(_nextRoundButton, point)
			|| IsPointInControl(_statsTabButton, point)
			|| IsPointInControl(_logTabButton, point);
	}

	private static bool IsPointInControl(Control? control, Vector2 point)
	{
		return UiHelpers.IsValid(control) && control!.GetGlobalRect().HasPoint(point);
	}

	private static ResizeEdge GetResizeEdge(Vector2 mousePos, Rect2 rect)
	{
		ResizeEdge edge = ResizeEdge.None;
		if (mousePos.X >= rect.End.X - ResizeMargin) edge |= ResizeEdge.Right;
		if (mousePos.Y >= rect.End.Y - ResizeMargin) edge |= ResizeEdge.Bottom;
		return edge;
	}

	private static void ResizePanel(Vector2 mousePosition)
	{
		Vector2 position = _panel!.Position;
		Vector2 size = _panel.Size;
		if (_resizeEdge.HasFlag(ResizeEdge.Right))
			size.X = Math.Max(MinPanelWidth, mousePosition.X - position.X);
		if (_resizeEdge.HasFlag(ResizeEdge.Bottom))
			size.Y = Math.Max(MinPanelHeight, mousePosition.Y - position.Y);
		size = ClampPanelSize(size);
		_panel.CustomMinimumSize = new Vector2(MinPanelWidth, MinPanelHeight);
		_panel.Size = size;
		UpdateGripPosition();
		Refresh();
	}

	private static void UpdateResizeCursor(Vector2 mousePos, Rect2 rect)
	{
		if (!rect.HasPoint(mousePos))
		{
			_panel!.MouseDefaultCursorShape = Control.CursorShape.Arrow;
			return;
		}
		ResizeEdge edge = GetResizeEdge(mousePos, rect);
		_panel!.MouseDefaultCursorShape = edge switch
		{
			ResizeEdge.Right | ResizeEdge.Bottom => Control.CursorShape.Fdiagsize,
			ResizeEdge.Right => Control.CursorShape.Hsize,
			ResizeEdge.Bottom => Control.CursorShape.Vsize,
			_ => Control.CursorShape.Arrow
		};
	}

	private static void UpdateGripPosition()
	{
		if (!UiHelpers.IsValid(_gripLabel) || !UiHelpers.IsValid(_panel)) return;
		Vector2 pos = _panel!.Position;
		Vector2 sz = _panel.Size;
		_gripLabel!.Position = new Vector2(pos.X + sz.X - 16f, pos.Y + sz.Y - 18f);
	}

	// ═══════════════════════════════════════════
	//  Actions
	// ═══════════════════════════════════════════

	private static void SwitchToStats()
	{
		if (_activeTab == Tab.Stats) return;
		_activeTab = Tab.Stats;
		Refresh();
	}

	private static void SwitchToLog()
	{
		if (_activeTab == Tab.Log) return;
		_activeTab = Tab.Log;
		Refresh();
	}

	private static void PrevRound()
	{
		if (_viewMode == HistoryViewMode.Combat) return;
		_selectedRound = Math.Max(1, GetSelectedRoundNumber() - 1);
		Refresh();
	}

	private static void NextRound()
	{
		if (_viewMode == HistoryViewMode.Combat) return;
		_selectedRound = Math.Min(Math.Max(1, _trackedRound), GetSelectedRoundNumber() + 1);
		Refresh();
	}

	private static void ToggleViewMode()
	{
		_viewMode = _viewMode == HistoryViewMode.Combat ? HistoryViewMode.Round : HistoryViewMode.Combat;
		if (_viewMode == HistoryViewMode.Round && _selectedRound <= 0)
			_selectedRound = Math.Max(1, _trackedRound);
		Refresh();
	}

	private static void ToggleCollapsed()
	{
		ModSettings.TurnSummaryCollapsed = !ModSettings.TurnSummaryCollapsed;
		ModSettings.Save();
		Refresh();
	}

	private static void PersistLayout()
	{
		if (!UiHelpers.IsValid(_panel)) return;
		ModSettings.TurnSummaryPosition = _panel!.Position;
		ModSettings.TurnSummarySize = _panel.Size;
		ModSettings.Save();
	}

	// ═══════════════════════════════════════════
	//  Cleanup
	// ═══════════════════════════════════════════

	private static void CleanupUi()
	{
		if (UiHelpers.IsValid(_canvasLayer))
			_canvasLayer!.QueueFree();

		_canvasLayer = null;
		_panel = null;
		_scrollContainer = null;
		_contentContainer = null;
		_titleLabel = null;
		_prevRoundButton = null;
		_scopeButton = null;
		_nextRoundButton = null;
		_collapseButton = null;
		_statsTabButton = null;
		_logTabButton = null;
		_gripLabel = null;
	}

	private static void ClearContainer(Container container)
	{
		foreach (Node child in container.GetChildren())
			child.QueueFree();
	}
}
