#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace BetterSpire2.Trackers;

public static partial class DamageTracker
{
	private static List<int> CollectEnemyAttackHits(CombatContext context, Creature player)
	{
		List<int> attackHits = new List<int>();
		if (context.Intercepts.CoveredByInterceptor.ContainsKey(player))
		{
			return attackHits;
		}

		DamageMultiplier attackMultiplier = GetAttackMultiplier(player, context.Intercepts);
		Creature[] targets = new Creature[1] { player };
		foreach (Creature activeEnemy in context.ActiveEnemies)
		{
			if (activeEnemy.Monster?.NextMove?.Intents == null)
			{
				continue;
			}

			foreach (AbstractIntent intent in activeEnemy.Monster.NextMove.Intents)
			{
				if (intent is not AttackIntent attackIntent)
				{
					continue;
				}

				int singleDamage = attackIntent.GetSingleDamage(targets, activeEnemy);
				if (singleDamage <= 0)
				{
					continue;
				}

				singleDamage = attackMultiplier.ScaleFrom(singleDamage, context.LocalAttackMultiplier);
				if (singleDamage <= 0)
				{
					continue;
				}

				int repeats = GetAttackRepeats(attackIntent, activeEnemy, targets, singleDamage);
				for (int index = 0; index < repeats; index++)
				{
					attackHits.Add(singleDamage);
				}
			}
		}

		ApplyDiamondDiademAdjustment(player, attackHits);
		return attackHits;
	}

	private static int GetAttackRepeats(AttackIntent attackIntent, Creature enemy, IEnumerable<Creature> targets, int singleDamage)
	{
		if (attackIntent.Repeats > 0)
		{
			return attackIntent.Repeats;
		}

		if (singleDamage <= 0)
		{
			return 0;
		}

		int totalDamage = attackIntent.GetTotalDamage(targets, enemy);
		if (totalDamage <= 0)
		{
			return 0;
		}

		return Math.Max(totalDamage / singleDamage, 1);
	}

	private static void ApplyDiamondDiademAdjustment(Creature player, List<int> attackHits)
	{
		try
		{
			DiamondDiadem? relic = GetRelic<DiamondDiadem>(player);
			if (relic == null)
			{
				return;
			}

			int cardThreshold = 2;
			if (relic.DynamicVars.TryGetValue("CardThreshold", out DynamicVar value))
			{
				cardThreshold = (int)value.BaseValue;
			}

			if (relic.DisplayAmount > cardThreshold)
			{
				return;
			}

			for (int index = 0; index < attackHits.Count; index++)
			{
				attackHits[index] /= 2;
			}
		}
		catch
		{
		}
	}

	private static List<PendingDamage> CollectSelfDamageSources(Creature player, Creature? pet)
	{
		List<PendingDamage> damages = new List<PendingDamage>();
		foreach (Creature target in EnumerateSelfDamageTargets(player, pet))
		{
			if (target.IsDead)
			{
				continue;
			}

			AddPowerDamage(damages, target, GetPowerAmount<ConstrictPower>(target), DamageKind.Blockable);
			AddPowerDamage(damages, target, GetPowerAmount<DemisePower>(target), DamageKind.HpLoss);
			AddMagicBombDamage(damages, target);
			AddPowerDamage(damages, target, GetPowerAmount<DisintegrationPower>(target), DamageKind.Blockable);
		}

		AddTurnEndHandDamage(damages, player);
		return damages;
	}

	private static void AddPowerDamage(List<PendingDamage> damages, Creature target, int amount, DamageKind kind)
	{
		if (amount > 0)
		{
			damages.Add(new PendingDamage(target, amount, kind));
		}
	}

	private static void AddMagicBombDamage(List<PendingDamage> damages, Creature target)
	{
		foreach (MagicBombPower powerInstance in target.GetPowerInstances<MagicBombPower>())
		{
			if (powerInstance.Amount > 0 && powerInstance.Applier != null && !powerInstance.Applier.IsDead)
			{
				damages.Add(new PendingDamage(target, powerInstance.Amount, DamageKind.Blockable));
			}
		}
	}

	private static void AddTurnEndHandDamage(List<PendingDamage> damages, Creature player)
	{
		IReadOnlyList<CardModel>? cards = player.Player?.PlayerCombatState?.Hand?.Cards;
		if (cards == null)
		{
			return;
		}

		foreach (CardModel card in cards)
		{
			if (!card.HasTurnEndInHandEffect)
			{
				continue;
			}

			if (TryGetDynamicBaseValue(card.DynamicVars, "Damage", out int damage) && damage > 0)
			{
				damages.Add(new PendingDamage(player, damage, DamageKind.Blockable));
			}
			else if (TryGetDynamicBaseValue(card.DynamicVars, "HpLoss", out int hpLoss) && hpLoss > 0)
			{
				damages.Add(new PendingDamage(player, hpLoss, DamageKind.HpLoss));
			}
		}
	}

	private static IEnumerable<Creature> EnumerateSelfDamageTargets(Creature player, Creature? pet)
	{
		yield return player;
		if (pet != null && !pet.IsDead)
		{
			yield return pet;
		}
	}

	private static DamageResolution ResolveDamage(Creature player, Creature? pet, bool petVisible, int projectedPlayerBlock, List<PendingDamage> selfDamageSources, List<int> enemyAttackHits)
	{
		int playerDamage = 0;
		int petDamage = 0;
		int playerBlock = projectedPlayerBlock;
		int petHp = petVisible ? pet.CurrentHp : 0;
		int petBlock = petVisible ? pet.Block : 0;
		bool playerHasIntangible = player.GetPower<IntangiblePower>() != null;
		bool playerHasTungsten = HasRelic<TungstenRod>(player);

		ApplySelfDamage(selfDamageSources, playerHasIntangible, playerHasTungsten, ref playerBlock, ref petBlock, ref petHp, ref playerDamage, ref petDamage);
		ApplyIncomingEnemyHits(enemyAttackHits, playerHasIntangible, playerHasTungsten, petVisible, ref playerBlock, ref petHp, ref playerDamage, ref petDamage);
		return new DamageResolution(playerDamage, petDamage);
	}

	private static void ApplySelfDamage(List<PendingDamage> selfDamageSources, bool playerHasIntangible, bool playerHasTungsten, ref int playerBlock, ref int petBlock, ref int petHp, ref int playerDamage, ref int petDamage)
	{
		foreach (PendingDamage selfDamageSource in selfDamageSources)
		{
			if (selfDamageSource.Kind == DamageKind.Blockable)
			{
				ApplyBlockableDamage(selfDamageSource, playerHasIntangible, playerHasTungsten, ref playerBlock, ref petBlock, ref petHp, ref playerDamage, ref petDamage);
			}
		}

		foreach (PendingDamage selfDamageSource in selfDamageSources)
		{
			if (selfDamageSource.Kind == DamageKind.HpLoss)
			{
				ApplyHpLossDamage(selfDamageSource, playerHasIntangible, playerHasTungsten, ref petHp, ref playerDamage, ref petDamage);
			}
		}
	}

	private static void ApplyIncomingEnemyHits(List<int> enemyAttackHits, bool playerHasIntangible, bool playerHasTungsten, bool petVisible, ref int playerBlock, ref int petHp, ref int playerDamage, ref int petDamage)
	{
		foreach (int enemyAttackHit in enemyAttackHits)
		{
			ApplyEnemyAttackHit(enemyAttackHit, playerHasIntangible, playerHasTungsten, petVisible, ref playerBlock, ref petHp, ref playerDamage, ref petDamage);
		}
	}

	private static void ApplyBlockableDamage(PendingDamage damage, bool playerHasIntangible, bool playerHasTungsten, ref int playerBlock, ref int petBlock, ref int petHp, ref int playerDamage, ref int petDamage)
	{
		int adjustedDamage = playerHasIntangible && !damage.Target.IsPet ? Math.Min(damage.Amount, 1) : damage.Amount;
		if (damage.Target.IsPet)
		{
			int blockedDamage = Math.Min(petBlock, adjustedDamage);
			petBlock -= blockedDamage;
			int unblockedDamage = adjustedDamage - blockedDamage;
			petHp -= unblockedDamage;
			petDamage += unblockedDamage;
			return;
		}

		int blockedPlayerDamage = Math.Min(playerBlock, adjustedDamage);
		playerBlock -= blockedPlayerDamage;
		int remainingDamage = adjustedDamage - blockedPlayerDamage;
		if (remainingDamage > 0 && playerHasTungsten)
		{
			remainingDamage = Math.Max(remainingDamage - 1, 0);
		}

		playerDamage += remainingDamage;
	}

	private static void ApplyHpLossDamage(PendingDamage damage, bool playerHasIntangible, bool playerHasTungsten, ref int petHp, ref int playerDamage, ref int petDamage)
	{
		int adjustedDamage = playerHasIntangible && !damage.Target.IsPet ? Math.Min(damage.Amount, 1) : damage.Amount;
		if (damage.Target.IsPet)
		{
			petHp -= adjustedDamage;
			petDamage += adjustedDamage;
			return;
		}

		if (playerHasTungsten && adjustedDamage > 0)
		{
			adjustedDamage = Math.Max(adjustedDamage - 1, 0);
		}

		playerDamage += adjustedDamage;
	}

	private static void ApplyEnemyAttackHit(int attackHit, bool playerHasIntangible, bool playerHasTungsten, bool petVisible, ref int playerBlock, ref int petHp, ref int playerDamage, ref int petDamage)
	{
		int adjustedDamage = playerHasIntangible ? Math.Min(attackHit, 1) : attackHit;
		int blockedDamage = Math.Min(playerBlock, adjustedDamage);
		playerBlock -= blockedDamage;
		adjustedDamage -= blockedDamage;

		if (petVisible && petHp > 0 && adjustedDamage > 0)
		{
			int petAbsorbedDamage = Math.Min(petHp, adjustedDamage);
			petHp -= petAbsorbedDamage;
			petDamage += petAbsorbedDamage;
			adjustedDamage -= petAbsorbedDamage;
		}

		if (adjustedDamage > 0 && playerHasTungsten)
		{
			adjustedDamage = Math.Max(adjustedDamage - 1, 0);
		}

		playerDamage += adjustedDamage;
	}

	private static int GetProjectedBlock(CombatContext context, Creature player)
	{
		int block = player.Block;
		if (block == 0)
		{
			if (HasRelic<Orichalcum>(player))
			{
				block += 6;
			}
			if (HasRelic<FakeOrichalcum>(player))
			{
				block += 3;
			}
		}

		block += GetRippleBasinBlock(context, player);
		block += Math.Max(GetPowerAmount<PlatingPower>(player), 0);
		block += GetCloakClaspBlock(player);
		block += GetFrostOrbBlock(context, player);
		return block;
	}

	private static int GetRippleBasinBlock(CombatContext context, Creature player)
	{
		try
		{
			RippleBasin? relic = GetRelic<RippleBasin>(player);
			if (relic == null || DidPlayerPlayAttackCardThisTurn(context, player.Player))
			{
				return 0;
			}

			return relic.DynamicVars.TryGetValue("Block", out DynamicVar value) ? (int)value.BaseValue : 0;
		}
		catch
		{
			return 0;
		}
	}

	private static int GetCloakClaspBlock(Creature player)
	{
		try
		{
			CloakClasp? relic = GetRelic<CloakClasp>(player);
			if (relic == null)
			{
				return 0;
			}

			int handCount = (player.Player?.PlayerCombatState?.Hand?.Cards?.Count).GetValueOrDefault();
			if (handCount <= 0)
			{
				return 0;
			}

			int blockPerCard = relic.DynamicVars.TryGetValue("Block", out DynamicVar value) ? (int)value.BaseValue : 1;
			return handCount * blockPerCard;
		}
		catch
		{
			return 0;
		}
	}

	private static int GetFrostOrbBlock(CombatContext context, Creature player)
	{
		IReadOnlyList<OrbModel>? orbs = player.Player?.PlayerCombatState?.OrbQueue?.Orbs;
		if (orbs == null)
		{
			return 0;
		}

		int totalBlock = 0;
		foreach (OrbModel orb in orbs)
		{
			if (orb is FrostOrb frostOrb)
			{
				int passiveTriggerCount = GetOrbPassiveTriggerCount(context.State, orb);
				totalBlock += Math.Max((int)frostOrb.PassiveVal, 0) * passiveTriggerCount;
			}
		}

		return totalBlock;
	}

	private static int GetOrbPassiveTriggerCount(object? combatState, OrbModel orb)
	{
		try
		{
			if (combatState is not MegaCrit.Sts2.Core.Combat.CombatState state)
			{
				return 1;
			}

			return Math.Max(Hook.ModifyOrbPassiveTriggerCount(state, orb, 1, out _), 0);
		}
		catch
		{
			return 1;
		}
	}

	private static bool DidPlayerPlayAttackCardThisTurn(CombatContext context, Player? player)
	{
		if (player == null)
		{
			return false;
		}

		return context.CombatManager.History.CardPlaysFinished.Any(entry => entry.HappenedThisTurn(context.State) && entry.CardPlay.Card.Owner == player && entry.CardPlay.Card.Type.ToString().Contains("Attack"));
	}
}