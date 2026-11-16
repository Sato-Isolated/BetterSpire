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
        "ShouldTakeExtraTurn", "ShouldTakeExtraTurnEarly", "AfterTakingExtraTurn",
        "ShouldDie", "ShouldDieLate", "ModifyUnblockedDamageTarget",
        "ModifyHpLostBeforeOsty", "ModifyHpLostBeforeOstyLate",
        "ModifyHpLostAfterOsty", "ModifyHpLostAfterOstyLate",
        "BeforeFlush", "BeforeFlushLate", "AfterFlush", "AfterCardExhausted",
        "BeforeCardAutoPlayed", "AfterPlayerTurnStart", "BeforePlayerTurnStart",
        "AfterCreatureAdded", "BeforeCreatureRemoved", "AfterCreatureRemoved"
    };
    // One line per native model: the hooks it overrides that are either simulated by the forecast
    // builders or proven to have no HP/block effect between the local end turn and the enemy turn end.
    // Anything a model overrides beyond its listed hooks keeps the forecast uncertain.
    private static readonly Dictionary<string, HashSet<string>> Supported = new(StringComparer.Ordinal)
    {
        // Simulated: end-turn block, healing, hand and relic effects.
        ["Orichalcum"] = Set("BeforeSideTurnEndVeryEarly", "BeforeSideTurnEnd", "BeforeSideTurnStart"),
        ["FakeOrichalcum"] = Set("BeforeSideTurnEndVeryEarly", "BeforeSideTurnEnd", "BeforeSideTurnStart"),
        ["PlatingPower"] = Set("BeforeSideTurnEndEarly"),
        ["RegenPower"] = Set("BeforeSideTurnEndEarly"),
        ["CloakClasp"] = Set("BeforeSideTurnEnd"),
        ["RippleBasin"] = Set("BeforeSideTurnEnd"),
        ["Regret"] = Set("BeforeSideTurnEnd"),
        ["ObsidianVice"] = Set("BeforeSideTurnEnd"), // v0.113 random-target hit, x hand size
        // Simulated: damage over time, Doom and late self-damage.
        ["PoisonPower"] = Set("AfterSideTurnStart"),
        ["DoomPower"] = Set("BeforeSideTurnEnd", "AfterSideTurnEnd"),
        ["DisintegrationPower"] = Set("AfterSideTurnEndLate"),
        ["InsatiableDominionPower"] = Set("AfterSideTurnEndLate", "AfterSideTurnStart", "AfterApplied"), // v0.113 Dread
        // Simulated: retaliation, redirection, revival and HP-loss rules.
        ["ThornsPower"] = Set("BeforeDamageReceived"),
        ["FlameBarrierPower"] = Set("AfterDamageReceived"),
        ["DieForYouPower"] = Set("ModifyUnblockedDamageTarget"),
        ["WuppysLoyaltyPower"] = Set("ModifyPreBlockDamageTarget"),
        ["Wuppy"] = Set("AfterCurrentHpChanged"),
        ["LizardTail"] = Set("ShouldDieLate"),
        ["BufferPower"] = Set("ModifyHpLostAfterOstyLate"),
        ["TungstenRod"] = Set("ModifyHpLostAfterOsty"),
        ["BeatingRemnant"] = Set("ModifyHpLostAfterOsty", "AfterDamageReceived", "BeforeSideTurnStart"),
        ["SlipperyPower"] = Set("ModifyHpLostAfterOsty", "AfterDamageReceived"),
        ["IntangiblePower"] = Set("ModifyHpLostAfterOsty", "AfterSideTurnEnd"),
        ["HibernatePower"] = Set("AfterPlayerTurnStart"), // Frost passives already copy block to teammates
        ["DiamondDiadem"] = Set("AfterSideTurnStart"),

        // Expire or trigger only once the enemy turn has ended; their modifiers are read natively.
        ["WeakPower"] = Set("AfterSideTurnEnd"),
        ["VulnerablePower"] = Set("AfterSideTurnEnd"),
        ["FrailPower"] = Set("AfterSideTurnEnd"),
        ["TaintedPower"] = Set("AfterSideTurnEnd"),
        ["ConstrictPower"] = Set("AfterSideTurnEnd"),
        ["MagicBombPower"] = Set("AfterSideTurnEnd"),
        ["DemisePower"] = Set("AfterSideTurnEnd"),
        ["TemporaryThornsPower"] = Set("AfterSideTurnEnd", "BeforeApplied", "AfterPowerAmountChanged"),

        // Fool (v0.113). Arrows are shot/impaled only from card OnPlay (pinned by an IL contract);
        // Impaled reacts only to its applier's powered attacks, and window player damage is Unpowered.
        // Twine (block on dislodge), SimpletonPower (auto-play) and VigorPower (enemy-owned first-hit
        // bonus) stay uncertain. AfterDeath support does not bypass HasUnresolvedDeathReaction.
        ["ImpaledPower"] = Set("AfterApplied", "AfterDamageReceived", "AfterDeath"),
        ["AimPower"] = Set("BeforeAttack", "AfterAttack"),
        ["QuietusPower"] = Set("BeforeAttack", "AfterAttack", "AfterArrowShot", "AfterSideTurnEnd"),
        ["WhistlingArrows"] = Set("AfterArrowShot"),
        ["CoveringFirePower"] = Set("AfterArrowShot"),
        ["ExplosiveArrowsPower"] = Set("AfterArrowImpaled"),
        ["YahPower"] = Set("AfterArrowDislodged", "BeforeSideTurnStart"),
        ["EpicRetrieval"] = Set("AfterArrowDislodged"),
        ["SongBow"] = Set("AfterRoomEntered"),
        ["DislikeYouPower"] = Set("AfterSideTurnEnd"), // arrow-only damage bonus, removed at player turn end
        ["TunePower"] = Set("AfterSideTurnEnd"), // grants free card plays only
        ["ShowOffPower"] = Set("AfterSideTurnEnd", "AfterDeath"), // arrow-only Aim
        ["KiVersePower"] = Set("AfterSideTurnEnd", "AfterModifyingCardPlayCount"),
        ["EncorePower"] = Set("AfterSideTurnEnd", "AfterApplied", "AfterCardPlayed", "BeforeCardPlayed"),
        ["RetainHandPower"] = Set("AfterSideTurnEnd"),
        ["CompostPower"] = Set("AfterDeath"), // energy only
        ["LullabyPower"] = Set("AfterApplied", "AfterCardPlayed", "BeforeSideTurnStart"),
        ["ConductorsBaton"] = Set("AfterCardPlayed", "BeforeSideTurnStart"),
        ["ConductorsBatonPower"] = Set("AfterSideTurnStart"),
        ["ArcheryPower"] = Set("AfterSideTurnStart"),
        ["HiddenRootsPower"] = Set("AfterSideTurnStart"),
        ["ExerciseMat"] = Set("AfterSideTurnStart"),
        ["EcstasyPower"] = Set("AfterPlayerTurnStart"),
        ["StretchPower"] = Set("AfterCardPlayed", "BeforeCardPlayed"),
        ["FreeCardPower"] = Set("BeforeCardPlayed"),
        ["ViolentChordPower"] = Set("AfterCardPlayed"),
        ["SongPathPower"] = Set("AfterCardPlayed"),
        ["MotifPower"] = Set("AfterCardPlayed"),
        ["LostLutePower"] = Set("AfterCardPlayed"),
        ["EnsemblePower"] = Set("AfterCardPlayed"),
        ["BizarreLamp"] = Set("BeforeHandDraw"),
        ["Ocarina"] = Set("BeforeHandDraw"),
        ["Bounce"] = Set("BeforeHandDraw"),
        ["MythicFormPower"] = Set("BeforeHandDraw"),
        ["EnnuiPower"] = Set("BeforeHandDraw"),
        ["AllIsWellPower"] = Set("AfterCardGeneratedForCombat"), // only player-created Sadness
        ["Fertilizer"] = Set("AfterCardGeneratedForCombat"),
        ["WhittlingKnife"] = Set("AfterShuffle"),

        // Other v0.113 content: combat entry, player-turn start, card or draw time, or no HP/block effect.
        ["Storyteller"] = Set("AfterRoomEntered", "BeforeSideTurnEnd"), // arrow-only Aim
        ["BathRobes"] = Set("AfterRoomEntered", "BeforeCombatStart"),
        ["Byrdine"] = Set("BeforeCombatStart", "AfterObtained"),
        ["BathTowel"] = Set("AfterPlayerTurnStartLate"),
        ["TormentousCollar"] = Set("BeforeSideTurnStart"),
        ["AeonglassDominionPower"] = Set("BeforeHandDraw"),
        ["AmnesiaPower"] = Set("AfterPlayerTurnStart", "AfterSideTurnEnd"),
        ["GodByrdPower"] = Set("AfterPlayerTurnStart"),
        ["YouAreMinePower"] = Set("AfterPlayerTurnStart"),
        ["LagavulinMatriarchDominionPower"] = Set("AfterSideTurnEnd"), // Str/Dex loss after end-turn block
        ["MightyWings"] = Set("AfterDamageGiven"), // Strength from player powered attacks
        ["Thumbscrew"] = Set("ModifyAttackHitCount") // owner's attack cards only
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
        Supported.TryGetValue(type, out var hooks) && hooks.Contains(hook) ||
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
