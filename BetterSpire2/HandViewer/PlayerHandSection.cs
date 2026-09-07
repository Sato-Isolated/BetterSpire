#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace BetterSpire2.HandViewer;

public static partial class TeammateHandViewer
{
    private sealed partial class PlayerHandSection
    {
        private readonly Player _player;
        private readonly ulong? _localNetId;
        private CardPile? _hand;
        private Creature? _creature;
        private PlayerCombatState? _combat;
        private Label? _nameLabel, _empty;
        private HFlowContainer? _statsRow, _potionRow;
        private Container? _cardRow;
        private readonly Dictionary<CardModel, CardView> _cardViews = new();
        private readonly List<CardModel> _removedViews = new();
        private readonly HashSet<CardModel> _subscribedCards = new();
        private int _viewGeneration;
        private bool _cardsDirty = true, _potionsDirty = true, _statsDirty = true, _disposed;
        private ulong _lastError, _retryAfter, _nextSafetyRefresh;
        private string _displayName = "";
        private (int Hp, int Max, int Block, int Energy, int Stars, int Count, bool Available)? _lastStats;

        private static int CardWidth => CompactHudGeometry.ScaledCardWidth(ModSettings.CardScalePercent);
        private static int PortraitHeight => CompactHudGeometry.ScaledPortraitHeight(ModSettings.CardScalePercent);
        private int _lastCardCount = -1;
        private static readonly Color AttackColor = HudTheme.Danger;
        private static readonly Color SkillColor = HudTheme.Info;
        private static readonly Color PowerColor = HudTheme.Accent;
        private static readonly Color StatusColor = HudTheme.Muted;
        private static readonly Color CurseColor = new(.75f, .48f, .84f);
        private static readonly Color DefaultCardColor = HudTheme.Text;

        internal PlayerHandSection(Player player, ulong? localNetId)
        { _player = player; _localNetId = localNetId; }

        internal void AddTo(VBoxContainer parent)
        {
            _displayName = ReadDisplayName();
            _nameLabel = _title; // Reuse the only header; no repeated name/character/status rows.
            if (ModSettings.CompactHandViewer)
            {
                _statsRow = Flow(); parent.AddChild(_statsRow);
                _potionRow = Flow(); parent.AddChild(_potionRow);
            }
            _empty = UiHelpers.CreateLabel("", HudTheme.Muted, 11);
            _empty.MouseFilter = Control.MouseFilterEnum.Pass;
            HudTheme.Outline(_empty);
            parent.AddChild(_empty);
            if (!ModSettings.CompactHandViewer)
            {
                _cardRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
                _cardRow.AddThemeConstantOverride("separation", CompactHudGeometry.CardGap);
                parent.AddChild(_cardRow);
            }
            _player.PotionProcured += OnPotionChanged;
            _player.PotionDiscarded += OnPotionChanged;
            _player.UsedPotionRemoved += OnPotionChanged;
            RefreshHandReference();
            FlushPendingChanges();
        }
        private string ReadDisplayName()
        {
            string character = _player.Character?.Title?.GetFormattedText() ?? ModText.T("Player");
            string nickname = PlayerDisplayNames.Nickname(_player);
            string name = nickname.Length == 0 ? character : nickname + " · " + character;
            return name + (_localNetId == _player.NetId ? " · " + ModText.T("You") : "");
        }
        private void RefreshDisplayName()
        {
            string name = ReadDisplayName();
            if (_displayName == name) return;
            _displayName = name;
            _nameLabel!.TooltipText = name;
            _lastStats = null; _statsDirty = true;
        }
        private static HFlowContainer Flow()
        {
            var flow = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
            flow.AddThemeConstantOverride("h_separation", 6);
            flow.AddThemeConstantOverride("v_separation", 3);
            return flow;
        }
        internal void RefreshHandReference()
        {
            var combat = _player.PlayerCombatState;
            if (!ReferenceEquals(combat, _combat) || !ReferenceEquals(_creature, _player.Creature))
            {
                _lastStats = null;
                UnbindStatus(); _combat = combat; _creature = _player.Creature; BindStatus();
            }
            var hand = combat?.Hand;
            if (!ReferenceEquals(hand, _hand))
            {
                UnsubscribeHand(); UnsubscribeCardUpgrades();
                _hand = hand; SubscribeHand();
            }
            _cardsDirty = _statsDirty = true;
        }
        internal void FlushPendingChanges()
        {
            if (_disposed) return;
            ulong now = Time.GetTicksMsec();
            if (now < _retryAfter) return;
            if (now >= _nextSafetyRefresh)
            {
                _nextSafetyRefresh = now + 2000;
                RefreshDisplayName();
                RefreshHandReference(); // Fallback for state replacement and unobserved cost modifiers.
            }
            if (!_cardsDirty && !_potionsDirty && !_statsDirty) return;
            try
            {
                using var measurement = PerformanceProbe.Measure(ProbeSection.HandViewer);
                if (_potionsDirty) { RefreshPotions(); _potionsDirty = false; }
                if (_cardsDirty) { RefreshCards(); _cardsDirty = false; }
                if (_statsDirty) { RefreshStatus(); _statsDirty = false; }
                _retryAfter = 0;
            }
            catch (Exception ex)
            {
                LogError(ex);
                _cardsDirty = _potionsDirty = _statsDirty = true;
                _retryAfter = now + 1000;
            }
        }
        private void LogError(Exception ex)
        {
            ulong now = Time.GetTicksMsec();
            if (_lastError != 0 && now - _lastError < 10000) return;
            _lastError = now; ModLog.Error("HandViewer.Refresh", ex);
        }
        private void OnHandChanged() => _cardsDirty = _statsDirty = true;
        private void OnCardAddedOrRemoved(CardModel _) => OnHandChanged();
        private void OnPotionChanged(PotionModel _) => _potionsDirty = true;
        private void OnPowerChanged(PowerModel _) => OnHandChanged();
        private void OnPowerIncreased(PowerModel _, int amount, bool silent) => OnHandChanged();
        private void OnPowerDecreased(PowerModel _, bool silent) => OnHandChanged();
        private void OnValuesChanged(int previous, int current) { if (previous != current) OnHandChanged(); }
        private void OnCardChanged() => _cardsDirty = true;
        private void SubscribeHand()
        {
            if (_hand == null) return;
            _hand.ContentsChanged += OnHandChanged;
            _hand.CardAdded += OnCardAddedOrRemoved;
            _hand.CardRemoved += OnCardAddedOrRemoved;
        }
        private void UnsubscribeHand()
        {
            if (_hand == null) return;
            _hand.ContentsChanged -= OnHandChanged;
            _hand.CardAdded -= OnCardAddedOrRemoved;
            _hand.CardRemoved -= OnCardAddedOrRemoved;
        }
        private void BindStatus()
        {
            if (_creature != null)
            {
                _creature.CurrentHpChanged += OnValuesChanged;
                _creature.MaxHpChanged += OnValuesChanged;
                _creature.BlockChanged += OnValuesChanged;
                _creature.PowerApplied += OnPowerChanged;
                _creature.PowerRemoved += OnPowerChanged;
                _creature.PowerIncreased += OnPowerIncreased;
                _creature.PowerDecreased += OnPowerDecreased;
            }
            if (_combat != null)
            {
                _combat.EnergyChanged += OnValuesChanged;
                _combat.StarsChanged += OnValuesChanged;
                _combat.PlayerTurnPhaseChanged += OnHandChanged;
            }
        }
        private void UnbindStatus()
        {
            if (_creature != null)
            {
                _creature.CurrentHpChanged -= OnValuesChanged;
                _creature.MaxHpChanged -= OnValuesChanged;
                _creature.BlockChanged -= OnValuesChanged;
                _creature.PowerApplied -= OnPowerChanged;
                _creature.PowerRemoved -= OnPowerChanged;
                _creature.PowerIncreased -= OnPowerIncreased;
                _creature.PowerDecreased -= OnPowerDecreased;
            }
            if (_combat != null)
            {
                _combat.EnergyChanged -= OnValuesChanged;
                _combat.StarsChanged -= OnValuesChanged;
                _combat.PlayerTurnPhaseChanged -= OnHandChanged;
            }
        }
        private void ObserveCard(CardModel card, bool observe)
        {
            if (observe)
            {
                card.Upgraded += OnCardChanged; card.EnergyCostChanged += OnCardChanged;
                card.StarCostChanged += OnCardChanged; card.EnchantmentChanged += OnCardChanged;
                card.AfflictionChanged += OnCardChanged; card.KeywordsChanged += OnCardChanged;
            }
            else
            {
                card.Upgraded -= OnCardChanged; card.EnergyCostChanged -= OnCardChanged;
                card.StarCostChanged -= OnCardChanged; card.EnchantmentChanged -= OnCardChanged;
                card.AfflictionChanged -= OnCardChanged; card.KeywordsChanged -= OnCardChanged;
            }
        }
        private void SyncCardSubscriptions(IReadOnlyList<CardModel> cards)
        {
            var current = new HashSet<CardModel>(cards);
            _subscribedCards.RemoveWhere(card => { if (current.Contains(card)) return false; ObserveCard(card, false); return true; });
            foreach (var card in current) if (_subscribedCards.Add(card)) ObserveCard(card, true);
        }
        private void UnsubscribeCardUpgrades()
        {
            foreach (var card in _subscribedCards) ObserveCard(card, false);
            _subscribedCards.Clear();
        }
        private void RefreshCards()
        {
            _statsDirty = true;
            bool available = _hand != null;
            var cards = _hand?.Cards ?? (IReadOnlyList<CardModel>)Array.Empty<CardModel>();
            _empty!.Text = ModText.T(available ? "Hand is empty." : "Synchronizing hand…");
            _empty.TooltipText = ModText.T(available ? "Hand is empty." : "Hand unavailable. Waiting for combat synchronization.");
            _empty.Visible = !available || cards.Count == 0;
            if (_lastCardCount != cards.Count)
            {
                _lastCardCount = cards.Count;
                _autoFit?.Request();
            }
            if (ModSettings.CompactHandViewer || _cardRow == null)
            { UnsubscribeCardUpgrades(); return; }
            SyncCardSubscriptions(cards);
            foreach (var card in cards) TryUpdateDynamicVarPreview(card);
            SyncCards(cards);
        }
        private void TryUpdateDynamicVarPreview(CardModel card)
        {
            try { card.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, card.DynamicVars); }
            catch (Exception ex) { LogError(ex); }
        }
        private void RefreshStatus()
        {
            var stats = (_creature?.CurrentHp ?? 0, _creature?.MaxHp ?? 0, _creature?.Block ?? 0,
                _combat?.Energy ?? 0, _combat?.Stars ?? 0, _hand?.Cards.Count ?? 0, _hand != null);
            if (_lastStats == stats) return;
            _lastStats = stats;
            string nickname = PlayerDisplayNames.Nickname(_player);
            if (nickname.Length == 0) nickname = _player.Character?.Title?.GetFormattedText() ?? ModText.T("Player");
            _nameLabel!.Text = nickname + (stats.Item7 ? $" ({stats.Item6})" : " (—)");
            _nameLabel.TooltipText = _displayName + (_players.Count > 1 ? $" · {_currentPage + 1}/{_players.Count}" : "")
                + "\n" + ModText.T("HP") + (_creature == null ? " —" : $" {stats.Item1}/{stats.Item2}")
                + " · " + ModText.T("Block") + (_creature == null ? " —" : $" {stats.Item3}")
                + " · " + ModText.T("Energy") + (_combat == null ? " —" : $" {stats.Item4}")
                + (stats.Item5 > 0 ? " · " + ModText.T("Stars") + $" {stats.Item5}" : "")
                + "\n" + ModText.T("Drag name to move · Page Up / Page Down: teammate · F3: close");
            if (_statsRow == null) return;
            foreach (var child in _statsRow!.GetChildren()) { _statsRow.RemoveChild(child); child.QueueFree(); }
            AddStat(ModText.T("HP") + (_creature == null ? " —" : $" {stats.Item1}/{stats.Item2}"),
                _creature == null ? HudTheme.Muted : stats.Item1 <= 0 ? HudTheme.Danger : HudTheme.Success);
            AddStat(ModText.T("Block") + (_creature == null ? " —" : $" {stats.Item3}"), HudTheme.Info);
            AddStat(ModText.T("Energy") + " " + (_combat == null ? "—" : stats.Item4.ToString()), HudTheme.Accent);
            if (stats.Item5 > 0) AddStat(ModText.T("Stars") + $" {stats.Item5}", HudTheme.Accent);
        }
        private void AddStat(string text, Color color)
        {
            var label = UiHelpers.CreateLabel(text, color, 11);
            HudTheme.Outline(label);
            _statsRow!.AddChild(label);
        }
        private void RefreshPotions()
        {
            if (_potionRow == null) return; // Cards mode renders nothing except the icons and name.
            foreach (var child in _potionRow.GetChildren()) { _potionRow.RemoveChild(child); child.QueueFree(); }
            foreach (var potion in _player.Potions)
            {
                if (potion == null) continue;
                var icon = new TextureRect
                {
                    Texture = potion.Image, TooltipText = (potion.Title?.GetFormattedText() ?? "") + "\n\n" +
                        CleanDescriptionText(potion.DynamicDescription?.GetFormattedText() ?? ""),
                    CustomMinimumSize = new Vector2(18, 18), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Stop
                };
                _potionRow.AddChild(icon);
            }
            _potionRow.Visible = _potionRow.GetChildCount() > 0;
            _autoFit?.Request();
        }
        internal bool ContainsVisiblePoint(Vector2 point)
        {
            foreach (var view in _cardViews.Values)
                if (Hits(view.Control, point)) return true;
            return Hits(_statsRow, point) || Hits(_potionRow, point) || Hits(_empty, point);
        }
        internal void Cleanup()
        {
            if (_disposed) return;
            _disposed = true;
            UnsubscribeHand(); UnsubscribeCardUpgrades(); UnbindStatus();
            _player.PotionProcured -= OnPotionChanged;
            _player.PotionDiscarded -= OnPotionChanged;
            _player.UsedPotionRemoved -= OnPotionChanged;
            ClearCards();
        }
    }
}
