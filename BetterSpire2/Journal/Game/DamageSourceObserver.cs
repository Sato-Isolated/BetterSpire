#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BetterSpire2.Journal.Core;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace BetterSpire2.Journal.Game;

/// <summary>Provides one-shot source provenance for native orb and retaliation damage.</summary>
internal static class DamageSourceObserver
{
    private static readonly OneShotDamageScope<EffectDamageCredit> Scope = new();

    internal static EffectDamageCredit? CurrentCredit => Scope.Current;
    internal static IDisposable EnterCommand() => Scope.EnterCommand();

    internal static Task<IEnumerable<DamageResult>> LightningDamage(PlayerChoiceContext choiceContext,
        IEnumerable<Creature> targets, decimal amount, ValueProp props, Creature dealer,
        LightningOrb orb, bool isEvoke)
    {
        string sourceId = isEvoke ? "effect:lightning-evoke" : "effect:lightning-passive";
        using var pending = Request(() => new EffectDamageCredit(sourceId,
            isEvoke ? T("Lightning orb (evoke)", "Orbe de foudre (évocation)") :
                T("Lightning orb (passive)", "Orbe de foudre (passif)"), HistoryReader.PlayerId(orb.Owner.Creature)),
            "damage-source:lightning", "Lightning");
        return CreatureCmd.Damage(choiceContext, targets, amount, props, dealer);
    }

    internal static Task<IEnumerable<DamageResult>> DarkOrbEvokeDamage(PlayerChoiceContext choiceContext,
        Creature target, decimal amount, ValueProp props, Creature dealer, DarkOrb orb)
    {
        using var pending = Request(() => new EffectDamageCredit("effect:dark-orb-evoke",
            T("Dark orb (evoke)", "Orbe sombre (évocation)"), HistoryReader.PlayerId(orb.Owner.Creature)),
            "damage-source:dark-orb", "DarkOrb");
        return CreatureCmd.Damage(choiceContext, target, amount, props, dealer);
    }

    internal static Task<IEnumerable<DamageResult>> ThornsDamage(PlayerChoiceContext choiceContext,
        Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource,
        CardPlay? cardPlay, ThornsPower power)
    {
        using var pending = Request(() => new EffectDamageCredit("effect:thorns",
            PowerName(power, T("Thorns", "Épines")), HistoryReader.PlayerId(power.Owner)),
            "damage-source:thorns", "Thorns");
        return CreatureCmd.Damage(choiceContext, target, amount, props, dealer, cardSource, cardPlay);
    }

    internal static Task<IEnumerable<DamageResult>> FlameBarrierDamage(PlayerChoiceContext choiceContext,
        Creature target, decimal amount, ValueProp props, Creature dealer, FlameBarrierPower power)
    {
        using var pending = Request(() => new EffectDamageCredit("effect:flame-barrier",
            PowerName(power, T("Flame Barrier", "Barrière de flammes")), HistoryReader.PlayerId(power.Owner)),
            "damage-source:flame-barrier", "FlameBarrier");
        return CreatureCmd.Damage(choiceContext, target, amount, props, dealer);
    }

    internal static Task<IEnumerable<DamageResult>> ExplosiveArrowsDamage(PlayerChoiceContext choiceContext,
        IEnumerable<Creature> targets, decimal amount, ValueProp props, Creature dealer, ExplosiveArrowsPower power)
    {
        using var pending = Request(() => new EffectDamageCredit("effect:explosive-arrows",
            PowerName(power, T("Explosive Arrows", "Flèches explosives")), HistoryReader.PlayerId(power.Owner)),
            "damage-source:explosive-arrows", "ExplosiveArrows");
        return CreatureCmd.Damage(choiceContext, targets, amount, props, dealer);
    }

    internal static Task<IEnumerable<DamageResult>> ViolentChordDamage(PlayerChoiceContext choiceContext,
        IEnumerable<Creature> targets, decimal amount, ValueProp props, Creature dealer, ViolentChordPower power)
    {
        using var pending = Request(() => new EffectDamageCredit("effect:violent-chord",
            PowerName(power, T("Violent Chord", "Accord violent")), HistoryReader.PlayerId(power.Owner)),
            "damage-source:violent-chord", "ViolentChord");
        return CreatureCmd.Damage(choiceContext, targets, amount, props, dealer);
    }

    private static IDisposable? Request(Func<EffectDamageCredit> capture, string capability, string context)
    {
        try { return Scope.RequestNext(capture()); }
        catch (Exception ex)
        {
            try { DamageObservationCoverage.Require(capability, false); } catch { }
            ModLog.Error("Journal." + context + "Source", ex);
            return null;
        }
    }

    private static string PowerName(PowerModel power, string fallback)
    {
        try
        {
            string name = power.Title.GetFormattedText();
            return string.IsNullOrWhiteSpace(name) ? fallback : name;
        }
        catch { return fallback; }
    }

    private static string T(string english, string french) => ModText.IsFrench ? french : english;
}
