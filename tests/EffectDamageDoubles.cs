#nullable enable
// Source-linked native-boundary doubles. Harmony patches the actual async methods
// below; metadata tests separately check the v113 game call sites/signatures.
global using BetterSpire2.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BetterSpire2.Journal.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace BetterSpire2.Core
{
    internal static class ModLog
    {
        internal static readonly List<string> Errors = new();
        public static void Error(string context, Exception ex) => Errors.Add(context + ": " + ex.Message);
        public static void Info(string text) => Errors.Add(text);
    }
    internal static class ModText { public static bool IsFrench => true; }
}
namespace MegaCrit.Sts2.Core.ValueProps
{ [Flags] public enum ValueProp { Unpowered = 1, Unblockable = 2 } }
namespace MegaCrit.Sts2.Core.GameActions.Multiplayer
{ public sealed class PlayerChoiceContext { public List<object> Models { get; } = new(); } }
namespace MegaCrit.Sts2.Core.Entities.Players
{ public sealed class Player { public ulong NetId; public Creature Creature = null!; } }
namespace MegaCrit.Sts2.Core.Entities.FoolArrows { public sealed class ArrowSlot { } }
namespace MegaCrit.Sts2.Core.Entities.Cards
{ public sealed class CardPlay { public CardModel Card = null!; public Creature? Target; } }
namespace MegaCrit.Sts2.Core.Models
{
    public sealed class ModelId { public string Entry = "TEST"; }
    public sealed class LocalizedText(string text) { public string GetFormattedText() => text; }
    public class CardModel { public ModelId Id = new(); public string Title = "Card"; public Player? Owner; }
    public class PowerModel
    {
        public Creature Owner = null!;
        public Creature? Applier;
        public int Amount;
        public LocalizedText Title = new("Power");
    }
}
namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    public readonly record struct DamageTargetData(Creature target);
    public sealed class Creature
    {
        public string Name = "Enemy";
        public CombatSide Side;
        public CombatState? CombatState;
        public Player? Player, PetOwner;
        public int CurrentHp = 100;
        public int MaxHp = 100;
        public Action<int,int>? CurrentHpChanged;
        public int Block;
        public bool IsPlayer => Player != null;
        public bool IsDead => CurrentHp <= 0;
        public bool IsAlive => !IsDead;
        public Creature? RedirectTo, PreBlockTarget;
        public List<PowerModel> Powers = new();
        public T? GetPower<T>() where T : PowerModel => Powers.OfType<T>().FirstOrDefault();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void SetCurrentHpInternal(decimal amount)
        {
            int before = CurrentHp, next = (int)Math.Min(amount, MaxHp);
            if (next < 0) throw new ArgumentException("Current HP must be positive");
            CurrentHp = next;
            if (before != next) CurrentHpChanged?.Invoke(before, next);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public DamageResult LoseHpInternal(decimal amount, ValueProp props)
        {
            int before = CurrentHp, loss = (int)Math.Clamp(amount, 0, 999999999);
            CurrentHp = Math.Max(0, before - loss);
            return new DamageResult(this) { UnblockedDamage = before - CurrentHp,
                OverkillDamage = Math.Max(0, loss - before), WasTargetKilled = before > 0 && loss >= before };
        }
    }
    public sealed class DamageResult(Creature receiver)
    {
        public DamageResult(Creature receiver, ValueProp props) : this(receiver) { }
        public Creature Receiver { get; } = receiver;
        public int UnblockedDamage { get; set; }
        public int BlockedDamage { get; set; }
        public int OverkillDamage { get; set; }
        public bool WasTargetKilled;
    }
}
namespace MegaCrit.Sts2.Core.Models.Cards
{
    public sealed class Judgement : CardModel
    {
        public int Threshold = 30;
        public Judgement() { Id.Entry = "JUDGEMENT"; Title = "Judgement"; }
        public Task Play(PlayerChoiceContext context, CardPlay play) => OnPlay(context,play);
        private async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        {
            ArgumentNullException.ThrowIfNull(cardPlay.Target);
            if (cardPlay.Target.IsAlive && cardPlay.Target.CurrentHp <= Threshold)
                await Commands.CreatureCmd.SetCurrentHp(cardPlay.Target, 0m);
        }
    }
}
namespace MegaCrit.Sts2.Core.Combat
{
    public enum CombatSide { Player, Enemy }
    public interface ICombatState { }
    public sealed class CombatState : ICombatState
    {
        public int RoundNumber = 1;
        public CombatSide CurrentSide;
        public List<Creature> Enemies = new();
    }
    public sealed class CombatManager
    {
        public static CombatManager Instance = new();
        public int CurrentCombatId = 1;
        public bool IsInProgress = true;
        public bool IsEnding => BetterSpire2.Journal.Game.JournalService.State.Enemies.All(c => c.IsDead);
        public CombatHistory History = new();
    }
}
namespace MegaCrit.Sts2.Core.Hooks
{ public enum ModifyDamageHookType { All, Cap } }
namespace MegaCrit.Sts2.Core.Combat.History
{
    public abstract class CombatHistoryEntry
    {
        private int RoundNumber { get; }
        private CombatSide CurrentSide { get; }
        public Creature Actor = null!;
        protected CombatHistoryEntry(CombatState? state = null)
        { RoundNumber = state?.RoundNumber ?? 1; CurrentSide = state?.CurrentSide ?? CombatSide.Player; }
    }
    public sealed class CombatHistory
    {
        public List<CombatHistoryEntry> Entries = new();
        public Action? Changed;
        public void Add(CombatHistoryEntry entry) { Entries.Add(entry); Changed?.Invoke(); }
        public void DamageReceived(ICombatState state, Creature receiver, Creature? dealer, DamageResult result, CardModel? card) =>
            Add(new DamageReceivedEntry((CombatState)state) { Receiver = receiver, Dealer = dealer, Result = result, CardSource = card });
    }
}
namespace MegaCrit.Sts2.Core.Combat.History.Entries
{
    public sealed class PowerReceivedEntry : CombatHistoryEntry { public PowerModel Power = null!; public Creature? Applier; public decimal Amount; }
    public sealed class CardPlayFinishedEntry : CombatHistoryEntry { public CardPlay CardPlay = null!; }
    public sealed class CardDrawnEntry : CombatHistoryEntry { }
    public sealed class CardDiscardedEntry : CombatHistoryEntry { }
    public sealed class CardExhaustedEntry : CombatHistoryEntry { }
    public sealed class CardGeneratedEntry : CombatHistoryEntry { }
    public sealed class EnergySpentEntry : CombatHistoryEntry { public int Amount; }
    public sealed class StarsModifiedEntry : CombatHistoryEntry { public int Amount; }
    public sealed class OrbChanneledEntry : CombatHistoryEntry { }
    public sealed class PotionUsedEntry : CombatHistoryEntry { }
    public sealed class BlockGainedEntry : CombatHistoryEntry { public Creature Receiver = null!; public int Amount; public CardPlay? CardPlay; }
    public sealed class DamageReceivedEntry(CombatState state) : CombatHistoryEntry(state)
    { public Creature Receiver = null!; public Creature? Dealer; public DamageResult Result = null!; public CardModel? CardSource; }
}
namespace MegaCrit.Sts2.Core.Models.Powers
{
    public sealed class ThornsPower : PowerModel
    {
        public async Task BeforeDamageReceived(PlayerChoiceContext context, Creature target, decimal amount,
            ValueProp props, Creature? dealer, CardModel? card)
        {
            await Task.Yield();
            if (target == Owner && dealer != null)
                await Commands.CreatureCmd.Damage(context, dealer, Amount, ValueProp.Unpowered, Owner, null, null);
        }
    }
    public sealed class FlameBarrierPower : PowerModel
    {
        public async Task AfterDamageReceived(PlayerChoiceContext context, Creature target, DamageResult result,
            ValueProp props, Creature? dealer, CardModel? card)
        {
            await Task.Yield();
            if (target == Owner && dealer != null)
                await Commands.CreatureCmd.Damage(context, dealer, Amount, ValueProp.Unpowered, Owner);
        }
    }
    public sealed class ImpaledPower : PowerModel { }
    public sealed class ExplosiveArrowsPower : PowerModel
    {
        public Creature[] Targets = System.Array.Empty<Creature>();
        public async Task AfterArrowImpaled(ImpaledPower power, Entities.FoolArrows.ArrowSlot arrow,
            PlayerChoiceContext context, CardModel? source)
        {
            await Task.Yield();
            await Commands.CreatureCmd.Damage(context, Targets, Amount, ValueProp.Unpowered, Owner);
        }
    }
    public sealed class ViolentChordPower : PowerModel
    {
        public Creature[] Targets = System.Array.Empty<Creature>();
        public async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
        {
            await Task.Yield();
            await Commands.CreatureCmd.Damage(context, Targets, Amount, ValueProp.Unpowered, Owner);
        }
    }
    public sealed class PoisonPower : PowerModel { public Task Trigger() => Task.CompletedTask; }
    public sealed class StranglePower : PowerModel
    {
        private readonly Dictionary<CardModel, int> _amounts = new();
        public void BeforeCardPlayed(CardPlay play)
        { if (Applier?.Player == play.Card.Owner) _amounts.Add(play.Card, Amount); }
        public async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay play)
        {
            if (_amounts.Remove(play.Card, out int amount))
                await Commands.CreatureCmd.Damage(context, Owner, amount, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
        }
    }
    public sealed class DoomPower : PowerModel
    {
        public static async Task DoomKill(IReadOnlyList<Creature> creatures)
        {
            foreach (var creature in creatures)
            { await Task.Yield(); await Commands.CreatureCmd.Kill(creature, null); }
        }
    }
}
namespace MegaCrit.Sts2.Core.Models.Orbs
{
    public sealed class LightningOrb
    {
        public Player Owner = null!;
        public Task Fire(PlayerChoiceContext context, Creature target, bool evoke) => ApplyLightningDamage(3, target, context, evoke);
        private async Task ApplyLightningDamage(decimal amount, Creature target, PlayerChoiceContext context, bool isEvoke)
        {
            await Task.Yield();
            await Commands.CreatureCmd.Damage(context, new[] { target }, isEvoke ? amount + 5 : amount, ValueProp.Unpowered, Owner.Creature);
        }
    }
    public sealed class DarkOrb
    {
        public Player Owner = null!;
        public async Task<IEnumerable<Creature>> Evoke(PlayerChoiceContext context)
        {
            var target = Owner.Creature.CombatState!.Enemies.First(c => !c.IsDead);
            await Task.Yield();
            await Commands.CreatureCmd.Damage(context, target, 6, ValueProp.Unpowered, Owner.Creature);
            return new[] { target };
        }
    }
}
namespace MegaCrit.Sts2.Core.Commands
{
    public static class CreatureCmd
    {
        public static Func<Creature, Task>? BeforeDamage, AfterDamage, BeforeKill, AfterKill;
        public static Func<Creature, Task>? AfterCurrentHpChanged;
        public static bool SealOnVictory;
        public static Task? LastDamageTask, LastKillTask;
        public static Task? LastSetCurrentHpTask;
        public static async Task SetCurrentHp(Creature creature, decimal amount)
        {
            int before = creature.CurrentHp;
            creature.SetCurrentHpInternal(amount);
            if (amount != before && AfterCurrentHpChanged != null) await AfterCurrentHpChanged(creature);
            if (creature.IsDead) await Kill(creature);
        }
        public static Task<IEnumerable<DamageResult>> Damage(PlayerChoiceContext context, Creature target,
            decimal amount, ValueProp props, Creature? dealer, CardModel? card, CardPlay? play) =>
            Damage(context, new[] { target }, amount, props, dealer, card, play);
        public static Task<IEnumerable<DamageResult>> Damage(PlayerChoiceContext context, Creature target,
            decimal amount, ValueProp props, Creature dealer) => Damage(context, new[] { target }, amount, props, dealer, null, null);
        public static Task<IEnumerable<DamageResult>> Damage(PlayerChoiceContext context, IEnumerable<Creature> targets,
            decimal amount, ValueProp props, Creature dealer) => Damage(context, targets, amount, props, dealer, null, null);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Task<IEnumerable<DamageResult>> Damage(PlayerChoiceContext context, Creature target,
            decimal amount, ValueProp props, CardModel? card, CardPlay? play)
        {
            var task = Damage(context, new[] { target }, amount, props, null, card, play);
            LastDamageTask = task; return task;
        }
        public static Task<IEnumerable<DamageResult>> Damage(PlayerChoiceContext context, IEnumerable<Creature> targets,
            decimal amount, ValueProp props, Creature? dealer, CardModel? card, CardPlay? play,
            ModifyDamageHookType modifyDamageHookType = ModifyDamageHookType.All) =>
            Damage(context, targets.Select(t => new DamageTargetData(t)), amount, props, dealer, card, play, modifyDamageHookType);
        public static async Task<IEnumerable<DamageResult>> Damage(PlayerChoiceContext context, IEnumerable<DamageTargetData> targetData,
            decimal amount, ValueProp props, Creature? dealer, CardModel? card, CardPlay? play,
            ModifyDamageHookType modifyDamageHookType = ModifyDamageHookType.All)
        {
            var results = new List<DamageResult>();
            foreach (var data in targetData)
            {
                var target = data.target;
                if (target.IsDead) continue;
                if (BeforeDamage != null) await BeforeDamage(target);
                var hitResults = new List<DamageResult>();
                decimal remaining = amount;
                if (target.PreBlockTarget is { } preBlock)
                {
                    var redirected = preBlock.LoseHpInternal(remaining, props);
                    hitResults.Add(redirected);
                    remaining = redirected.OverkillDamage;
                }
                var receiver = target.RedirectTo ?? target;
                int blocked = props.HasFlag(ValueProp.Unblockable) ? 0 : Math.Min(target.Block, (int)remaining);
                target.Block -= blocked;
                var result = receiver.LoseHpInternal(remaining - blocked, props);
                hitResults.Add(result);
                if (receiver != target)
                {
                    var originalResult = target.LoseHpInternal(result.OverkillDamage, props);
                    originalResult.BlockedDamage = blocked; hitResults.Add(originalResult);
                }
                else result.BlockedDamage = blocked;
                foreach (var item in hitResults)
                {
                    int damage = item.UnblockedDamage + item.OverkillDamage;
                    if (CombatManager.Instance.IsInProgress && !CombatManager.Instance.IsEnding)
                        CombatManager.Instance.History.DamageReceived(target.CombatState!, item.Receiver, dealer, item, card);
                    GC.KeepAlive(damage);
                }
                results.AddRange(hitResults);
                if (AfterDamage != null) await AfterDamage(target);
                if (target.IsDead) await Kill(target);
            }
            return results;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Task Kill(Creature creature, bool force = false)
            => Kill(creature, null, force);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Task Kill(Creature creature, CardModel? cardSource, bool force = false)
        { var task = KillWithoutCheckingWinCondition(creature, cardSource, force); LastKillTask = task; return task; }
        private static async Task KillWithoutCheckingWinCondition(Creature creature, CardModel? cardSource, bool force, int recursion = 0)
        {
            if (BeforeKill != null) await BeforeKill(creature);
            if (creature.CurrentHp > 0) creature.LoseHpInternal(creature.CurrentHp, ValueProp.Unpowered | ValueProp.Unblockable);
            if (AfterKill != null) await AfterKill(creature);
            if (SealOnVictory && CombatManager.Instance.IsEnding)
                BetterSpire2.Journal.Game.JournalService.Seal();
        }
    }
}
namespace BetterSpire2.Journal.Game
{
    internal static class PoisonDamageObserver
    {
        internal static void Reset() { }
        internal static void TagResult(DamageResult result, CombatRecord combat) { }
        internal static bool TryGetCredit(DamageResult result, out PoisonDamageCredit credit) { credit = null!; return false; }
    }
    internal static class JournalService
    {
        internal static readonly HistoryReader Reader = new();
        internal static JournalSession Session = new();
        internal static CombatState State = new();
        private static int _combatId;
        private static CombatManager? _manager;
        private static int _commands;
        private static bool _sealPending;
        internal static void Reset(CombatState state)
        {
            State = state; _manager = CombatManager.Instance; _combatId = _manager.CurrentCombatId;
            _commands = 0; _sealPending = false;
            Reader.Reset(); Session = new JournalSession(); Session.Attach(new RunJournal { Key = "effects", DamageMeterCoverageVersion = 1 });
            foreach (ulong id in new ulong[] { 1, 2 }) Session.RegisterPlayer(new JournalPlayer { Id = id.ToString(), Name = "Player " + id });
            Session.BeginCombat("combat", "Effects", 1, 1, false);
            DamageObservationCoverage.Apply(Session);
            // Drain immediately to make duplicate/late attribution bugs observable.
            _manager.History.Changed = Drain;
        }
        internal static void Drain()
        {
            if (ReferenceEquals(_manager, CombatManager.Instance) && _combatId == _manager.CurrentCombatId)
                Reader.Drain(CombatManager.Instance.History, Session);
        }
        internal static NativeDamageContext? BeginNativeDamage()
        {
            if (Session.Active is not { Outcome: CombatOutcome.InProgress } combat) return null;
            _commands++;
            return new(combat);
        }
        internal static void EndNativeDamage(NativeDamageContext context)
        {
            if (!ReferenceEquals(Session.Active, context.Combat) || _commands <= 0) return;
            _commands--; if (_commands == 0 && _sealPending) Seal();
        }
        internal static void RecordNativeDamage(NativeDamageContext context, DamageResult result, Creature? dealer,
            CardModel? card, EffectDamageCredit? credit)
        {
            if (!ReferenceEquals(Session.Active, context.Combat) ||
                !ReferenceEquals(result.Receiver.CombatState, State) || context.Combat.Outcome != CombatOutcome.InProgress ||
                !ReferenceEquals(_manager, CombatManager.Instance) || _combatId != _manager.CurrentCombatId) return;
            Drain(); Reader.RecordDamage(Session, State.RoundNumber,
                State.CurrentSide == CombatSide.Player ? JournalPhase.Player : JournalPhase.Enemy, result.Receiver, dealer, result, card, credit);
        }
        internal static CombatRecord? CaptureEffectCombat(Creature target) =>
            ReferenceEquals(_manager, CombatManager.Instance) && _combatId == _manager.CurrentCombatId &&
            ReferenceEquals(target.CombatState, State) && target.Side == CombatSide.Enemy &&
            Session.Active?.Outcome == CombatOutcome.InProgress ? Session.Active : null;
        internal static PoisonDamageCredit? CaptureDoom(DoomPower doom)
        { if (CaptureEffectCombat(doom.Owner) == null) return null; Drain(); return Reader.CaptureDoom(doom); }
        internal static void RecordEffectDamage(Creature target, DamageResult result, CombatRecord combat, EffectDamageCredit credit)
        {
            if (!ReferenceEquals(CaptureEffectCombat(target), combat)) return;
            Drain(); Reader.RecordDamage(Session, State.RoundNumber,
                State.CurrentSide == CombatSide.Player ? JournalPhase.Player : JournalPhase.Enemy, target, null, result, null, credit);
        }
        internal static void Seal()
        {
            Drain(); if (_commands > 0) { _sealPending = true; return; }
            _sealPending = false; Session.Complete(CombatOutcome.Won); CombatManager.Instance.IsInProgress = false;
        }
    }
}
