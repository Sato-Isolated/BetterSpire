#nullable enable
using System;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.ValueProps;

namespace BetterSpire2.Guardian.Game;

internal sealed partial class GameForecastAdapter
{
    private void BuildEnemyTurn()
    {
        // AfterSideTurnStart iterates hook listeners. Poison bypasses block and decreases per tick.
        foreach (var poison in _combatListeners.OfType<PoisonPower>())
        {
            if (poison.Owner.Side != CombatSide.Enemy || !poison.Owner.IsAlive) continue;
            if (CapabilityAudit.HasUnresolvedDeathReaction(_runListeners, poison.Owner))
            {
                Warn(T("Poison kill not credited: death/prevention reaction present.",
                    "Mort par poison non déduite : réaction de mort ou prévention présente."));
                continue; // Retain that enemy's attacks rather than falsely promising safety.
            }
            int triggers = ReadRequired<int>(poison, "TriggerCount");
            if (triggers < 0 || triggers > 1000) throw new InvalidOperationException("Invalid poison trigger count.");
            for (int i = 0; i < triggers && poison.Amount - i > 0; i++)
                Damage(poison.Owner, poison.Amount - i, ValueProp.Unpowered | ValueProp.Unblockable,
                    Source(poison), T("Enemy turn / poison", "Tour ennemi / poison"));
        }
        // Verified ExecuteEnemyTurn iterates a ToList snapshot of state.Enemies in this order.
        foreach (var enemy in _state.Enemies)
        {
            if (!enemy.IsAlive || enemy.Monster == null) continue;
            var intents = enemy.Monster.NextMove?.Intents;
            if (intents == null)
            {
                Warn(T("Missing enemy intent: ", "Intention ennemie absente : ") + enemy.Name);
                continue;
            }
            foreach (var intent in intents)
            {
                if (intent is not AttackIntent attack)
                {
                    if (intent is not (SleepIntent or StunIntent))
                        Warn(enemy.Name + T(" : non-attack intent may change the outcome.",
                            " : intention secondaire pouvant modifier le résultat."));
                    continue;
                }
                // DamageCalc is public in the supplied DLL. GetSingleDamage ignores its target
                // argument and uses LocalContext, so NEVER use it for a per-target forecast.
                // Missing damage data must invalidate the forecast, never become zero damage.
                var damageCalc = attack.DamageCalc
                    ?? throw new InvalidOperationException("Missing attack damage calculator.");
                decimal raw = damageCalc();
                int repeats = attack is SingleAttackIntent ? 1 : attack.Repeats;
                if (repeats < 0 || repeats > 1000) throw new InvalidOperationException("Invalid attack repeat count.");
                if (attack is not (SingleAttackIntent or MultiAttackIntent))
                    Warn(T("Custom attack intent: targeting/order not certified.",
                        "Intention d'attaque personnalisée : ciblage/ordre non certifié."));
                // AttackCommand issues one grouped Damage command per hit, after checking
                // whether the attacker survived the previous command.
                foreach (var hit in ForecastTurnOrder.AttackHits(_state.Players, repeats)
                    .GroupBy(entry => entry.HitIndex))
                {
                    var targets = hit.Select(entry => entry.Target.Creature)
                        .Where(creature => creature.IsAlive).ToArray();
                    if (targets.Length == 0) continue;
                    DamageCommand(targets, raw, ValueProp.Move,
                        enemy.Name + (repeats > 1 ? $" ({hit.Key + 1}/{repeats})" : ""),
                        T("Enemy attacks", "Attaques ennemies"), enemy, stopOnVictory: true);
                }
            }
        }
        var enemySide = _state.GetCreaturesOnSide(CombatSide.Enemy).ToArray();
        var enemyParticipants = enemySide
            .Where(enemy => enemy.IsAlive).ToArray();
        var enemySet = enemyParticipants.ToHashSet();
        foreach (var regen in _combatListeners.OfType<RegenPower>())
            if (regen.Amount > 0 && enemySet.Contains(regen.Owner))
                _events.Add(new(BetterSpire2.Guardian.Core.ForecastEventKind.Heal, Id(regen.Owner),
                    Source(regen), regen.Amount,
                    Phase: T("Enemy turn / early", "Tour ennemi / début")));
        foreach (var doom in _combatListeners.OfType<DoomPower>())
            if (enemySet.Contains(doom.Owner))
                AddDoomCheck(enemySide, new[] { doom.Owner },
                    T("Enemy turn / before end", "Tour ennemi / avant la fin"));
    }
}
