#nullable enable
using System;
using System.Collections.Generic;

namespace BetterSpire2.Guardian.Core;

// No Godot, game objects, reflection, commands, tasks, RNG or global state in this layer.
public enum ForecastEventKind { Damage, Block, Heal, AttackMultiplier, ClearBlock }
public enum HpRuleKind { Reduction, Cap, Slippery, Buffer, TurnLimit }
public sealed record HpRule(HpRuleKind Kind, decimal Value, int Charges = 0, string Source = "");

public sealed record ActorSnapshot(
    string Id, string Name, int Hp, int MaxHp, int Block,
    string? SharedBlockOwner = null, string? RedirectPoweredAttacksTo = null,
    IReadOnlyList<HpRule>? HpRules = null, decimal DamageAlreadyReceived = 0,
    int ReviveHp = 0, bool IsPlayer = false);

/// <summary>Amount for Damage is modified damage BEFORE caps and integer truncation.</summary>
public sealed record ForecastEvent(
    ForecastEventKind Kind, string Target, string Source, decimal Amount,
    bool BypassesBlock = false, bool PoweredAttack = false,
    decimal DamageCap = decimal.MaxValue, string? Attacker = null,
    bool RequireLivingAttacker = false, string Phase = "",
    // A dead poison target cannot take more poison; a redirected hit can still overflow.
    bool StopWhenNoEnemies = false);

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
    int BuffersUsed, int Prevented, string? FirstLethalSource, bool IsLocalPet = false);

public sealed record ForecastResult(
    IReadOnlyDictionary<string, ActorForecast> Actors,
    IReadOnlyList<ForecastStep> Steps,
    IReadOnlyList<string> Warnings,
    string LocalPlayerId)
{
    public ActorForecast Local => Actors[LocalPlayerId];
    public bool IsPartial => Warnings.Count != 0;
    // Absence of loss in a partial model is NEVER a safety guarantee.
    public bool KnownLethal => Local.FinalHp == 0;
}
