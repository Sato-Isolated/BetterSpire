#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace BetterSpire2.Trackers;

public static partial class TurnSummaryTracker
{
	private static List<PlayerTurnSummary> BuildSummaries(CombatState state, CombatHistory history)
	{
		ulong localNetId = GetLocalNetId();
		Dictionary<Creature, PlayerTurnSummary> summaries = state.PlayerCreatures
			.Where(IsTrackedPlayerCreature)
			.ToDictionary(c => c, c => new PlayerTurnSummary(c, GetDisplayName(c.Player, localNetId), c.Player?.NetId == localNetId && localNetId != 0));

		foreach (CombatHistoryEntry entry in history.Entries)
		{
			if (!IsRoundIncluded(entry.RoundNumber)) continue;

			switch (entry)
			{
				case CardDrawnEntry cde when summaries.TryGetValue(cde.Actor, out PlayerTurnSummary? drawSummary):
					drawSummary.CardsDrawn++;
					break;

				case CardPlayFinishedEntry cpf when summaries.TryGetValue(cpf.Actor, out PlayerTurnSummary? cardSummary):
					cardSummary.CardsPlayed++;
					cardSummary.RegisterPlayedCard(cpf.CardPlay.Card);
					break;

				case CardDiscardedEntry cde when summaries.TryGetValue(cde.Actor, out PlayerTurnSummary? discardSummary):
					discardSummary.CardsDiscarded++;
					break;

				case CardGeneratedEntry cge when summaries.TryGetValue(cge.Actor, out PlayerTurnSummary? generatedSummary):
					generatedSummary.CardsGenerated++;
					break;

				case CardExhaustedEntry cee when summaries.TryGetValue(cee.Actor, out PlayerTurnSummary? exhaustedSummary):
					exhaustedSummary.CardsExhausted++;
					break;

				case EnergySpentEntry ese when summaries.TryGetValue(ese.Actor, out PlayerTurnSummary? energySummary):
					energySummary.EnergySpent += ese.Amount;
					break;

				case DamageReceivedEntry dre:
					if (summaries.TryGetValue(dre.Receiver, out PlayerTurnSummary? receivedSummary))
					{
						receivedSummary.DamageReceivedTotal += dre.Result.TotalDamage;
						receivedSummary.DamageReceivedHp += dre.Result.UnblockedDamage;
						receivedSummary.DamageReceivedBlocked += dre.Result.BlockedDamage;
						receivedSummary.BlockSpent += dre.Result.BlockedDamage;
						if (dre.Result.WasBlockBroken)
						{
							receivedSummary.BlockBreaks++;
						}
					}
					if (dre.Dealer != null && summaries.TryGetValue(dre.Dealer, out PlayerTurnSummary? dealtSummary))
					{
						dealtSummary.DamageDealtTotal += dre.Result.TotalDamage;
						dealtSummary.DamageDealtHp += dre.Result.UnblockedDamage;
						dealtSummary.DamageDealtBlocked += dre.Result.BlockedDamage;
						dealtSummary.RegisterCardDamage(dre.CardSource, dre.Result.UnblockedDamage, dre.Result.BlockedDamage);
					}
					break;

				case BlockGainedEntry bge when summaries.TryGetValue(bge.Receiver, out PlayerTurnSummary? blockSummary):
					blockSummary.BlockGained += bge.Amount;
					blockSummary.RegisterBlockGain(bge.CardPlay, bge.Amount, bge.Props);
					break;
			}
		}

		foreach (SupplementalStatEntry entry in _supplementalEntries)
		{
			if (!IsRoundIncluded(entry.RoundNumber) || !summaries.TryGetValue(entry.Owner, out PlayerTurnSummary? summary))
			{
				continue;
			}

			switch (entry.Kind)
			{
				case SupplementalStatKind.BlockLost:
					summary.BlockLost += entry.Amount;
					break;
				case SupplementalStatKind.BlockCleared:
					summary.BlockCleared += entry.Amount;
					break;
				case SupplementalStatKind.EnergyGained:
					summary.EnergyGained += entry.Amount;
					break;
			}
		}

		return summaries.Values.OrderByDescending(s => s.IsLocal).ThenBy(s => s.DisplayName, StringComparer.Ordinal).ToList();
	}

	private static void AppendSupplementalStat(Creature creature, int roundNumber, SupplementalStatKind kind, int amount)
	{
		if (!IsTrackedPlayerCreature(creature)) return;
		_supplementalEntries.Add(new SupplementalStatEntry(Math.Max(1, roundNumber), creature, kind, amount));
	}

	private static ulong GetLocalNetId()
	{
		try
		{
			return (RunManager.Instance?.NetService?.NetId).GetValueOrDefault();
		}
		catch
		{
			return 0uL;
		}
	}

	private static int GetCurrentRoundNumber()
	{
		return CombatManager.Instance?.DebugOnlyGetState()?.RoundNumber ?? Math.Max(1, _trackedRound);
	}

	private static int GetSelectedRoundNumber()
	{
		int maxRound = Math.Max(1, _trackedRound);
		return _selectedRound <= 0 ? maxRound : Math.Clamp(_selectedRound, 1, maxRound);
	}

	private static bool IsRoundIncluded(int roundNumber)
	{
		if (roundNumber <= 0) return false;
		return _viewMode == HistoryViewMode.Combat || roundNumber == GetSelectedRoundNumber();
	}

	private static string FormatDamageBreakdown(DamageResult result)
	{
		if (result.UnblockedDamage > 0 && result.BlockedDamage > 0)
		{
			return $"{result.TotalDamage} ({result.UnblockedDamage}hp, {result.BlockedDamage}blk)";
		}
		if (result.UnblockedDamage > 0) return $"{result.UnblockedDamage}hp";
		if (result.BlockedDamage > 0) return $"{result.BlockedDamage}blk";
		return "0";
	}

	private static string DescribeBlockSource(CardPlay? cardPlay, ValueProp props)
	{
		CardModel? card = cardPlay?.Card;
		if (card != null)
		{
			string type = card.Type.ToString().ToLowerInvariant();
			return type switch
			{
				"attack" => GetCardName(card) + " (atk)",
				"skill" => GetCardName(card) + " (skill)",
				"power" => GetCardName(card) + " (pwr)",
				_ => GetCardName(card)
			};
		}

		if (props.HasFlag(ValueProp.Unpowered)) return "Other";
		return "Power / Relic";
	}

	private static string GetDisplayName(Player? player, ulong localNetId)
	{
		if (player == null) return "Player";
		string playerName = PlatformUtil.GetPlayerName(PlatformType.Steam, player.NetId);
		string? characterName = player.Character?.Title?.GetFormattedText();
		string displayName = !string.IsNullOrEmpty(playerName) ? playerName : (characterName ?? "Player");
		if (!string.IsNullOrEmpty(playerName) && !string.IsNullOrEmpty(characterName))
		{
			displayName = $"{playerName} ({characterName})";
		}
		if (localNetId != 0 && player.NetId == localNetId)
		{
			displayName += " (You)";
		}
		return displayName;
	}

	private static string GetCreatureName(Creature? creature)
	{
		if (creature == null) return "Unknown";
		if (creature.IsPlayer) return GetDisplayName(creature.Player, GetLocalNetId());
		return creature.Monster?.Id.Entry ?? "Enemy";
	}

	private static string GetCardName(CardModel? card)
	{
		if (card == null) return "Unknown";
		string? title = card.Title;
		return !string.IsNullOrWhiteSpace(title) ? title : card.Id.Entry;
	}

	private static void ResetCombatTracking()
	{
		_supplementalEntries.Clear();
		_expandedCardSections.Clear();
	}

	private static bool IsTrackedPlayerCreature(Creature creature)
	{
		return creature.IsPlayer && !creature.IsPet && creature.Player != null;
	}
}