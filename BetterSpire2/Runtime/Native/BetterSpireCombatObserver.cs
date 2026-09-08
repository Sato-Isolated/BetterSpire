#nullable enable
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

namespace BetterSpire2.Runtime.Native;

/// <summary>
/// Read-only native listener. Created canonically by ModelDb, cloned once per combat.
/// It never modifies arguments, resolves choices, draws RNG, sends data, appends stats
/// or executes commands. Only these audited notification hooks are overridden.
/// </summary>
public sealed class BetterSpireCombatObserver : AbstractModel
{
    private CombatManager? _manager;
    private CombatId? _combatId;
    internal CombatState? State { get; private set; }
    internal bool Pending { get; set; }
    public override bool ShouldReceiveCombatHooks => NativeCombatHooks.IsActive(this);

    internal void Bind(CombatState state, CombatManager manager)
    {
        AssertMutable();
        State = state; _manager = manager; _combatId = manager.CurrentCombatId;
        Pending = false;
    }

    internal bool Matches(CombatState state, CombatManager? manager) =>
        ReferenceEquals(State, state) && ReferenceEquals(_manager, manager) &&
        _combatId != null && manager?.CurrentCombatId == _combatId;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        State = null; _manager = null; _combatId = null; Pending = false;
    }

    private Task Changed()
    {
        NativeCombatHooks.Notify(this);
        return Task.CompletedTask;
    }

    public override Task BeforeAttack(AttackCommand command) => Changed();
    public override Task AfterAttack(PlayerChoiceContext choiceContext, AttackCommand command) => Changed();
    public override Task BeforeCardPlayed(CardPlay cardPlay) => Changed();
    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay) => Changed();
    public override Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer,
        DamageResult result, ValueProp props, Creature target, CardModel? cardSource) => Changed();
    public override Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target,
        DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource) => Changed();
    public override Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power,
        decimal amount, Creature? applier, CardModel? cardSource) => Changed();
    public override Task AfterBlockGained(Creature creature, decimal amount, ValueProp props,
        CardModel? cardSource) => Changed();
    public override Task AfterCurrentHpChanged(Creature creature, decimal delta) => Changed();
    public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
        IReadOnlyList<Creature> participants, ICombatState combatState) => Changed();
    public override Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants,
        ICombatState combatState) => Changed();
    public override Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
        IEnumerable<Creature> participants) => Changed();
    public override Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player) => Changed();
}
