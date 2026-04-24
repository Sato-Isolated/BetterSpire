#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace BetterSpire2.Trackers;

public static partial class DamageTracker
{

	private static int _lastSnapshotHash;

	private static bool _hasSnapshot;

	public static void Recalculate()
	{
		try
		{
			if (!TryGetCombatContext(out CombatContext? context))
			{
				Hide();
				return;
			}
			if (context.CombatManager.IsEnemyTurnStarted)
			{
				RefreshLabelPositions();
				return;
			}
			int snapshotHash = BuildSnapshotHash(context);
			if (_hasSnapshot && snapshotHash == _lastSnapshotHash)
			{
				RefreshLabelPositions();
				return;
			}
			RenderLabels(CalculateLabels(context));
			_lastSnapshotHash = snapshotHash;
			_hasSnapshot = true;
		}
		catch (Exception ex)
		{
			ModLog.Error("DamageTracker.Recalculate", ex);
		}
	}

	private static Dictionary<Creature, LabelRenderInfo> CalculateLabels(CombatContext context)
	{
		Dictionary<Creature, LabelRenderInfo> dictionary = new Dictionary<Creature, LabelRenderInfo>();
		foreach (Creature player in context.PlayerCreatures)
		{
			if (!IsTrackedPlayerCreature(player))
			{
				continue;
			}
			Creature livingPet = GetLivingPet(player);
			bool petVisible = IsPetVisible(livingPet);
			List<int> enemyAttackHits = CollectEnemyAttackHits(context, player);
			List<PendingDamage> selfDamageSources = CollectSelfDamageSources(player, livingPet);
			int regenAmount = GetPowerAmount<RegenPower>(player);
			if (enemyAttackHits.Count == 0 && selfDamageSources.Count == 0 && (!ModSettings.ShowExpectedHp || regenAmount <= 0))
			{
				continue;
			}
			DamageResolution damageResolution = ResolveDamage(player, livingPet, petVisible, GetProjectedBlock(context, player), selfDamageSources, enemyAttackHits);
			dictionary[player] = new LabelRenderInfo(FormatPlayerLabel(player, damageResolution.PlayerDamage, regenAmount), (damageResolution.PlayerDamage > 0) ? _incomingColor : _safeColor);
			if (petVisible && livingPet != null && damageResolution.PetDamage > 0)
			{
				dictionary[livingPet] = new LabelRenderInfo(damageResolution.PetDamage.ToString(), _petColor);
			}
		}
		return dictionary;
	}

	private static string FormatPlayerLabel(Creature player, int incomingDamage, int regenAmount)
	{
		if (!ModSettings.ShowExpectedHp)
		{
			return incomingDamage.ToString();
		}
		int num = ClampHp(player.CurrentHp - incomingDamage + regenAmount, player.MaxHp);
		return $"{incomingDamage} ({num})";
	}

	private static int ClampHp(int hp, int maxHp)
	{
		return Math.Max(0, Math.Min(hp, maxHp));
	}

	private static bool IsTrackedPlayerCreature(Creature creature)
	{
		return !creature.IsDead && creature.IsPlayer && !creature.IsPet;
	}

	private static bool IsPetVisible(Creature? pet)
	{
		return pet != null && !pet.IsDead && pet.MaxHp > 0 && (pet.Monster == null || pet.Monster.IsHealthBarVisible);
	}

	private static Creature? GetLivingPet(Creature player)
	{
		IReadOnlyList<Creature> pets = player.Pets;
		if (pets == null || pets.Count == 0)
		{
			return null;
		}
		return pets.FirstOrDefault((Creature pet) => !pet.IsDead);
	}

	private static bool HasRelic<TRelic>(Creature player) where TRelic : class
	{
		return player.Player?.Relics?.OfType<TRelic>().Any() == true;
	}

	private static TRelic? GetRelic<TRelic>(Creature player) where TRelic : class
	{
		return player.Player?.Relics?.OfType<TRelic>().FirstOrDefault();
	}

	private static int GetPowerAmount<TPower>(Creature creature) where TPower : PowerModel
	{
		TPower power = creature.GetPower<TPower>();
		return (power != null) ? power.Amount : 0;
	}

	private static bool TryGetDynamicBaseValue(DynamicVarSet dynamicVars, string key, out int value)
	{
		value = 0;
		if (!dynamicVars.TryGetValue(key, out DynamicVar value2))
		{
			return false;
		}
		value = (int)value2.BaseValue;
		return true;
	}

}