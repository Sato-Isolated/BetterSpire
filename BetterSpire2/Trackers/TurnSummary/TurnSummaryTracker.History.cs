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
				case CardPlayFinishedEntry cpf when summaries.TryGetValue(cpf.Actor, out PlayerTurnSummary? cardSummary):
					cardSummary.CardsPlayed++;
					cardSummary.RegisterPlayedCard(cpf.CardPlay.Card);
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

		foreach (TurnTimelineEntry entry in _timelineEntries)
		{
			if (!IsRoundIncluded(entry.RoundNumber) || !summaries.TryGetValue(entry.Owner, out PlayerTurnSummary? summary))
			{
				continue;
			}

			switch (entry.Kind)
			{
				case TimelineEntryKind.BlockLost:
					summary.BlockLost += entry.Amount;
					break;
				case TimelineEntryKind.BlockCleared:
					summary.BlockCleared += entry.Amount;
					break;
			}
		}

		return summaries.Values.OrderByDescending(s => s.IsLocal).ThenBy(s => s.DisplayName, StringComparer.Ordinal).ToList();
	}

	private static void ProcessPendingHistoryEntries(CombatHistory history)
	{
		if (_processedHistoryCount < 0)
		{
			_processedHistoryCount = 0;
		}

		int index = 0;
		foreach (CombatHistoryEntry entry in history.Entries)
		{
			if (index++ < _processedHistoryCount)
			{
				continue;
			}

			AppendTimelineFromHistory(entry);
		}
		_processedHistoryCount = index;
	}

	private static void AppendTimelineFromHistory(CombatHistoryEntry entry)
	{
		switch (entry)
		{
			case CardPlayFinishedEntry cpf when IsTrackedPlayerCreature(cpf.Actor):
				AppendTimeline(cpf.Actor, entry.RoundNumber, $"Played {GetCardName(cpf.CardPlay.Card)}", _cardsColor);
				break;

			case DamageReceivedEntry dre:
				if (dre.Dealer != null && IsTrackedPlayerCreature(dre.Dealer))
				{
					AppendTimeline(dre.Dealer, entry.RoundNumber, $"Hit {GetCreatureName(dre.Receiver)} for {FormatDamageBreakdown(dre.Result)}", _dealColor);
				}
				if (IsTrackedPlayerCreature(dre.Receiver))
				{
					AppendTimeline(dre.Receiver, entry.RoundNumber, $"Took {FormatDamageBreakdown(dre.Result)} from {GetCreatureName(dre.Dealer)}", _takeColor);
					if (dre.Result.WasBlockBroken)
					{
						AppendTimeline(dre.Receiver, entry.RoundNumber, "Block broken", _breakColor);
					}
				}
				break;

			case BlockGainedEntry bge when IsTrackedPlayerCreature(bge.Receiver):
				AppendTimeline(bge.Receiver, entry.RoundNumber, $"Gained {bge.Amount} block from {DescribeBlockSource(bge.CardPlay, bge.Props)}", _blockGainColor);
				break;
		}
	}

	private static void AppendTimeline(Creature creature, int roundNumber, string text, Godot.Color color, TimelineEntryKind kind = TimelineEntryKind.Generic, int amount = 0)
	{
		if (!IsTrackedPlayerCreature(creature)) return;
		_timelineEntries.Add(new TurnTimelineEntry(++_timelineSequence, Math.Max(1, roundNumber), creature, text, color, kind, amount));
	}

	private static List<TurnTimelineEntry> GetTimelineFor(Creature creature)
	{
		return _timelineEntries.Where(e => ReferenceEquals(e.Owner, creature) && IsRoundIncluded(e.RoundNumber)).OrderBy(e => e.Order).ToList();
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
		_timelineEntries.Clear();
		_processedHistoryCount = 0;
		_timelineSequence = 0;
	}

	private static bool IsTrackedPlayerCreature(Creature creature)
	{
		return creature.IsPlayer && !creature.IsPet && creature.Player != null;
	}
}