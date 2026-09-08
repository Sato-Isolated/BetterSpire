#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterSpire2.Guardian.Core;

/// <summary>
/// Deterministic, sequential shadow simulation. Input records are never mutated.
/// Reproduces the inspected DLL's decimal block consumption, truncation, HP modifier
/// ordering, owner block sharing and powered-attack redirection. Unsupported game
/// callbacks belong in the adapter's warnings, never in an invented "safe" result.
/// </summary>
public static class ForecastSimulator
{
    private sealed class RuleState
    {
        public HpRule Spec { get; }
        public int Charges;
        public RuleState(HpRule spec) { Spec = spec; Charges = Math.Max(0, spec.Charges); }
    }
    private sealed class Actor
    {
        public ActorSnapshot Spec { get; }
        public int Hp, Block, ReviveHp, Loss, Healing, Gained, Spent, Revivals, Buffers, Prevented;
        public decimal DamageThisTurn, AttackMultiplier = 1m;
        public string? FirstLethal;
        public List<RuleState> Rules;
        public Actor(ActorSnapshot spec)
        {
            Spec = spec;
            Hp = Math.Clamp(spec.Hp, 0, Math.Max(0, spec.MaxHp));
            Block = Math.Max(0, spec.Block);
            ReviveHp = Math.Max(0, spec.ReviveHp);
            DamageThisTurn = Math.Max(0, spec.DamageAlreadyReceived);
            Rules = (spec.HpRules ?? Array.Empty<HpRule>()).Select(r => new RuleState(r)).ToList();
        }
    }

    public static ForecastResult Run(ForecastInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Actors.Count > 256 || input.Events.Count > 20000)
            throw new ArgumentException("Forecast exceeds the safety budget.", nameof(input));
        var actors = input.Actors.ToDictionary(a => a.Id, a => new Actor(a), StringComparer.Ordinal);
        if (!actors.ContainsKey(input.LocalPlayerId)) throw new ArgumentException("Missing local player.");
        var steps = new List<ForecastStep>();
        foreach (var e in input.Events)
        {
            if (!actors.TryGetValue(e.Target, out var target))
                throw new ArgumentException("Unknown forecast target: " + e.Target);
            if (target.Hp <= 0) continue;
            if (e.RequireLivingAttacker && e.Attacker != null &&
                (!actors.TryGetValue(e.Attacker, out var dealer) || dealer.Hp <= 0)) continue;
            if (e.StopWhenNoEnemies && input.EnemyIds is { Count: > 0 } &&
                input.EnemyIds.All(id => !actors.TryGetValue(id, out var foe) || foe.Hp <= 0)) continue;
            switch (e.Kind)
            {
                case ForecastEventKind.AttackMultiplier:
                    target.AttackMultiplier *= Math.Max(0, e.Amount);
                    break;
                case ForecastEventKind.ClearBlock:
                    target.Block = 0;
                    break;
                case ForecastEventKind.Block:
                {
                    int added = Math.Min(ToInt(e.Amount), 999999999 - target.Block);
                    target.Block += added;
                    target.Gained = Add(target.Gained, added);
                    steps.Add(new(e.Target, e.Source, e.Phase, e.Kind, 0, 0, added, 0, 0,
                        target.Hp, target.Block, 0, 0, false));
                    break;
                }
                case ForecastEventKind.Heal:
                {
                    int heal = Math.Min(ToInt(e.Amount), target.Spec.MaxHp - target.Hp);
                    target.Hp += heal;
                    target.Healing = Add(target.Healing, heal);
                    steps.Add(new(e.Target, e.Source, e.Phase, e.Kind, 0, 0, 0, 0, heal,
                        target.Hp, target.Block, 0, 0, false));
                    break;
                }
                case ForecastEventKind.Damage:
                    ResolveDamage(e, target, actors, steps);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(e.Kind));
            }
        }
        var result = actors.ToDictionary(p => p.Key, p =>
        {
            var a = p.Value;
            return new ActorForecast(a.Spec.Id, a.Spec.Name, a.Spec.Hp, a.Hp, a.Spec.Block,
                a.Block, a.Gained, a.Spent, a.Loss, a.Healing, a.Revivals, a.Buffers,
                a.Prevented, a.FirstLethal, a.Spec.SharedBlockOwner == input.LocalPlayerId);
        }, StringComparer.Ordinal);
        return new(result, steps, input.Warnings.Distinct().ToArray(), input.LocalPlayerId);
    }

    private static void ResolveDamage(ForecastEvent e, Actor original,
        Dictionary<string, Actor> actors, List<ForecastStep> steps)
    {
        decimal damage = Math.Max(0, e.Amount);
        if (e.PoweredAttack) damage *= original.AttackMultiplier;
        damage = Math.Max(0, Math.Min(damage, e.DamageCap));
        Actor blockOwner = original;
        if (original.Spec.SharedBlockOwner is string owner && actors.TryGetValue(owner, out var shared))
            blockOwner = shared;
        // The DLL subtracts the integer part from Block but returns decimal absorbed damage.
        decimal blocked = e.BypassesBlock ? 0 : Math.Min(blockOwner.Block, damage);
        int spent = ToInt(blocked);
        blockOwner.Block -= spent;
        blockOwner.Spent = Add(blockOwner.Spent, spent);
        decimal unblocked = Math.Max(0, damage - blocked);
        Actor recipient = original;
        if (e.PoweredAttack && original.Spec.RedirectPoweredAttacksTo is string petId &&
            actors.TryGetValue(petId, out var pet) && pet.Hp > 0)
            recipient = pet;
        // No second consumption of block when an attack is redirected to Osty.
        decimal loss = ModifyHp(recipient, unblocked, out int buffers);
        int overflow = ApplyHp(recipient, e, loss, out int hpLost, out int healed, out bool revived);
        int prevented = Math.Max(0, ToInt(unblocked) - ToInt(loss));
        recipient.Buffers += buffers;
        recipient.Prevented = Add(recipient.Prevented, prevented);
        steps.Add(new(recipient.Spec.Id, e.Source, e.Phase, e.Kind, damage, spent, 0,
            hpLost, healed, recipient.Hp, blockOwner.Block, prevented, buffers, revived));
        int playerLost = 0;
        if (!ReferenceEquals(recipient, original))
        {
            // Apply the PLAYER's AfterOsty reductions only to the pet's overkill.
            decimal playerLoss = ModifyHp(original, overflow, out int playerBuffers);
            ApplyHp(original, e, playerLoss, out playerLost, out int playerHealed, out bool playerRevived);
            int playerPrevented = Math.Max(0, overflow - ToInt(playerLoss));
            original.Buffers += playerBuffers;
            original.Prevented = Add(original.Prevented, playerPrevented);
            if (overflow > 0)
                steps.Add(new(original.Spec.Id, e.Source + " / Osty", e.Phase, e.Kind, overflow,
                    0, 0, playerLost, playerHealed, original.Hp, original.Block,
                    playerPrevented, playerBuffers, playerRevived));
        }
        // v111 CreatureCmd.Damage dispatches reactions over EACH DamageResult.Receiver,
        // after resolving both Osty's loss and the original target's overflow.
        // A dead receiver is skipped; one revived by a supported rule is alive again.
        AfterDamageReceived(recipient, hpLost);
        if (!ReferenceEquals(recipient, original)) AfterDamageReceived(original, playerLost);
    }

    private static void AfterDamageReceived(Actor receiver, int hpLost)
    {
        if (receiver.Hp <= 0) return;
        receiver.DamageThisTurn += hpLost;
        if (hpLost <= 0) return;
        foreach (var rule in receiver.Rules)
            if (rule.Spec.Kind == HpRuleKind.Slippery && rule.Charges > 0)
                rule.Charges--;
    }

    private static decimal ModifyHp(Actor target, decimal amount, out int buffersUsed)
    {
        buffersUsed = 0;
        decimal value = Math.Max(0, amount);
        // Order is captured as all normal AfterOsty listeners, then all Late listeners.
        foreach (var rule in target.Rules)
        {
            switch (rule.Spec.Kind)
            {
                case HpRuleKind.Reduction: value = Math.Max(0, value - rule.Spec.Value); break;
                case HpRuleKind.Cap: value = Math.Min(value, rule.Spec.Value); break;
                case HpRuleKind.TurnLimit:
                    value = Math.Min(value, Math.Max(0, rule.Spec.Value - target.DamageThisTurn)); break;
                case HpRuleKind.Slippery:
                    if (rule.Charges > 0) value = Math.Min(value, rule.Spec.Value);
                    break;
                case HpRuleKind.Buffer:
                    if (rule.Charges > 0)
                    {
                        // Hook only calls AfterModifying when the truncated value changes.
                        if (decimal.Truncate(value) > 0) { rule.Charges--; buffersUsed++; }
                        value = 0;
                    }
                    break;
            }
        }
        return Math.Max(0, value);
    }

    private static int ApplyHp(Actor actor, ForecastEvent e, decimal amount,
        out int lost, out int healed, out bool revived)
    {
        int intDamage = ToInt(amount);
        lost = Math.Min(actor.Hp, intDamage);
        int overkill = Math.Max(0, intDamage - actor.Hp);
        actor.Hp -= lost;
        actor.Loss = Add(actor.Loss, lost);
        healed = 0;
        revived = false;
        if (actor.Hp == 0 && lost > 0)
        {
            actor.FirstLethal ??= e.Source;
            if (actor.ReviveHp > 0)
            {
                healed = Math.Min(actor.Spec.MaxHp, actor.ReviveHp);
                actor.Hp = healed;
                actor.ReviveHp = 0;
                actor.Healing = Add(actor.Healing, healed);
                actor.Revivals++;
                revived = true;
            }
        }
        return overkill;
    }

    private static int ToInt(decimal value) => (int)Math.Clamp(decimal.Truncate(value), 0, 999999999);
    private static int Add(int a, int b) => (int)Math.Min((long)a + b, int.MaxValue);
}
