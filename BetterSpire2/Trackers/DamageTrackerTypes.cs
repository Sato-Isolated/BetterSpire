#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace BetterSpire2.Trackers;

public static partial class DamageTracker
{
	private enum DamageKind
	{
		Blockable,
		HpLoss
	}

	private readonly struct PendingDamage
	{
		public Creature Target { get; }

		public int Amount { get; }

		public DamageKind Kind { get; }

		public PendingDamage(Creature target, int amount, DamageKind kind)
		{
			Target = target;
			Amount = amount;
			Kind = kind;
		}
	}

	private readonly struct DamageResolution
	{
		public int PlayerDamage { get; }

		public int PetDamage { get; }

		public DamageResolution(int playerDamage, int petDamage)
		{
			PlayerDamage = playerDamage;
			PetDamage = petDamage;
		}
	}

	private readonly struct LabelRenderInfo
	{
		public string Text { get; }

		public Color Color { get; }

		public LabelRenderInfo(string text, Color color)
		{
			Text = text;
			Color = color;
		}
	}

	private readonly struct DamageMultiplier
	{
		public static DamageMultiplier One => new DamageMultiplier(1L, 1L);

		public long Numerator { get; }

		public long Denominator { get; }

		public DamageMultiplier(long numerator, long denominator)
		{
			if (denominator == 0L)
			{
				numerator = 1L;
				denominator = 1L;
			}
			if (numerator == 0L)
			{
				numerator = 0L;
				denominator = 1L;
			}
			if (denominator < 0L)
			{
				numerator = -numerator;
				denominator = -denominator;
			}

			long gcd = Gcd(Math.Abs(numerator), Math.Abs(denominator));
			Numerator = numerator / gcd;
			Denominator = denominator / gcd;
		}

		public DamageMultiplier Multiply(long numerator, long denominator)
		{
			return new DamageMultiplier(Numerator * numerator, Denominator * denominator);
		}

		public int ScaleFrom(int amount, DamageMultiplier baseline)
		{
			if (amount <= 0)
			{
				return 0;
			}

			long denominator = Denominator * baseline.Numerator;
			if (denominator <= 0L)
			{
				return amount;
			}

			long numerator = (long)amount * Numerator * baseline.Denominator;
			return Math.Max(0, (int)(numerator / denominator));
		}

		private static long Gcd(long a, long b)
		{
			while (b != 0L)
			{
				long remainder = a % b;
				a = b;
				b = remainder;
			}

			return Math.Max(a, 1L);
		}
	}

	private sealed class InterceptState
	{
		public Dictionary<Creature, Creature> CoveredByInterceptor { get; } = new Dictionary<Creature, Creature>();

		public Dictionary<Creature, int> CoveredCountByInterceptor { get; } = new Dictionary<Creature, int>();
	}

	private sealed class CombatContext
	{
		public CombatManager CombatManager { get; }

		public CombatState State { get; }

		public IReadOnlyList<Creature> PlayerCreatures { get; }

		public IReadOnlyList<Creature> ActiveEnemies { get; }

		public Creature? LocalPlayerCreature { get; }

		public DamageMultiplier LocalAttackMultiplier { get; }

		public InterceptState Intercepts { get; }

		public CombatContext(
			CombatManager combatManager,
			CombatState state,
			IReadOnlyList<Creature> playerCreatures,
			IReadOnlyList<Creature> activeEnemies,
			Creature? localPlayerCreature,
			DamageMultiplier localAttackMultiplier,
			InterceptState intercepts)
		{
			CombatManager = combatManager;
			State = state;
			PlayerCreatures = playerCreatures;
			ActiveEnemies = activeEnemies;
			LocalPlayerCreature = localPlayerCreature;
			LocalAttackMultiplier = localAttackMultiplier;
			Intercepts = intercepts;
		}
	}
}