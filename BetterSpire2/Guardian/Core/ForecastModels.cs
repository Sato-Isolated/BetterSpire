#nullable enable
using System;
using System.Collections.Generic;

namespace BetterSpire2.Guardian.Core;

// No Godot, game objects, reflection, commands, tasks, RNG or global state in this layer.
public enum ForecastEventKind { Damage, DamageCommand, Block, Heal, AttackMultiplier, ClearBlock, RandomTargetDamage, DoomCheck }
public enum HpRuleKind { Reduction, Cap, Slippery, Buffer, TurnLimit }
public enum DamageReactionKind { Thorns, FlameBarrier }
public sealed record HpRule(HpRuleKind Kind, decimal Value, int Charges = 0, string Source = "");
public sealed record DamageReaction(DamageReactionKind Kind, decimal Amount, string Source,
    IReadOnlyDictionary<string, decimal>? AmountByTarget = null)
{
    public decimal ForTarget(string target) => AmountByTarget != null && AmountByTarget.TryGetValue(target, out var amount)
        ? amount : Amount;
}

/// <summary>Damage already modified for one possible receiver by the native game hooks.</summary>
public sealed record ForecastTargetDamage(string Target, decimal Amount,
    bool BypassesBlock = false, decimal DamageCap = decimal.MaxValue);

public sealed record ActorSnapshot(
    string Id, string Name, int Hp, int MaxHp, int Block,
    string? SharedBlockOwner = null, string? RedirectPoweredAttacksTo = null,
    IReadOnlyList<HpRule>? HpRules = null, decimal DamageAlreadyReceived = 0,
    int ReviveHp = 0, bool IsPlayer = false, decimal DoomThreshold = 0,
    IReadOnlyList<DamageReaction>? DamageReactions = null)
{
    public string? RedirectPoweredAttacksBeforeBlockTo { get; init; }
}

/// <summary>Amount for Damage is modified damage BEFORE caps and integer truncation.</summary>
public sealed record ForecastEvent(
    ForecastEventKind Kind, string Target, string Source, decimal Amount,
    bool BypassesBlock = false, bool PoweredAttack = false,
    decimal DamageCap = decimal.MaxValue, string? Attacker = null,
    bool RequireLivingAttacker = false, string Phase = "",
    // A dead poison target cannot take more poison; a redirected hit can still overflow.
    bool StopWhenNoEnemies = false, bool TriggersThorns = false,
    IReadOnlyList<ForecastTargetDamage>? TargetOptions = null,
    IReadOnlyList<string>? AffectedActors = null,
    IReadOnlyList<string>? EligibleOwners = null,
    bool StopWhenCombatOver = false);

public sealed record ForecastInput(
    IReadOnlyList<ActorSnapshot> Actors,
    IReadOnlyList<ForecastEvent> Events,
    IReadOnlyList<string> Warnings,
    string LocalPlayerId,
    IReadOnlyList<string>? EnemyIds = null);

public sealed record ForecastStep(
    string Target, string Source, string Phase, ForecastEventKind Kind,
    decimal Incoming, int BlockSpent, int BlockGained, int HpLost, int Healed,
    int HpAfter, int BlockAfter, int Prevented, int BuffersUsed, bool Revived);

public sealed record ActorForecast(
    string Id, string Name, int StartHp, int FinalHp, int StartBlock, int FinalBlock,
    int BlockGained, int BlockSpent, int HpLost, int Healed, int Revivals,
    int BuffersUsed, int Prevented, string? FirstLethalSource, bool IsLocalPet = false,
    int? FinalHpMinimum = null, int? FinalHpMaximum = null,
    int? HpLostMinimum = null, int? HpLostMaximum = null)
{
    public bool HasVariableOutcome => FinalHpMinimum != FinalHpMaximum || HpLostMinimum != HpLostMaximum;
    public int MinimumFinalHp => FinalHpMinimum ?? FinalHp;
    public int MaximumFinalHp => FinalHpMaximum ?? FinalHp;
    public int MinimumHpLost => HpLostMinimum ?? HpLost;
    public int MaximumHpLost => HpLostMaximum ?? HpLost;
}

public sealed record ForecastResult(
    IReadOnlyDictionary<string, ActorForecast> Actors,
    IReadOnlyList<ForecastStep> Steps,
    IReadOnlyList<string> Warnings,
    string LocalPlayerId,
    string? CompatibilityNotice = null,
    int ScenarioCount = 1,
    bool BranchEnumerationComplete = true,
    bool BranchLimitReached = false)
{
    public ActorForecast Local => Actors[LocalPlayerId];
    public bool IsPartial => Warnings.Count != 0 || BranchLimitReached || !BranchEnumerationComplete;
    // Absence of loss in a partial model is NEVER a safety guarantee.
    public bool KnownLethal => BranchEnumerationComplete &&
        Local.MinimumFinalHp == 0 && Local.MaximumFinalHp == 0;
}
