#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;

namespace BetterSpire2.Trackers;

public static partial class DamageTracker
{
	private static readonly FieldInfo? _powerInternalDataField = typeof(PowerModel).GetField("_internalData", BindingFlags.Instance | BindingFlags.NonPublic);

	private static bool _loggedInterceptReflectionFailure;

	private static bool TryGetCombatContext(out CombatContext? context)
	{
		context = null;
		if (!ModSettings.PlayerDamageTotal)
		{
			return false;
		}

		NCapstoneContainer capstoneContainer = NCapstoneContainer.Instance;
		if (capstoneContainer != null && capstoneContainer.InUse)
		{
			return false;
		}

		NOverlayStack overlayStack = NOverlayStack.Instance;
		if (overlayStack != null && overlayStack.ScreenCount > 0)
		{
			return false;
		}

		CombatManager combatManager = CombatManager.Instance;
		if (combatManager == null)
		{
			return false;
		}

		CombatState state = combatManager.DebugOnlyGetState();
		if (state == null)
		{
			return false;
		}

		IReadOnlyList<Creature> playerCreatures = state.PlayerCreatures;
		InterceptState interceptState = BuildInterceptState(playerCreatures);
		Creature? localPlayerCreature = FindLocalPlayerCreature(playerCreatures);
		if (localPlayerCreature == null)
		{
			return false;
		}

		context = new CombatContext(combatManager, state, playerCreatures, GetActiveEnemies(state), localPlayerCreature, GetAttackMultiplier(localPlayerCreature, interceptState), interceptState);
		return true;
	}

	private static InterceptState BuildInterceptState(IReadOnlyList<Creature> playerCreatures)
	{
		InterceptState interceptState = new InterceptState();
		foreach (Creature playerCreature in playerCreatures)
		{
			if (!IsTrackedPlayerCreature(playerCreature))
			{
				continue;
			}

			InterceptPower power = playerCreature.GetPower<InterceptPower>();
			if (power == null)
			{
				continue;
			}

			IReadOnlyList<Creature> coveredCreatures = GetInterceptCoveredCreatures(power);
			if (coveredCreatures.Count <= 0)
			{
				continue;
			}

			interceptState.CoveredCountByInterceptor[playerCreature] = coveredCreatures.Count;
			foreach (Creature coveredCreature in coveredCreatures)
			{
				if (coveredCreature != playerCreature)
				{
					interceptState.CoveredByInterceptor[coveredCreature] = playerCreature;
				}
			}
		}

		return interceptState;
	}

	private static IReadOnlyList<Creature> GetActiveEnemies(CombatState state)
	{
		List<Creature> activeEnemies = new List<Creature>();
		foreach (Creature enemy in state.Enemies)
		{
			if (enemy.IsDead || enemy.Monster == null)
			{
				continue;
			}

			PoisonPower poison = enemy.GetPower<PoisonPower>();
			if (poison != null && poison.Amount >= enemy.CurrentHp)
			{
				HardToKillPower hardToKill = enemy.GetPower<HardToKillPower>();
				int pendingPoison = poison.Amount;
				if (hardToKill != null && hardToKill.Amount > 0)
				{
					pendingPoison = Math.Min(pendingPoison, hardToKill.Amount);
				}

				if (pendingPoison >= enemy.CurrentHp)
				{
					continue;
				}
			}

			activeEnemies.Add(enemy);
		}

		return activeEnemies;
	}

	private static Creature? FindLocalPlayerCreature(IReadOnlyList<Creature> playerCreatures)
	{
		try
		{
			ulong localNetId = (RunManager.Instance?.NetService?.NetId).GetValueOrDefault();
			if (localNetId == 0)
			{
				return null;
			}

			return playerCreatures.FirstOrDefault(creature => !creature.IsDead && creature.IsPlayer && !creature.IsPet && creature.Player?.NetId == localNetId);
		}
		catch
		{
			return null;
		}
	}

	private static DamageMultiplier GetAttackMultiplier(Creature? creature, InterceptState interceptState)
	{
		DamageMultiplier multiplier = DamageMultiplier.One;
		if (creature == null)
		{
			return multiplier;
		}

		try
		{
			if (creature.GetPower<TankPower>() != null)
			{
				multiplier = multiplier.Multiply(2L, 1L);
			}

			if (creature.GetPower<GuardedPower>() != null)
			{
				multiplier = multiplier.Multiply(1L, 2L);
			}

			if (interceptState.CoveredCountByInterceptor.TryGetValue(creature, out int coveredCount) && coveredCount > 0)
			{
				multiplier = multiplier.Multiply(coveredCount + 1, 1L);
			}
		}
		catch
		{
		}

		return multiplier;
	}

	private static IReadOnlyList<Creature> GetInterceptCoveredCreatures(InterceptPower power)
	{
		try
		{
			if (_powerInternalDataField == null)
			{
				return Array.Empty<Creature>();
			}

			object? internalData = _powerInternalDataField.GetValue(power);
			if (internalData == null)
			{
				return Array.Empty<Creature>();
			}

			FieldInfo? coveredCreaturesField = internalData.GetType().GetField("coveredCreatures", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (coveredCreaturesField?.GetValue(internalData) is IEnumerable<Creature> coveredCreatures)
			{
				return coveredCreatures.Where(creature => creature != null).ToList();
			}
		}
		catch (Exception ex)
		{
			if (!_loggedInterceptReflectionFailure)
			{
				_loggedInterceptReflectionFailure = true;
				ModLog.Error("DamageTracker.GetInterceptCoveredCreatures", ex);
			}
		}

		return Array.Empty<Creature>();
	}
}