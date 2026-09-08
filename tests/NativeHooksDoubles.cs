#nullable enable
// Minimal doubles for the source-linked observer. Metadata contracts additionally
// verify actual v111 signatures. These are not a game simulation or network test.
global using BetterSpire2.Core;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;


namespace BetterSpire2.Core
{
    public static class ModLog { public static int Errors; public static void Error(string name, Exception error) => Errors++; }
    public static class ModText { public static bool IsFrench => false; }
}
namespace BetterSpire2.Guardian.Game
{ internal static class GameForecastAdapter { internal static string Source(AbstractModel model) => model.GetType().Name; } }
namespace MegaCrit.Sts2.Core.Combat
{
    public readonly record struct CombatId(int Id);
    public enum CombatSide { Player, Enemy }
    public interface ICombatState { }
    public sealed class CombatState : ICombatState { }
    public sealed class CombatManager
    {
        public static CombatManager Instance { get; set; } = new();
        public CombatId? CurrentCombatId { get; set; }
        public bool IsStarting { get; set; }
        public bool IsInProgress { get; set; }
        public bool IsOverOrEnding { get; set; }
    }
}
namespace BetterSpire2.Runtime
{ internal static class CombatLifecycle { internal static CombatState? CurrentState { get; set; } } }
namespace MegaCrit.Sts2.Core.Commands.Builders { public sealed class AttackCommand { public int Sentinel; } }
namespace MegaCrit.Sts2.Core.Entities.Cards { public sealed class CardPlay { } }
namespace MegaCrit.Sts2.Core.Entities.Creatures { public sealed class Creature { } public sealed class DamageResult { } }
namespace MegaCrit.Sts2.Core.Entities.Players { public sealed class Player { } }
namespace MegaCrit.Sts2.Core.ValueProps { public enum ValueProp { Move, Unpowered, Unblockable } }
namespace MegaCrit.Sts2.Core.GameActions.Multiplayer
{ public sealed class PlayerChoiceContext { public List<AbstractModel> Models { get; } = new(); } }
namespace MegaCrit.Sts2.Core.Models
{
    public abstract class AbstractModel
    {
        public bool IsMutable { get; private set; }
        public abstract bool ShouldReceiveCombatHooks { get; }
        public void AssertMutable() { if (!IsMutable) throw new InvalidOperationException("Canonical model"); }
        public AbstractModel MutableClone() { var m = (AbstractModel)MemberwiseClone(); m.IsMutable = true; m.AfterCloned(); return m; }
        protected virtual void AfterCloned() { }
        public virtual Task AfterCombatVictory(object room) => Task.CompletedTask;
    public virtual Task BeforeAttack(AttackCommand command) => Task.CompletedTask;
    public virtual Task AfterAttack(PlayerChoiceContext choiceContext, AttackCommand command) => Task.CompletedTask;
    public virtual Task BeforeCardPlayed(CardPlay cardPlay) => Task.CompletedTask;
    public virtual Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay) => Task.CompletedTask;
    public virtual Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer,
        DamageResult result, ValueProp props, Creature target, CardModel? cardSource) => Task.CompletedTask;
    public virtual Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target,
        DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource) => Task.CompletedTask;
    public virtual Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power,
        decimal amount, Creature? applier, CardModel? cardSource) => Task.CompletedTask;
    public virtual Task AfterBlockGained(Creature creature, decimal amount, ValueProp props,
        CardModel? cardSource) => Task.CompletedTask;
    public virtual Task AfterCurrentHpChanged(Creature creature, decimal delta) => Task.CompletedTask;
    public virtual Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
        IReadOnlyList<Creature> participants, ICombatState combatState) => Task.CompletedTask;
    public virtual Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants,
        ICombatState combatState) => Task.CompletedTask;
    public virtual Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
        IEnumerable<Creature> participants) => Task.CompletedTask;
    public virtual Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player) => Task.CompletedTask;
    }
    public sealed class CardModel : AbstractModel { public override bool ShouldReceiveCombatHooks => true; }
    public sealed class PowerModel : AbstractModel { public override bool ShouldReceiveCombatHooks => true; }
    public static class ModelDb
    {
        public static BetterSpire2.Runtime.Native.BetterSpireCombatObserver? Canonical;
        public static int Lookups;
        public static void Initialize() => Canonical = new();
        public static string GetId<T>() where T : AbstractModel => typeof(T).Name;
        public static T GetById<T>(string id) where T : AbstractModel
        { Lookups++; return (T)(AbstractModel)(Canonical ?? throw new InvalidOperationException("Model unavailable")); }
    }
}
namespace MegaCrit.Sts2.Core.Models.Relics
{
    public sealed class BurningBlood : AbstractModel
    { public override bool ShouldReceiveCombatHooks => true; public override Task AfterCombatVictory(object room) => Task.CompletedTask; }
    public sealed class UnknownVictoryRelic : AbstractModel
    { public override bool ShouldReceiveCombatHooks => true; public override Task AfterCombatVictory(object room) => Task.CompletedTask; }
}
namespace External
{ public sealed class BurningBlood : AbstractModel
  { public override bool ShouldReceiveCombatHooks => true; public override Task AfterCombatVictory(object room) => Task.CompletedTask; } }
namespace MegaCrit.Sts2.Core.Modding
{
    public static class ModHelper
    {
        private static Func<CombatState, IEnumerable<AbstractModel>>? provider;
        public static int Registrations;
        public static void SubscribeForCombatStateHooks(string id, Func<CombatState, IEnumerable<AbstractModel>> del)
        { if (provider != null) throw new Exception("Duplicate subscription"); provider = del; Registrations++; }
        public static IEnumerable<AbstractModel> Provide(CombatState state) => provider?.Invoke(state) ?? Array.Empty<AbstractModel>();
    }
    public sealed class ModManifest { public string name = "external"; public string id = "external"; }
    public sealed class Mod { public ModManifest manifest = new(); }
    public static class AssemblyInfo
    {
        public static Mod? ModForType(Type type, out bool isBaseGame)
        { isBaseGame = type.Namespace?.StartsWith("MegaCrit.", StringComparison.Ordinal) == true; return isBaseGame ? null : new Mod(); }
    }
}
