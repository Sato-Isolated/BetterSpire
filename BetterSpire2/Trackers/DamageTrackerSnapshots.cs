#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace BetterSpire2.Trackers;

public static partial class DamageTracker
{
	private static int BuildSnapshotHash(CombatContext context)
	{
		HashCode hashCode = new HashCode();
		hashCode.Add(ModSettings.PlayerDamageTotal);
		hashCode.Add(ModSettings.ShowExpectedHp);
		hashCode.Add(RuntimeHelpers.GetHashCode(context.State));
		hashCode.Add(context.ActiveEnemies.Count);
		hashCode.Add(context.PlayerCreatures.Count);
		hashCode.Add((int)context.LocalAttackMultiplier.Numerator);
		hashCode.Add((int)context.LocalAttackMultiplier.Denominator);

		AddPlayerSnapshots(ref hashCode, context);
		AddEnemySnapshots(ref hashCode, context.ActiveEnemies);
		return hashCode.ToHashCode();
	}

	private static void AddPlayerSnapshots(ref HashCode hashCode, CombatContext context)
	{
		foreach (Creature playerCreature in context.PlayerCreatures)
		{
			if (!IsTrackedPlayerCreature(playerCreature))
			{
				continue;
			}

			AddCreatureHash(ref hashCode, playerCreature);
			DamageMultiplier attackMultiplier = GetAttackMultiplier(playerCreature, context.Intercepts);
			hashCode.Add((int)attackMultiplier.Numerator);
			hashCode.Add((int)attackMultiplier.Denominator);
			hashCode.Add(GetPowerAmount<ConstrictPower>(playerCreature));
			hashCode.Add(GetPowerAmount<DemisePower>(playerCreature));
			hashCode.Add(GetPowerAmount<DisintegrationPower>(playerCreature));
			hashCode.Add(GetPowerAmount<PlatingPower>(playerCreature));
			hashCode.Add(GetPowerAmount<RegenPower>(playerCreature));
			hashCode.Add(GetMagicBombSnapshot(playerCreature));
			hashCode.Add(playerCreature.GetPower<IntangiblePower>() != null);
			hashCode.Add(HasRelic<TungstenRod>(playerCreature));
			hashCode.Add(GetDiamondDiademSnapshot(playerCreature));
			hashCode.Add(GetCloakClaspSnapshot(playerCreature));
			hashCode.Add(GetFrostOrbSnapshot(playerCreature));
			hashCode.Add(GetHandTurnEndSnapshot(playerCreature));
			hashCode.Add(DidPlayerPlayAttackCardThisTurn(context, playerCreature.Player));

			if (context.Intercepts.CoveredByInterceptor.TryGetValue(playerCreature, out Creature interceptor))
			{
				hashCode.Add(RuntimeHelpers.GetHashCode(interceptor));
			}

			Creature? livingPet = GetLivingPet(playerCreature);
			if (livingPet != null)
			{
				AddCreatureHash(ref hashCode, livingPet);
				hashCode.Add(GetPowerAmount<ConstrictPower>(livingPet));
				hashCode.Add(GetPowerAmount<DemisePower>(livingPet));
				hashCode.Add(GetPowerAmount<DisintegrationPower>(livingPet));
				hashCode.Add(GetMagicBombSnapshot(livingPet));
			}
		}
	}

	private static void AddEnemySnapshots(ref HashCode hashCode, IReadOnlyList<Creature> activeEnemies)
	{
		foreach (Creature activeEnemy in activeEnemies)
		{
			AddCreatureHash(ref hashCode, activeEnemy);
			hashCode.Add(GetPowerAmount<PoisonPower>(activeEnemy));
			hashCode.Add(GetPowerAmount<HardToKillPower>(activeEnemy));
			if (activeEnemy.Monster?.NextMove?.Intents == null)
			{
				continue;
			}

			foreach (AbstractIntent intent in activeEnemy.Monster.NextMove.Intents)
			{
				hashCode.Add(intent.GetType().FullName);
				if (intent is AttackIntent attackIntent)
				{
					hashCode.Add(attackIntent.Repeats);
					hashCode.Add(attackIntent.GetSingleDamage(Array.Empty<Creature>(), activeEnemy));
				}
			}
		}
	}

	private static int GetMagicBombSnapshot(Creature creature)
	{
		HashCode hashCode = new HashCode();
		foreach (MagicBombPower powerInstance in creature.GetPowerInstances<MagicBombPower>())
		{
			hashCode.Add(powerInstance.Amount);
			hashCode.Add(powerInstance.Applier != null ? RuntimeHelpers.GetHashCode(powerInstance.Applier) : 0);
		}
		return hashCode.ToHashCode();
	}

	private static int GetDiamondDiademSnapshot(Creature player)
	{
		DiamondDiadem? relic = GetRelic<DiamondDiadem>(player);
		if (relic == null)
		{
			return -1;
		}

		HashCode hashCode = new HashCode();
		hashCode.Add(relic.DisplayAmount);
		if (relic.DynamicVars.TryGetValue("CardThreshold", out DynamicVar value))
		{
			hashCode.Add((int)value.BaseValue);
		}
		return hashCode.ToHashCode();
	}

	private static int GetCloakClaspSnapshot(Creature player)
	{
		CloakClasp? relic = GetRelic<CloakClasp>(player);
		if (relic == null)
		{
			return -1;
		}

		HashCode hashCode = new HashCode();
		hashCode.Add((player.Player?.PlayerCombatState?.Hand?.Cards?.Count).GetValueOrDefault());
		if (relic.DynamicVars.TryGetValue("Block", out DynamicVar value))
		{
			hashCode.Add((int)value.BaseValue);
		}
		return hashCode.ToHashCode();
	}

	private static int GetFrostOrbSnapshot(Creature player)
	{
		HashCode hashCode = new HashCode();
		hashCode.Add(HasRelic<GoldPlatedCables>(player));
		IReadOnlyList<OrbModel>? orbs = player.Player?.PlayerCombatState?.OrbQueue?.Orbs;
		if (orbs == null)
		{
			return hashCode.ToHashCode();
		}

		foreach (OrbModel orb in orbs)
		{
			hashCode.Add(orb.GetType().FullName);
			if (orb is FrostOrb frostOrb)
			{
				hashCode.Add((int)frostOrb.PassiveVal);
				hashCode.Add(GetOrbPassiveTriggerCount(player.CombatState, orb));
			}
		}

		return hashCode.ToHashCode();
	}

	private static int GetHandTurnEndSnapshot(Creature player)
	{
		HashCode hashCode = new HashCode();
		IReadOnlyList<CardModel>? cards = player.Player?.PlayerCombatState?.Hand?.Cards;
		if (cards == null)
		{
			return 0;
		}

		foreach (CardModel card in cards)
		{
			if (!card.HasTurnEndInHandEffect)
			{
				continue;
			}

			hashCode.Add(RuntimeHelpers.GetHashCode(card));
			if (TryGetDynamicBaseValue(card.DynamicVars, "Damage", out int damage))
			{
				hashCode.Add(damage);
			}
			if (TryGetDynamicBaseValue(card.DynamicVars, "HpLoss", out int hpLoss))
			{
				hashCode.Add(hpLoss);
			}
		}

		return hashCode.ToHashCode();
	}

	private static void AddCreatureHash(ref HashCode hashCode, Creature creature)
	{
		hashCode.Add(RuntimeHelpers.GetHashCode(creature));
		hashCode.Add(creature.IsDead);
		hashCode.Add(creature.CurrentHp);
		hashCode.Add(creature.MaxHp);
		hashCode.Add(creature.Block);
	}
}