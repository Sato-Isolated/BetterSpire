#nullable enable
using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.TestSupport;

namespace BetterSpire2.Guardian.Game;

/// <summary>Gameplay-only invalidation. Dynamic-variable previews and mouse signals are not observed.</summary>
internal sealed class ForecastEventObserver : IDisposable
{
    private readonly Action _changed;
    private CombatState? _state;
    private CombatHistory? _history;
    private CombatManager? _manager;
    private CombatId? _combatId;
    private bool _nativeTrackerSubscribed;
    private readonly List<Creature> _creatures = new();
    private readonly List<Player> _players = new();
    private readonly List<PlayerCombatState> _playerStates = new();
    private readonly List<CardPile> _piles = new();
    private readonly List<CardModel> _cards = new();
    private readonly List<RelicModel> _relics = new();
    private readonly Dictionary<MonsterModel, object?> _moves = new();
    private bool _bindingsDirty;

    internal ForecastEventObserver(Action changed) => _changed = changed;
    internal void Observe(CombatState state)
    {
        if (!ReferenceEquals(state, _state))
        {
            Dispose();
            _state = state;
            _manager = CombatManager.Instance;
            _combatId = _manager.CurrentCombatId;
            _history = _manager.History;
            _history.Changed += Changed;
            // The game's tracker is a deferred UI notification, forbidden in backend
            // TestMode. Keep immediate signals below as a stale-display fence.
            if (!TestMode.IsOn)
            {
                _manager.StateTracker.CombatStateChanged += NativeStateChanged;
                _nativeTrackerSubscribed = true;
            }
            _manager.PlayerEndedTurn += PlayerEndedTurn;
            _manager.PlayerUnendedTurn += PlayerUnendedTurn;
            _state.CreaturesChanged += CreaturesChanged;
            _bindingsDirty = true;
            _changed();
        }
        if (_bindingsDirty) BindModels();
        // A move can change without a history entry. Read identities only; never evaluate damage here.
        foreach (var creature in _creatures)
        {
            var monster = creature.Monster;
            if (monster == null) continue;
            object? move = monster.NextMove;
            if (!_moves.TryGetValue(monster, out var previous) || !ReferenceEquals(move, previous))
            { _moves[monster] = move; _changed(); }
        }
    }
    private void NativeStateChanged(CombatState state)
    {
        if (ReferenceEquals(_state, state) && _manager?.IsCurrentLiveCombat(_combatId) == true &&
            !_manager.IsOverOrEnding) Rebind();
    }
    private void PlayerEndedTurn(Player _, bool canBackOut) => Changed();
    private void PlayerUnendedTurn(Player _) => Changed();
    private void Changed() { if (_state != null) _changed(); }
    private void ValuesChanged(int previous, int current) { if (previous != current) _changed(); }
    private void PowerChanged(PowerModel _) => _changed();
    private void PowerIncreased(PowerModel _, int amount, bool silent) => _changed();
    private void PowerDecreased(PowerModel _, bool silent) => _changed();
    private void CreaturesChanged(ICombatState _) => Rebind();
    private void RelicsChanged(RelicModel _) => Rebind();
    private void Rebind() { _bindingsDirty = true; _changed(); }
    private void CardChanged(CardModel _) => Rebind();
    private void BindModels()
    {
        UnbindModels();
        if (_state == null) return;
        foreach (var creature in _state.Creatures)
        {
            _creatures.Add(creature);
            creature.CurrentHpChanged += ValuesChanged;
            creature.MaxHpChanged += ValuesChanged;
            creature.BlockChanged += ValuesChanged;
            creature.PowerApplied += PowerChanged;
            creature.PowerRemoved += PowerChanged;
            creature.PowerIncreased += PowerIncreased;
            creature.PowerDecreased += PowerDecreased;
            if (creature.Monster is { } monster) _moves[monster] = monster.NextMove;
        }
        foreach (var player in _state.Players)
        {
            _players.Add(player);
            player.RelicObtained += RelicsChanged;
            player.RelicRemoved += RelicsChanged;
            foreach (var relic in player.Relics)
            {
                _relics.Add(relic);
                relic.DisplayAmountChanged += Changed;
                relic.StatusChanged += Changed;
            }
            var combat = player.PlayerCombatState;
            if (combat == null) continue;
            _playerStates.Add(combat);
            combat.PlayerTurnPhaseChanged += Changed;
            combat.EnergyChanged += ValuesChanged;
            combat.StarsChanged += ValuesChanged;
            foreach (var pile in combat.AllPiles)
            {
                _piles.Add(pile);
                pile.ContentsChanged += Rebind;
                pile.CardAdded += CardChanged;
                pile.CardRemoved += CardChanged;
            }
            foreach (var card in combat.Hand.Cards)
            {
                _cards.Add(card);
                card.Upgraded += Changed;
                card.EnchantmentChanged += Changed;
                card.AfflictionChanged += Changed;
                card.KeywordsChanged += Changed;
                card.EnergyCostChanged += Changed;
                card.StarCostChanged += Changed;
                card.ReplayCountChanged += Changed;
                card.Forged += Changed;
            }
        }
        _bindingsDirty = false;
    }
    private void UnbindModels()
    {
        foreach (var creature in _creatures)
        {
            creature.CurrentHpChanged -= ValuesChanged;
            creature.MaxHpChanged -= ValuesChanged;
            creature.BlockChanged -= ValuesChanged;
            creature.PowerApplied -= PowerChanged;
            creature.PowerRemoved -= PowerChanged;
            creature.PowerIncreased -= PowerIncreased;
            creature.PowerDecreased -= PowerDecreased;
        }
        foreach (var player in _players)
        { player.RelicObtained -= RelicsChanged; player.RelicRemoved -= RelicsChanged; }
        foreach (var combat in _playerStates)
        {
            combat.PlayerTurnPhaseChanged -= Changed;
            combat.EnergyChanged -= ValuesChanged;
            combat.StarsChanged -= ValuesChanged;
        }
        foreach (var pile in _piles)
        { pile.ContentsChanged -= Rebind; pile.CardAdded -= CardChanged; pile.CardRemoved -= CardChanged; }
        foreach (var card in _cards)
        {
            card.Upgraded -= Changed;
            card.EnchantmentChanged -= Changed;
            card.AfflictionChanged -= Changed;
            card.KeywordsChanged -= Changed;
            card.EnergyCostChanged -= Changed;
            card.StarCostChanged -= Changed;
            card.ReplayCountChanged -= Changed;
            card.Forged -= Changed;
        }
        foreach (var relic in _relics)
        { relic.DisplayAmountChanged -= Changed; relic.StatusChanged -= Changed; }
        _creatures.Clear(); _players.Clear(); _playerStates.Clear(); _piles.Clear(); _cards.Clear();
        _relics.Clear(); _moves.Clear();
    }
    public void Dispose()
    {
        if (_history != null) _history.Changed -= Changed;
        if (_manager != null)
        {
            if (_nativeTrackerSubscribed)
                _manager.StateTracker.CombatStateChanged -= NativeStateChanged;
            _manager.PlayerEndedTurn -= PlayerEndedTurn;
            _manager.PlayerUnendedTurn -= PlayerUnendedTurn;
        }
        if (_state != null) _state.CreaturesChanged -= CreaturesChanged;
        UnbindModels();
        _state = null; _history = null; _bindingsDirty = false;
        _manager = null; _combatId = null; _nativeTrackerSubscribed = false;
    }
}
