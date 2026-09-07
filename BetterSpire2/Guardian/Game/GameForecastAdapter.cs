#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BetterSpire2.Guardian.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;

namespace BetterSpire2.Guardian.Game;

/// <summary>
/// Captures one synchronous, read-only forecast on the Godot main thread.
/// Only pure preview/modification hooks are invoked. No game commands, RNG samples,
/// cloned game objects, mutable game setters or asynchronous callbacks are executed.
/// </summary>
internal sealed partial class GameForecastAdapter
{
    private readonly CombatState _state;
    private readonly Player _local;
    private readonly AbstractModel[] _combatListeners;
    private readonly AbstractModel[] _runListeners;
    private readonly Creature[] _creatures;
    private readonly Dictionary<Creature, string> _ids;
    private readonly List<ForecastEvent> _events = new();
    private readonly List<string> _warnings = new();
    private readonly HashSet<string> _warningSet = new(StringComparer.Ordinal);
    private static readonly Dictionary<(Type, string), PropertyInfo?> ReadProperties = new();

    internal GameForecastAdapter(CombatState state, Player local)
    {
        _state = state;
        _local = local;
        _combatListeners = state.IterateHookListeners().ToArray();
        _runListeners = state.RunState.IterateHookListeners(state).ToArray();
        _creatures = state.Creatures.ToArray();
        _ids = _creatures.Select((c, i) => (c, id: i.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToDictionary(p => p.c, p => p.id);
    }

    internal IReadOnlyDictionary<Creature, string> ActorIds => _ids;

    internal ForecastResult Capture()
    {
        if (typeof(CombatState).Module.ModuleVersionId != new Guid("97f10687-c306-4798-ab75-8b9f23f34dfb"))
            Warn(T("Different game build: formulas require revalidation.", "Autre version du jeu : formules à revalider."));
        CapabilityAudit.Inspect(_runListeners.Concat(_combatListeners).Distinct(), Warn);
        if (_state.Players.Count > 1)
            Warn(T("Team actions and targeting may change this forecast.",
                "Les actions et le ciblage en équipe peuvent modifier cette prévision."));
        var actors = _creatures.Select(CaptureActor).ToArray();
        BuildPlayerEndTurn();
        BuildEnemyTurn();
        var input = new ForecastInput(actors, _events, _warnings.ToArray(), Id(_local.Creature),
            _state.Enemies.Select(Id).ToArray());
        return ForecastSimulator.Run(input);
    }

    private ActorSnapshot CaptureActor(Creature creature)
    {
        var rules = new List<HpRule>();
        decimal alreadyReceived = 0;
        int reviveHp = 0;
        foreach (var listener in _runListeners)
        {
            if (!ReferenceEquals(Owner(listener), creature)) continue;
            switch (listener)
            {
                case TungstenRod rod:
                    rules.Add(new(HpRuleKind.Reduction, Var(rod.DynamicVars, "HpLossReduction"), Source: Source(rod)));
                    break;
                case IntangiblePower p when p.Amount > 0:
                    rules.Add(new(HpRuleKind.Cap, 1m, Source: Source(p)));
                    break;
                case SlipperyPower p when p.Amount > 0:
                    rules.Add(new(HpRuleKind.Slippery, 1m, p.Amount, Source(p)));
                    break;
                case BeatingRemnant relic:
                    alreadyReceived = ReadRequired<decimal>(relic, "DamageReceivedThisTurn");
                    rules.Add(new(HpRuleKind.TurnLimit, Var(relic.DynamicVars, "MaxHpLoss"), Source: Source(relic)));
                    break;
                case LizardTail tail when !tail.WasUsed:
                    reviveHp = (int)Math.Clamp(Math.Max(1m,
                        creature.MaxHp * Var(tail.DynamicVars, "Heal") / 100m), 1, creature.MaxHp);
                    break;
            }
        }
        // The engine finishes every normal HP modifier BEFORE any Late modifier.
        foreach (var listener in _runListeners)
            if (listener is BufferPower buffer && buffer.Owner == creature && buffer.Amount > 0)
                rules.Add(new(HpRuleKind.Buffer, 0, buffer.Amount, Source(buffer)));
        var redirectors = _combatListeners.OfType<DieForYouPower>()
            .Where(p => p.Owner.IsAlive && p.Owner.PetOwner?.Creature == creature).ToArray();
        if (redirectors.Length > 1)
            Warn(T("Multiple pet redirects: ordering is uncertain.", "Plusieurs redirections de familier : ordre incertain."));
        var redirect = redirectors.LastOrDefault()?.Owner;
        return new ActorSnapshot(Id(creature), creature.Name, creature.CurrentHp, creature.MaxHp,
            creature.Block, creature.PetOwner is { } owner ? Id(owner.Creature) : null,
            redirect != null && _ids.ContainsKey(redirect) ? Id(redirect) : null,
            rules, alreadyReceived, reviveHp, creature.IsPlayer);
    }

    private void Damage(Creature target, decimal raw, ValueProp props, string source, string phase,
        Creature? dealer = null, CardModel? card = null, bool requireDealer = false,
        bool stopOnVictory = false)
    {
        dealer ??= card?.Owner?.Creature;
        if (!_ids.ContainsKey(target)) { Warn(T("Target unavailable.", "Cible indisponible.")); return; }
        decimal modified = Hook.ModifyDamage(_state.RunState, _state, target, dealer!, raw, props,
            card!, ModifyDamageHookType.Additive | ModifyDamageHookType.Multiplicative,
            CardPreviewMode.None, out _);
        decimal cap = decimal.MaxValue;
        foreach (var model in _runListeners)
            cap = Math.Min(cap, model.ModifyDamageCap(target, props, dealer!, card!));
        bool powered = (props & ValueProp.Move) != 0 && (props & ValueProp.Unpowered) == 0;
        _events.Add(new(ForecastEventKind.Damage, Id(target), source, modified,
            (props & ValueProp.Unblockable) != 0, powered, cap,
            dealer != null && _ids.TryGetValue(dealer, out var id) ? id : null,
            requireDealer, phase, stopOnVictory));
    }

    private void Block(Creature target, decimal raw, ValueProp props, string source, string phase)
    {
        decimal modified = Hook.ModifyBlock(_state, target, raw, props, null!, null!, out _);
        _events.Add(new(ForecastEventKind.Block, Id(target), source, Math.Max(0, modified), Phase: phase));
    }

    private void RelicBlock(RelicModel relic, decimal count, string phase)
    {
        var variable = relic.DynamicVars.Block;
        Block(relic.Owner.Creature, variable.BaseValue * count, variable.Props, Source(relic), phase);
    }

    private static Creature? Owner(AbstractModel model) => model switch
    {
        PowerModel power => power.Owner,
        RelicModel relic => relic.Owner?.Creature,
        CardModel card => card.Owner?.Creature,
        _ => null
    };

    private static decimal Var(DynamicVarSet vars, string key)
    {
        if (!vars.TryGetValue(key, out var value)) throw new InvalidOperationException("Missing game variable: " + key);
        return value.BaseValue;
    }

    private static TValue ReadRequired<TValue>(object instance, string name)
    {
        var key = (instance.GetType(), name);
        if (!ReadProperties.TryGetValue(key, out var property))
        {
            property = key.Item1.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            ReadProperties[key] = property;
        }
        return property?.GetValue(instance) is TValue value ? value :
            throw new InvalidOperationException("Game API changed: " + key.Item1.Name + "." + name);
    }

    private string Id(Creature creature) => _ids[creature];
    private void Warn(string message) { if (_warningSet.Add(message)) _warnings.Add(message); }
    private static string T(string english, string french) => ModText.IsFrench ? french : english;
    internal static string Source(AbstractModel model)
    {
        try
        {
            return model switch
            {
                RelicModel relic => relic.Title.GetFormattedText(),
                PowerModel power => power.Title.GetFormattedText(),
                CardModel card => card.Title,
                _ => model.GetType().Name
            };
        }
        catch { return model.GetType().Name; }
    }
}
