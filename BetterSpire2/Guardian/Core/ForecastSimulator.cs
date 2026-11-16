#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BetterSpire2.Guardian.Core;

/// <summary>
/// Sequential shadow simulation. It does not mutate the input or call game commands,
/// RNG, or game objects. Random target outcomes are explored as separate pure states.
/// </summary>
public static class ForecastSimulator
{
    private const int MaxDistinctStates = 256;

    private sealed class RuleState
    {
        public HpRule Spec { get; }
        public int Charges;
        public RuleState(HpRule spec) { Spec = spec; Charges = Math.Max(0, spec.Charges); }
        public RuleState Clone() => new(Spec) { Charges = Charges };
    }

    private sealed class Actor
    {
        public ActorSnapshot Spec { get; }
        public int Hp, Block, ReviveHp, Loss, Healing, Gained, Spent, Revivals, Buffers, Prevented;
        public decimal DamageThisTurn, AttackMultiplier = 1m;
        public decimal DoomThreshold;
        public bool PowersCleared;
        public string? FirstLethal;
        public List<RuleState> Rules;

        public Actor(ActorSnapshot spec)
        {
            Spec = spec;
            Hp = Math.Clamp(spec.Hp, 0, Math.Max(0, spec.MaxHp));
            Block = Math.Max(0, spec.Block);
            ReviveHp = Math.Max(0, spec.ReviveHp);
            DoomThreshold = Hp > 0 ? Math.Max(0, spec.DoomThreshold) : 0;
            PowersCleared = Hp <= 0;
            DamageThisTurn = Math.Max(0, spec.DamageAlreadyReceived);
            Rules = (spec.HpRules ?? Array.Empty<HpRule>()).Select(r => new RuleState(r)).ToList();
        }

        private Actor(Actor source)
        {
            Spec = source.Spec;
            Hp = source.Hp; Block = source.Block; ReviveHp = source.ReviveHp;
            Loss = source.Loss; Healing = source.Healing; Gained = source.Gained;
            Spent = source.Spent; Revivals = source.Revivals; Buffers = source.Buffers;
            Prevented = source.Prevented; DamageThisTurn = source.DamageThisTurn;
            AttackMultiplier = source.AttackMultiplier; FirstLethal = source.FirstLethal;
            DoomThreshold = source.DoomThreshold; PowersCleared = source.PowersCleared;
            Rules = source.Rules.Select(r => r.Clone()).ToList();
        }

        public Actor Clone() => new(this);
    }

    private sealed class Scenario
    {
        public Dictionary<string, Actor> Actors { get; }
        public List<ForecastStep> Steps { get; }

        public Scenario(IReadOnlyList<ActorSnapshot> specs)
        {
            Actors = specs.ToDictionary(a => a.Id, a => new Actor(a), StringComparer.Ordinal);
            Steps = new List<ForecastStep>();
        }

        private Scenario(Scenario source)
        {
            Actors = source.Actors.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.Ordinal);
            Steps = new List<ForecastStep>(source.Steps);
        }

        public Scenario Clone() => new(this);
    }

    public static ForecastResult Run(ForecastInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Actors.Count > MaxDistinctStates || input.Events.Count > 20000)
            throw new ArgumentException("Forecast exceeds the safety budget.", nameof(input));
        var actorIds = input.Actors.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        if (actorIds.Count != input.Actors.Count) throw new ArgumentException("Duplicate forecast actor id.", nameof(input));
        if (!actorIds.Contains(input.LocalPlayerId)) throw new ArgumentException("Missing local player.");
        ValidateReferences(input, actorIds);

        var scenarios = new List<Scenario> { new(input.Actors) };
        bool branchLimitReached = false;
        foreach (var forecastEvent in input.Events)
        {
            if (forecastEvent.Kind == ForecastEventKind.RandomTargetDamage)
            {
                scenarios = BranchRandomDamage(scenarios, forecastEvent, input, ref branchLimitReached);
                continue;
            }

            foreach (var scenario in scenarios)
                ApplyEvent(scenario, forecastEvent, input);
            scenarios = Deduplicate(scenarios);
        }

        if (scenarios.Count == 0) throw new InvalidOperationException("Forecast produced no outcomes.");
        var worstCase = scenarios
            .OrderBy(s => s.Actors[input.LocalPlayerId].Hp)
            .ThenByDescending(s => s.Actors[input.LocalPlayerId].Loss)
            .First();
        var output = new Dictionary<string, ActorForecast>(StringComparer.Ordinal);
        foreach (var spec in input.Actors)
        {
            var versions = scenarios.Select(s => s.Actors[spec.Id]).ToArray();
            int minimumHp = versions.Min(a => a.Hp);
            int maximumHp = versions.Max(a => a.Hp);
            int minimumLoss = versions.Min(a => a.Loss);
            int maximumLoss = versions.Max(a => a.Loss);
            Actor firstLethal = versions.FirstOrDefault(a => a.Hp == minimumHp && a.FirstLethal != null)
                ?? versions.OrderBy(a => a.Hp).ThenByDescending(a => a.Loss).First();
            output.Add(spec.Id, new ActorForecast(spec.Id, spec.Name, spec.Hp, minimumHp,
                spec.Block, versions.Min(a => a.Block), versions.Max(a => a.Gained),
                versions.Max(a => a.Spent), maximumLoss, versions.Max(a => a.Healing),
                versions.Max(a => a.Revivals), versions.Max(a => a.Buffers),
                versions.Max(a => a.Prevented), firstLethal.FirstLethal,
                spec.SharedBlockOwner == input.LocalPlayerId,
                minimumHp, maximumHp, minimumLoss, maximumLoss));
        }

        return new ForecastResult(output, worstCase.Steps,
            input.Warnings.Distinct().ToArray(), input.LocalPlayerId,
            ScenarioCount: scenarios.Count,
            BranchEnumerationComplete: !branchLimitReached,
            BranchLimitReached: branchLimitReached);
    }

    private static void ValidateReferences(ForecastInput input, HashSet<string> actorIds)
    {
        foreach (var actor in input.Actors)
            if (actor.RedirectPoweredAttacksBeforeBlockTo is { } pet &&
                (pet == actor.Id || !actorIds.Contains(pet)))
                throw new ArgumentException("Invalid pre-block redirect: " + pet);
        foreach (var forecastEvent in input.Events)
        {
            if (!actorIds.Contains(forecastEvent.Target))
                throw new ArgumentException("Unknown forecast target: " + forecastEvent.Target);
            foreach (var option in forecastEvent.TargetOptions ?? Array.Empty<ForecastTargetDamage>())
                if (!actorIds.Contains(option.Target))
                    throw new ArgumentException("Unknown random forecast target: " + option.Target);
            foreach (string id in forecastEvent.AffectedActors ?? Array.Empty<string>())
                if (!actorIds.Contains(id)) throw new ArgumentException("Unknown affected forecast actor: " + id);
            foreach (string id in forecastEvent.EligibleOwners ?? Array.Empty<string>())
                if (!actorIds.Contains(id)) throw new ArgumentException("Unknown eligible forecast owner: " + id);
            if (forecastEvent.Attacker != null && !actorIds.Contains(forecastEvent.Attacker))
                throw new ArgumentException("Unknown forecast attacker: " + forecastEvent.Attacker);
        }
        foreach (string id in input.EnemyIds ?? Array.Empty<string>())
            if (!actorIds.Contains(id)) throw new ArgumentException("Unknown enemy forecast actor: " + id);
    }

    private static List<Scenario> BranchRandomDamage(List<Scenario> scenarios,
        ForecastEvent forecastEvent, ForecastInput input, ref bool branchLimitReached)
    {
        var expanded = new List<Scenario>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        bool stop = false;
        bool limitReached = false;
        foreach (var source in scenarios)
        {
            if (MustSkip(forecastEvent, source, input))
            {
                AddDistinct(source);
                if (stop) break;
                continue;
            }

            var options = (forecastEvent.TargetOptions ?? Array.Empty<ForecastTargetDamage>())
                .Where(option => source.Actors[option.Target].Hp > 0).ToArray();
            if (options.Length == 0)
            {
                AddDistinct(source);
                if (stop) break;
                continue;
            }

            foreach (var option in options)
            {
                var branch = source.Clone();
                var damage = forecastEvent with
                {
                    Kind = ForecastEventKind.Damage,
                    Target = option.Target,
                    Amount = option.Amount,
                    BypassesBlock = option.BypassesBlock,
                    DamageCap = option.DamageCap,
                    TargetOptions = null
                };
                ApplyEvent(branch, damage, input);
                AddDistinct(branch);
                if (stop) break;
            }
            if (stop) break;
        }
        if (limitReached) branchLimitReached = true;
        return expanded;

        void AddDistinct(Scenario candidate)
        {
            string state = Fingerprint(candidate);
            if (!seen.Add(state)) return;
            if (expanded.Count >= MaxDistinctStates)
            {
                limitReached = true;
                stop = true;
                return;
            }
            expanded.Add(candidate);
        }
    }

    private static List<Scenario> Deduplicate(List<Scenario> scenarios)
    {
        var unique = new List<Scenario>(scenarios.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scenario in scenarios)
            if (seen.Add(Fingerprint(scenario))) unique.Add(scenario);
        return unique;
    }

    private static string Fingerprint(Scenario scenario)
    {
        var text = new StringBuilder();
        foreach (var pair in scenario.Actors.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var actor = pair.Value;
            text.Append(pair.Key).Append(':').Append(actor.Hp).Append(',').Append(actor.Block).Append(',')
                .Append(actor.ReviveHp).Append(',').Append(actor.Loss).Append(',').Append(actor.Healing).Append(',')
                .Append(actor.Gained).Append(',').Append(actor.Spent).Append(',').Append(actor.Revivals).Append(',')
                .Append(actor.Buffers).Append(',').Append(actor.Prevented).Append(',')
                .Append(actor.DamageThisTurn.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(actor.AttackMultiplier.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(actor.DoomThreshold.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(actor.PowersCleared).Append(',')
                .Append(actor.FirstLethal).Append('|');
            foreach (var rule in actor.Rules) text.Append(rule.Charges).Append(',');
            text.Append(';');
        }
        return text.ToString();
    }

    private static bool MustSkip(ForecastEvent forecastEvent, Scenario scenario, ForecastInput input)
    {
        if (forecastEvent.RequireLivingAttacker && forecastEvent.Attacker != null &&
            scenario.Actors[forecastEvent.Attacker].Hp <= 0) return true;
        if (forecastEvent.StopWhenNoEnemies && input.EnemyIds is { Count: > 0 } &&
            input.EnemyIds.All(id => scenario.Actors[id].Hp <= 0)) return true;
        if (forecastEvent.StopWhenCombatOver)
        {
            bool anyEnemyAlive = input.EnemyIds is { Count: > 0 } &&
                input.EnemyIds.Any(id => scenario.Actors[id].Hp > 0);
            bool anyPlayerAlive = scenario.Actors.Values.Any(actor => actor.Spec.IsPlayer && actor.Hp > 0);
            if (!anyEnemyAlive || !anyPlayerAlive) return true;
        }
        return false;
    }

    private static void ApplyEvent(Scenario scenario, ForecastEvent forecastEvent, ForecastInput input)
    {
        if (MustSkip(forecastEvent, scenario, input)) return;
        switch (forecastEvent.Kind)
        {
            case ForecastEventKind.DoomCheck:
                ResolveDoomCheck(forecastEvent, scenario);
                return;
            case ForecastEventKind.DamageCommand:
                ResolveDamageCommand(forecastEvent, scenario);
                return;
            case ForecastEventKind.RandomTargetDamage:
                throw new InvalidOperationException("Random damage must be expanded into target branches.");
        }

        if (!scenario.Actors.TryGetValue(forecastEvent.Target, out var target))
            throw new ArgumentException("Unknown forecast target: " + forecastEvent.Target);
        if (target.Hp <= 0) return;
        switch (forecastEvent.Kind)
        {
            case ForecastEventKind.AttackMultiplier:
                target.AttackMultiplier *= Math.Max(0, forecastEvent.Amount);
                break;
            case ForecastEventKind.ClearBlock:
                target.Block = 0;
                break;
            case ForecastEventKind.Block:
            {
                int added = Math.Min(ToInt(forecastEvent.Amount), 999999999 - target.Block);
                target.Block += added;
                target.Gained = Add(target.Gained, added);
                scenario.Steps.Add(new(forecastEvent.Target, forecastEvent.Source, forecastEvent.Phase,
                    forecastEvent.Kind, 0, 0, added, 0, 0, target.Hp, target.Block, 0, 0, false));
                break;
            }
            case ForecastEventKind.Heal:
            {
                int heal = Math.Min(ToInt(forecastEvent.Amount), target.Spec.MaxHp - target.Hp);
                target.Hp += heal;
                target.Healing = Add(target.Healing, heal);
                scenario.Steps.Add(new(forecastEvent.Target, forecastEvent.Source, forecastEvent.Phase,
                    forecastEvent.Kind, 0, 0, 0, 0, heal, target.Hp, target.Block, 0, 0, false));
                break;
            }
            case ForecastEventKind.Damage:
                ResolveDamage(forecastEvent, target, scenario);
                break;
            case ForecastEventKind.DamageCommand:
            case ForecastEventKind.DoomCheck:
            case ForecastEventKind.RandomTargetDamage:
                throw new InvalidOperationException("A grouped or branching event reached single-target resolution.");
            default:
                throw new ArgumentOutOfRangeException(nameof(forecastEvent.Kind));
        }
    }

    private static void ResolveDamageCommand(ForecastEvent command, Scenario scenario)
    {
        // AttackCommand calls CreatureCmd.Damage once per hit with a snapshot of all currently
        // living targets. CreatureCmd checks the dealer once at command entry; a Thorns death
        // during the first receiver does not cancel the other receivers in this same hit.
        foreach (var option in command.TargetOptions ?? Array.Empty<ForecastTargetDamage>())
        {
            var target = scenario.Actors[option.Target];
            if (target.Hp <= 0) continue;
            var damage = command with
            {
                Kind = ForecastEventKind.Damage,
                Target = option.Target,
                Amount = option.Amount,
                BypassesBlock = option.BypassesBlock,
                DamageCap = option.DamageCap,
                RequireLivingAttacker = false,
                StopWhenNoEnemies = false,
                TargetOptions = null
            };
            ResolveDamage(damage, target, scenario);
        }
    }

    private static void ResolveDoomCheck(ForecastEvent forecastEvent, Scenario scenario)
    {
        var affected = forecastEvent.AffectedActors ?? Array.Empty<string>();
        var doomed = affected
            .Where(id => scenario.Actors[id].Hp > 0 && !scenario.Actors[id].PowersCleared &&
                scenario.Actors[id].DoomThreshold > 0 &&
                scenario.Actors[id].Hp <= scenario.Actors[id].DoomThreshold).ToArray();
        if (doomed.Length == 0) return;

        // v111 only starts the side-wide kill from the first doomed creature when that creature
        // has a DoomPower owner participating in this exact side-turn phase.
        string first = doomed[0];
        var firstOwner = scenario.Actors[first];
        if (firstOwner.Hp <= 0 || firstOwner.PowersCleared ||
            !(forecastEvent.EligibleOwners ?? Array.Empty<string>()).Contains(first,
                StringComparer.Ordinal)) return;

        foreach (string id in doomed)
        {
            var actor = scenario.Actors[id];
            if (actor.Hp <= 0 || actor.PowersCleared || actor.DoomThreshold <= 0 ||
                actor.Hp > actor.DoomThreshold) continue;
            int hpAtKill = actor.Hp;
            actor.Hp = 0;
            actor.Loss = Add(actor.Loss, hpAtKill);
            actor.FirstLethal ??= forecastEvent.Source;
            int healed = 0;
            bool revived = false;
            if (actor.ReviveHp > 0)
            {
                healed = Math.Min(actor.Spec.MaxHp, actor.ReviveHp);
                actor.Hp = healed;
                actor.ReviveHp = 0;
                actor.Healing = Add(actor.Healing, healed);
                actor.Revivals++;
                revived = true;
            }
            else
            {
                actor.PowersCleared = true;
                actor.DoomThreshold = 0;
            }
            scenario.Steps.Add(new(id, forecastEvent.Source, forecastEvent.Phase,
                ForecastEventKind.DoomCheck, hpAtKill, 0, 0, hpAtKill, healed,
                actor.Hp, actor.Block, 0, 0, revived));
        }
    }

    private static void ResolveDamage(ForecastEvent e, Actor original, Scenario scenario)
    {
        // BeforeDamageReceived awaits Thorns damage, then CreatureCmd continues the already
        // engaged hit even if the nested retaliation killed its dealer.
        if (e.TriggersThorns && e.Attacker is { } attackerId &&
            scenario.Actors.TryGetValue(attackerId, out var attacker) && attacker.Hp > 0)
        {
            foreach (var reaction in ActiveReactions(original))
            {
                if (reaction.Kind != DamageReactionKind.Thorns || attacker.Hp <= 0) continue;
                ApplyReactionDamage(scenario, original, attacker, reaction, e.Phase);
            }
        }

        decimal damage = Math.Max(0, e.Amount);
        if (e.PoweredAttack) damage *= original.AttackMultiplier;
        damage = Math.Max(0, Math.Min(damage, e.DamageCap));
        Actor? preBlockRecipient = null;
        int preBlockLost = 0;
        if (e.PoweredAttack && original.Spec.RedirectPoweredAttacksBeforeBlockTo is { } shieldId &&
            scenario.Actors.TryGetValue(shieldId, out var shield) && shield.Hp > 0)
        {
            preBlockRecipient = shield;
            // v113 BeforeWuppy applies no HP modifiers; only integer overkill reaches block/Osty.
            int remaining = ApplyHp(shield, e, damage, out preBlockLost, out int shieldHealed, out bool shieldRevived);
            scenario.Steps.Add(new(shieldId, e.Source + " / Wuppy", e.Phase, e.Kind, damage, 0, 0,
                preBlockLost, shieldHealed, shield.Hp, shield.Block, 0, 0, shieldRevived));
            damage = remaining;
        }
        Actor blockOwner = original;
        if (original.Spec.SharedBlockOwner is string owner && scenario.Actors.TryGetValue(owner, out var shared))
            blockOwner = shared;
        // The native command subtracts the integer part from block but keeps decimal absorbed damage.
        decimal blocked = e.BypassesBlock ? 0 : Math.Min(blockOwner.Block, damage);
        int spent = ToInt(blocked);
        blockOwner.Block -= spent;
        blockOwner.Spent = Add(blockOwner.Spent, spent);
        decimal unblocked = Math.Max(0, damage - blocked);
        Actor recipient = original;
        if (e.PoweredAttack && original.Spec.RedirectPoweredAttacksTo is string petId &&
            scenario.Actors.TryGetValue(petId, out var pet) && pet.Hp > 0)
            recipient = pet;
        // Block is consumed only once when a powered attack is redirected to a pet.
        decimal loss = ModifyHp(recipient, unblocked, out int buffers);
        int overflow = ApplyHp(recipient, e, loss, out int hpLost, out int healed, out bool revived);
        int prevented = Math.Max(0, ToInt(unblocked) - ToInt(loss));
        recipient.Buffers += buffers;
        recipient.Prevented = Add(recipient.Prevented, prevented);
        scenario.Steps.Add(new(recipient.Spec.Id, e.Source, e.Phase, e.Kind, damage, spent, 0,
            hpLost, healed, recipient.Hp, blockOwner.Block, prevented, buffers, revived));

        int playerLost = 0;
        if (!ReferenceEquals(recipient, original))
        {
            // Osty overflow receives the player's personal HP-loss rules after pet damage.
            decimal playerLoss = ModifyHp(original, overflow, out int playerBuffers);
            ApplyHp(original, e, playerLoss, out playerLost, out int playerHealed, out bool playerRevived);
            int playerPrevented = Math.Max(0, overflow - ToInt(playerLoss));
            original.Buffers += playerBuffers;
            original.Prevented = Add(original.Prevented, playerPrevented);
            // v111 emits an empty result for the player even when the pet absorbs the whole hit.
            if (overflow > 0 || original.Hp > 0)
                scenario.Steps.Add(new(original.Spec.Id, e.Source + " / Osty", e.Phase, e.Kind,
                    overflow, 0, 0, playerLost, playerHealed, original.Hp, original.Block,
                    playerPrevented, playerBuffers, playerRevived));
        }

        // Hook.AfterDamageReceived is dispatched per DamageResult receiver. A fatal receiver
        // that remains dead is skipped; an alive receiver can trigger Flame Barrier even when
        // block or Osty prevented its own HP loss.
        if (preBlockRecipient != null) AfterDamageReceived(preBlockRecipient, preBlockLost, e, scenario);
        AfterDamageReceived(recipient, hpLost, e, scenario);
        if (!ReferenceEquals(recipient, original)) AfterDamageReceived(original, playerLost, e, scenario);
    }

    private static void ApplyReactionDamage(Scenario scenario, Actor dealer, Actor target,
        DamageReaction reaction, string phase)
    {
        if (dealer.Hp <= 0 || target.Hp <= 0) return;
        var reflection = new ForecastEvent(ForecastEventKind.Damage, target.Spec.Id,
            reaction.Source, reaction.ForTarget(target.Spec.Id), Attacker: dealer.Spec.Id,
            RequireLivingAttacker: true, Phase: phase);
        ResolveDamage(reflection, target, scenario);
    }

    private static void AfterDamageReceived(Actor receiver, int hpLost, ForecastEvent incoming, Scenario scenario)
    {
        if (receiver.Hp <= 0) return;
        receiver.DamageThisTurn += hpLost;
        if (hpLost > 0)
            foreach (var rule in receiver.Rules)
                if (rule.Spec.Kind == HpRuleKind.Slippery && rule.Charges > 0)
                    rule.Charges--;

        if (!incoming.PoweredAttack || incoming.Attacker is not { } attackerId ||
            !scenario.Actors.TryGetValue(attackerId, out var attacker) || attacker.Hp <= 0) return;
        foreach (var reaction in ActiveReactions(receiver))
        {
            if (reaction.Kind != DamageReactionKind.FlameBarrier || attacker.Hp <= 0) continue;
            ApplyReactionDamage(scenario, receiver, attacker, reaction, incoming.Phase);
        }
    }

    private static IReadOnlyList<DamageReaction> ActiveReactions(Actor actor) => actor.PowersCleared
        ? Array.Empty<DamageReaction>()
        : actor.Spec.DamageReactions ?? Array.Empty<DamageReaction>();

    private static decimal ModifyHp(Actor target, decimal amount, out int buffersUsed)
    {
        buffersUsed = 0;
        decimal value = Math.Max(0, amount);
        // Native hook order is the normal AfterOsty listeners, followed by the Late listeners.
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
                        // Buffer only spends when a positive integer HP loss would otherwise occur.
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
        int overflow = Math.Max(0, intDamage - actor.Hp);
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
            else
            {
                actor.PowersCleared = true;
                actor.DoomThreshold = 0;
            }
        }
        return overflow;
    }

    private static int ToInt(decimal value) => (int)Math.Clamp(decimal.Truncate(value), 0, 999999999);
    private static int Add(int a, int b) => (int)Math.Min((long)a + b, int.MaxValue);
}
