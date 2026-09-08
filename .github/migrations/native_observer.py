from pathlib import Path
r=Path('.')
p=r/'BetterSpire2/Guardian/Game/ForecastEventObserver.cs'
s=p.read_text().replace('private bool _bindingsDirty;', '''private bool _bindingsDirty, _checkStructure;
    private long _nextStructuralCheck;
    internal int BindingGeneration { get; private set; }''')
s=s.replace('        if (_bindingsDirty) BindModels();','''        long now = Environment.TickCount64;
        if (_checkStructure || now >= _nextStructuralCheck)
        {
            _checkStructure = false;
            _nextStructuralCheck = now + 2000;
            if (!_bindingsDirty && !StructureMatches())
            { _bindingsDirty = true; _changed(); }
        }
        if (_bindingsDirty) BindModels();''')
s=s.replace('            !_manager.IsOverOrEnding) Rebind();','''            !_manager.IsOverOrEnding)
        {
            // Generic value changes require invalidation, not delegate teardown.
            _checkStructure = true;
            Changed();
        }''')
s=s.replace('    private void BindModels()','''    private bool StructureMatches()
    {
        if (_state == null) return false;
        int creatures = 0, players = 0, states = 0, relics = 0, piles = 0, cards = 0;
        foreach (var creature in _state.Creatures)
            if (!Matches(_creatures, ref creatures, creature)) return false;
        foreach (var player in _state.Players)
        {
            if (!Matches(_players, ref players, player)) return false;
            foreach (var relic in player.Relics)
                if (!Matches(_relics, ref relics, relic)) return false;
            var combat = player.PlayerCombatState;
            if (combat == null) continue;
            if (!Matches(_playerStates, ref states, combat)) return false;
            foreach (var pile in combat.AllPiles)
                if (!Matches(_piles, ref piles, pile)) return false;
            foreach (var card in combat.Hand.Cards)
                if (!Matches(_cards, ref cards, card)) return false;
        }
        return creatures == _creatures.Count && players == _players.Count &&
            states == _playerStates.Count && relics == _relics.Count &&
            piles == _piles.Count && cards == _cards.Count;
    }
    private static bool Matches<T>(List<T> expected, ref int index, T item) where T : class =>
        index < expected.Count && ReferenceEquals(expected[index++], item);

    private void BindModels()''')
s=s.replace('        _bindingsDirty = false;\n    }','        _bindingsDirty = false;\n        BindingGeneration++;\n    }')
s=s.replace('_manager = null; _combatId = null; _nativeTrackerSubscribed = false;','_manager = null; _combatId = null; _nativeTrackerSubscribed = false;\n        _checkStructure = false; _nextStructuralCheck = 0;')
p.write_text(s)
p=r/'tests/GameBoundaryTests.cs';s=p.read_text()
anchor='        observer.Observe(state);\n        before = changes; manager.StateTracker.Publish(new CombatState());'
assert anchor in s
s=s.replace(anchor,'''        int generation = observer.BindingGeneration;
        for (int i = 0; i < 100; i++)
        { manager.StateTracker.Publish(state); observer.Observe(state); }
        Check(observer.BindingGeneration == generation, "value notifications never rebuild delegates");
        var replacement = new CardModel();
        player.PlayerCombatState!.Hand.Cards[0] = replacement;
        manager.StateTracker.Publish(state); observer.Observe(state);
        Check(observer.BindingGeneration == generation + 1, "same-size native hand replacement rebinds");
        before = changes; card.Forge(); replacement.Forge();
        Check(changes == before + 1, "old card detached and replacement observed");
        card = replacement;
        var replacementRelic = new RelicModel();
        player.Relics[0] = replacementRelic;
        manager.StateTracker.Publish(state); observer.Observe(state);
        Check(observer.BindingGeneration == generation + 2, "same-size relic replacement rebinds");
        relic = replacementRelic;
        before = changes; manager.StateTracker.Publish(new CombatState());''');p.write_text(s)
p=r/'BetterSpire2/Patches/UI/IntentLabelPatch.cs';s=p.read_text().replace('____valueLabel.Text = cached.Output;', '____valueLabel.SetTextAutoSize(cached.Output);').replace('____valueLabel.Text = output;', '____valueLabel.SetTextAutoSize(output);').replace('____valueLabel.Text = text + $" ({customTotal})";', '____valueLabel.SetTextAutoSize(text + $" ({customTotal})");');p.write_text(s)
(r/'BetterSpire2/Guardian/Core/ForecastHookPolicy.cs').write_text('''#nullable enable
using System;
namespace BetterSpire2.Guardian.Core;

/// <summary>New lifecycle reactions default to uncertain rather than silently supported.</summary>
public static class ForecastHookPolicy
{
    public static bool IsReaction(string methodName) =>
        methodName.StartsWith("Before", StringComparison.Ordinal) ||
        methodName.StartsWith("After", StringComparison.Ordinal);
}
''')
p=r/'BetterSpire2/Guardian/Game/CapabilityAudit.cs';s=p.read_text().replace('using System.Reflection;', 'using System.Reflection;\nusing BetterSpire2.Guardian.Core;')
s=s.replace('TrackedHooks.Contains(m.Name) || m.Name.StartsWith("AfterModifying", StringComparison.Ordinal)', 'TrackedHooks.Contains(m.Name) || ForecastHookPolicy.IsReaction(m.Name)')
s=s.replace('"BeforeAttack", "AfterAttack", "ModifyAttackHitCount",','"BeforeAttack", "AfterAttack", "ModifyAttackHitCount",\n        "BeforeDamageGiven", "AfterDamageGiven", "AfterDiedToDoom",');p.write_text(s)
p=r/'BetterSpire2/Guardian/Game/GameForecastAdapter.cs';s=p.read_text().replace('        var actors = _creatures.Select(CaptureActor).ToArray();','''        if (_combatListeners.OfType<DoomPower>().Any(p => p.Amount > 0))
            Warn(T("Doom: death checks and chained reactions are not simulated; healing/orbs can change the outcome.",
                "Doom : seuils de mort et réactions en chaîne non simulés ; soins/orbes peuvent modifier le résultat."));
        var actors = _creatures.Select(CaptureActor).ToArray();''');p.write_text(s)
