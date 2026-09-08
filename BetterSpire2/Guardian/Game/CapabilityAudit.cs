#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BetterSpire2.Guardian.Core;
using BetterSpire2.Runtime.Native;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;

namespace BetterSpire2.Guardian.Game;

/// <summary>
/// Closed-world coverage guard. Unknown state-changing callbacks downgrade the result.
/// A new game power/relic or another mod cannot silently receive a "safe" verdict.
/// Reflection is cached per concrete type and used for inspection, never invocation.
/// </summary>
internal static class CapabilityAudit
{
    private static readonly HashSet<string> TrackedHooks = new(StringComparer.Ordinal)
    {
        "BeforeSideTurnEndVeryEarly", "BeforeSideTurnEndEarly", "BeforeSideTurnEnd",
        "AfterSideTurnEnd", "AfterSideTurnEndLate", "BeforeSideTurnStart", "AfterSideTurnStart",
        "BeforeDamageReceived", "AfterDamageReceived", "AfterCurrentHpChanged",
        "BeforeAttack", "AfterAttack", "ModifyAttackHitCount",
        "BeforeDamageGiven", "AfterDamageGiven", "AfterDiedToDoom",
        "BeforeBlockGained", "AfterBlockGained", "AfterBlockBroken", "AfterBlockCleared",
        "BeforeDeath", "AfterDeath", "AfterAutoPostPlayPhaseEntered",
        "ShouldTakeExtraTurn", "AfterTakingExtraTurn",
        "ShouldDie", "ShouldDieLate", "ModifyUnblockedDamageTarget",
        "ModifyHpLostBeforeOsty", "ModifyHpLostBeforeOstyLate",
        "ModifyHpLostAfterOsty", "ModifyHpLostAfterOstyLate",
        "BeforeFlush", "BeforeFlushLate", "AfterFlush", "AfterCardExhausted",
        "BeforeCardAutoPlayed", "AfterPlayerTurnStart", "BeforePlayerTurnStart",
        "AfterCreatureAdded", "BeforeCreatureRemoved", "AfterCreatureRemoved"
    };
    private static readonly Dictionary<string, HashSet<string>> Supported = new(StringComparer.Ordinal)
    {
        ["BeforeSideTurnEndVeryEarly"] = Set("Orichalcum", "FakeOrichalcum"),
        ["BeforeSideTurnEndEarly"] = Set("PlatingPower", "RegenPower"),
        ["BeforeSideTurnEnd"] = Set("Orichalcum", "FakeOrichalcum", "CloakClasp", "RippleBasin", "Regret"),
        ["AfterSideTurnEnd"] = Set("ConstrictPower", "MagicBombPower", "DemisePower",
            "WeakPower", "VulnerablePower", "FrailPower", "IntangiblePower",
            // Its additive damage is already captured by Hook.ModifyDamage. Removal happens
            // after the enemy turn, once every attack included in this forecast has resolved.
            "TaintedPower"),
        ["AfterSideTurnEndLate"] = Set("DisintegrationPower"),
        ["BeforeSideTurnStart"] = Set("BeatingRemnant", "Orichalcum", "FakeOrichalcum"),
        ["AfterSideTurnStart"] = Set("PoisonPower", "DiamondDiadem"),
        ["AfterPlayerTurnStart"] = Set("HibernatePower"),
        ["AfterDamageReceived"] = Set("BeatingRemnant", "SlipperyPower"),
        ["ShouldDieLate"] = Set("LizardTail"),
        ["ModifyUnblockedDamageTarget"] = Set("DieForYouPower"),
        ["ModifyHpLostAfterOsty"] = Set("BeatingRemnant", "TungstenRod", "IntangiblePower", "SlipperyPower"),
        ["ModifyHpLostAfterOstyLate"] = Set("BufferPower")
    };
    private static readonly HashSet<string> SafeAfterModification = Set(
        "TungstenRod", "BeatingRemnant", "IntangiblePower", "HardToKillPower", "BufferPower");
    private static readonly Dictionary<Type, string[]> UnknownByType = new();
    private static readonly Dictionary<(Type, string), bool> OverrideCache = new();

    internal static void Inspect(IEnumerable<AbstractModel> models, Action<string> warn)
    {
        foreach (var model in models)
        {
            var type = model.GetType();
            if (type == typeof(BetterSpireCombatObserver)) continue;
            if (!IsBaseGame(type, out string owner))
            {
                warn((ModText.IsFrench ? "Mod externe non certifié : " : "Uncertified external mod: ") +
                    owner + " / " + type.Name);
                continue;
            }
            if (!UnknownByType.TryGetValue(type, out var unknown))
            {
                unknown = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Where(m => m.DeclaringType != typeof(AbstractModel) &&
                        m.GetBaseDefinition().DeclaringType == typeof(AbstractModel))
                    .Where(m => TrackedHooks.Contains(m.Name) || ForecastHookPolicy.IsReaction(m.Name))
                    .Where(m => !IsSupported(type.Name, m.Name) &&
                        !ForecastHookPolicy.IsOutsideForecastWindow(type.Name, m.Name))
                    .Select(m => m.Name).Distinct().ToArray();
                UnknownByType[type] = unknown;
            }
            if (unknown.Length > 0)
                warn(GameForecastAdapter.Source(model) + (ModText.IsFrench
                    ? " : interaction non simulée (" : ": unsimulated interaction (") + unknown[0] + ")");
        }
    }

    internal static bool HasUnresolvedDeathReaction(IEnumerable<AbstractModel> models, Creature target)
    {
        // Cross-creature reactions may revive, summon or retaliate: inspect ALL listeners.
        foreach (var model in models)
        {
            var type = model.GetType();
            if (type == typeof(BetterSpireCombatObserver)) continue;
            if (!IsBaseGame(type, out _) ||
                Overrides(type, "AfterDeath") || Overrides(type, "BeforeDeath") ||
                Overrides(type, "ShouldDie") || Overrides(type, "ShouldDieLate")) return true;
        }
        return false;
    }

    private static bool Overrides(Type type, string name)
    {
        var key = (type, name);
        if (OverrideCache.TryGetValue(key, out var result)) return result;
        result = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Any(m => m.Name == name && m.DeclaringType != typeof(AbstractModel) &&
                m.GetBaseDefinition().DeclaringType == typeof(AbstractModel));
        OverrideCache[key] = result;
        return result;
    }
    private static bool IsSupported(string type, string hook) =>
        Supported.TryGetValue(hook, out var types) && types.Contains(type) ||
        hook.StartsWith("AfterModifying", StringComparison.Ordinal) && SafeAfterModification.Contains(type);

    private static bool IsBaseGame(Type type, out string owner)
    {
        owner = type.Assembly.GetName().Name ?? "unknown";
        try
        {
            var mod = AssemblyInfo.ModForType(type, out bool isBaseGame);
            if (isBaseGame) return true;
            owner = mod?.manifest?.name ?? mod?.manifest?.id ?? owner;
            return false;
        }
        catch
        {
            // AssemblyInfo is initialized by the game, but pure tests and early startup may not have its map yet.
            return type.Assembly == typeof(AbstractModel).Assembly;
        }
    }
    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);
}
