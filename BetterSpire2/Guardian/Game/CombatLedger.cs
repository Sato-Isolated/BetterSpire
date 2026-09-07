#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace BetterSpire2.Guardian.Game;

internal sealed record ObservedHit(string Receiver, string Source, int HpLost, int Blocked, bool BypassesBlock);

/// <summary>Actual engine history, not simulated values. Includes unblockable HP loss.</summary>
internal sealed class CombatLedger
{
    private CombatState? _state;
    private int _processed;
    private readonly Queue<ObservedHit> _recent = new();
    internal int PlayerHpLost { get; private set; }
    internal int PlayerBlocked { get; private set; }
    internal int PlayerBlockGained { get; private set; }
    internal int PetHpLost { get; private set; }
    private ObservedHit[]? _recentSnapshot;
    internal IReadOnlyList<ObservedHit> Recent => _recentSnapshot ??= _recent.ToArray();

    internal void Reset()
    {
        _state = null; _processed = 0; _recent.Clear(); _recentSnapshot = null;
        PlayerHpLost = PlayerBlocked = PlayerBlockGained = PetHpLost = 0;
    }

    internal void Update(CombatState state, Creature local)
    {
        var history = CombatManager.Instance.History.Entries;
        var entries = history as IReadOnlyList<CombatHistoryEntry> ?? history.ToArray();
        if (!ReferenceEquals(_state, state) || entries.Count < _processed)
        {
            _state = state;
            _processed = 0;
            _recent.Clear(); _recentSnapshot = null;
            PlayerHpLost = PlayerBlocked = PlayerBlockGained = PetHpLost = 0;
        }
        for (; _processed < entries.Count; _processed++)
        {
            switch (entries[_processed])
            {
                case DamageReceivedEntry hit when hit.Receiver == local || hit.Receiver.PetOwner?.Creature == local:
                    if (hit.Receiver == local)
                    {
                        PlayerHpLost += hit.Result.UnblockedDamage;

                    }
                    else PetHpLost += hit.Result.UnblockedDamage;
                    // Direct pet damage uses its owner's block; a redirected overflow has
                    // zero blocked damage on the pet record, so this does not double count.
                    PlayerBlocked += hit.Result.BlockedDamage;
                    string source = hit.CardSource?.Title ?? hit.Dealer?.Name ??
                        (ModText.IsFrench ? "Effet (source non fournie)" : "Effect (source unavailable)");
                    _recentSnapshot = null;
                    _recent.Enqueue(new(hit.Receiver.Name, source, hit.Result.UnblockedDamage,
                        hit.Result.BlockedDamage,
                        hit.Result.Props.HasFlag(MegaCrit.Sts2.Core.ValueProps.ValueProp.Unblockable)));
                    while (_recent.Count > 120) _recent.Dequeue();
                    break;
                case BlockGainedEntry block when block.Receiver == local:
                    PlayerBlockGained += block.Amount;
                    break;
            }
        }
    }
}
