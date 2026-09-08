#nullable enable
using System;
using System.Linq;
using BetterSpire2.Guardian.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.ValueProps;

namespace BetterSpire2.Guardian.Game;

internal sealed partial class GameForecastAdapter
{
    private void BuildPlayerEndTurn()
    {
        string early = T("End turn / early", "Fin de tour / anticipé");
        string before = T("End turn / relics", "Fin de tour / reliques");
        string handPhase = T("End turn / hand", "Fin de tour / main");
        string after = T("End turn / powers", "Fin de tour / pouvoirs");
        // VeryEarly only latches Orichalcum; Plating runs in Early, then latches fire.
        // Both versions of Orichalcum evaluate the same pre-Early block, not each other's gain.
        var orichalcum = _combatListeners.OfType<RelicModel>()
            .Where(r => (r is Orichalcum || r is FakeOrichalcum) && r.Owner.Creature.Block <= 0)
            .ToHashSet();
        foreach (var model in _combatListeners)
            if (model is PlatingPower plating && plating.Owner.Side == CombatSide.Player)
                Block(plating.Owner, plating.Amount, ValueProp.Unpowered, Source(plating), early);
        foreach (var model in _combatListeners)
        {
            if (model is not RelicModel relic || relic.Owner.Creature.Side != CombatSide.Player) continue;
            switch (relic)
            {
                case Orichalcum or FakeOrichalcum when orichalcum.Contains(relic):
                    RelicBlock(relic, 1, before); break;
                case CloakClasp:
                    RelicBlock(relic, relic.Owner.PlayerCombatState?.Hand.Cards.Count ?? 0, before); break;
                case RippleBasin:
                    bool attacked = CombatManager.Instance.History.CardPlaysFinished.Any(entry =>
                        entry.HappenedThisTurn(_state) && entry.CardPlay.Card.Owner == relic.Owner &&
                        entry.CardPlay.Card.Type == CardType.Attack);
                    if (!attacked) RelicBlock(relic, 1, before);
                    break;
                case DiamondDiadem diadem:
                    if (diadem.DisplayAmount <= Var(diadem.DynamicVars, "CardThreshold") &&
                        diadem.Owner.Creature.GetPower<DiamondDiademPower>() == null)
                        _events.Add(new(ForecastEventKind.AttackMultiplier, Id(diadem.Owner.Creature),
                            Source(diadem), .5m, Phase: before));
                    break;
            }
        }
        // Per-player DoTurnEnd: orbs -> ethereal exhaustion -> the snapshotted hand effects.
        foreach (var player in _state.Players)
        {
            var combat = player.PlayerCombatState;
            if (combat == null || !player.Creature.IsAlive) continue;
            if (combat.OrbQueue != null)
                foreach (var orb in combat.OrbQueue.Orbs)
                {
                    if (orb is FrostOrb frost)
                    {
                        int count = Hook.ModifyOrbPassiveTriggerCount(_state, orb, 1, out _);
                        if (count < 0 || count > 1000) throw new InvalidOperationException("Invalid orb trigger count.");
                        for (int i = 0; i < count; i++)
                            Block(player.Creature, frost.PassiveVal, ValueProp.Unpowered,
                                T("Frost orb", "Orbe de givre"), T("End turn / orbs", "Fin de tour / orbes"));
                    }
                    else if (orb.GetType().Name is not ("DarkOrb" or "PlasmaOrb"))
                        Warn(T("Offensive/custom orb: enemy deaths are not fully simulated.",
                            "Orbe offensif/personnalisé : morts ennemies non entièrement simulées."));
                }
            var cards = combat.Hand.Cards.ToArray();
            if (cards.Any(card => card.Keywords.Contains(CardKeyword.Ethereal)))
                Warn(T("Ethereal exhaust triggers may change the hand or block.",
                    "Les déclenchements d'épuisement Éthéré peuvent modifier la main ou le blocage."));
            if (cards.Any(card => card.Keywords.Contains(CardKeyword.Sly)))
                Warn(T("Sly auto-play at end turn is not simulated.",
                    "Le jeu automatique des cartes Sly en fin de tour n'est pas simulé."));
            foreach (var card in cards)
            {
                if (!card.HasTurnEndInHandEffect) continue;
                switch (card.GetType().Name)
                {
                    case "Burn":
                    case "Decay":
                    case "Infection":
                    case "Toxic":
                    case "Wither":
                        Damage(player.Creature, card.DynamicVars.Damage.BaseValue,
                            card.DynamicVars.Damage.Props, Source(card), handPhase, card: card);
                        break;
                    case "BadLuck":
                    case "Beckon":
                        Damage(player.Creature, card.DynamicVars.HpLoss.BaseValue,
                            ValueProp.Move | ValueProp.Unpowered | ValueProp.Unblockable,
                            Source(card), handPhase, card: card);
                        break;
                    case "Regret":
                        Damage(player.Creature, cards.Length,
                            ValueProp.Move | ValueProp.Unpowered | ValueProp.Unblockable,
                            Source(card), handPhase, card: card);
                        break;
                    case "Doubt":
                    case "Shame":
                        // Weak/Frail added here do not alter the already-triggered unpowered block.
                        break;
                    case "Debt":
                        // Gold loss does not affect the survival forecast.
                        break;
                    default:
                        Warn(T("Unsimulated hand effect: ", "Effet de main non simulé : ") + Source(card));
                        break;
                }
            }
        }
        // Do NOT regroup damage before/after healing: listener order is gameplay order.
        foreach (var model in _combatListeners)
        {
            if (model is not PowerModel power || power.Owner.Side != CombatSide.Player || power.Amount <= 0) continue;
            switch (power)
            {
                case RegenPower:
                    _events.Add(new(ForecastEventKind.Heal, Id(power.Owner), Source(power), power.Amount, Phase: after));
                    break;
                case ConstrictPower:
                    Damage(power.Owner, power.Amount, ValueProp.Unpowered, Source(power), after, power.Owner);
                    break;
                case MagicBombPower bomb when bomb.Applier != null && bomb.Applier.IsAlive:
                    Damage(bomb.Owner, bomb.Amount, ValueProp.Unpowered, Source(bomb), after, bomb.Owner);
                    break;
                case DemisePower:
                    Damage(power.Owner, power.Amount, ValueProp.Unpowered | ValueProp.Unblockable,
                        Source(power), after);
                    break;
            }
        }
        foreach (var model in _combatListeners)
            if (model is DisintegrationPower power && power.Owner.Side == CombatSide.Player)
                Damage(power.Owner, power.Amount, ValueProp.Unpowered, Source(power),
                    T("End turn / late", "Fin de tour / tardif"), power.Owner);
    }
}
