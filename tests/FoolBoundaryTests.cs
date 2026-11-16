#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BetterSpire2.Guardian.Game;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;

/// <summary>v0.113 Fool kit: audited window-inert hooks pass, HP-changing ones stay uncertain.</summary>
internal static class FoolBoundaryTests
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string label)
        { checks++; if (!value) throw new Exception("FAIL Fool forecast boundary: " + label); }
        var warnings = new List<string>();
        var kit = new AbstractModel[]
        {
            new SongBow(), new ImpaledPower(), new AimPower(), new QuietusPower(), new YahPower(),
            new EpicRetrieval(), new CoveringFirePower(), new ExplosiveArrowsPower(), new DislikeYouPower(),
            new ArcheryPower(), new EncorePower(), new AllIsWellPower(), new WhittlingKnife(), new KiVersePower(),
            // Other v0.113 content: simulated (Obsidian Vice, Insatiable Dominion) or window-inert.
            new ObsidianVice(), new Thumbscrew(), new InsatiableDominionPower(), new TemporaryThornsPower(), new Byrdine()
        };
        CapabilityAudit.Inspect(kit, warnings.Add);
        Check(warnings.Count == 0, "audited Fool kit keeps forecasts certain: " + string.Join("; ", warnings));
        Check(CapabilityAudit.HasUnresolvedDeathReaction(new AbstractModel[] { new ImpaledPower() }, new Creature()),
            "Impaled death dislodge still blocks credited poison/Doom kills");

        foreach (var (model, hook) in new (AbstractModel, string)[]
        {
            (new Twine(), "AfterArrowDislodged"), (new SimpletonPower(), "AfterAutoPostPlayPhaseEntered"),
            (new VigorPower(), "BeforeAttack"), (new UnknownArrowReaction(), "AfterArrowDislodged"),
            (new ExplosivePower(), "AfterDeath"), (new ExtraTurnPower(), "ShouldTakeExtraTurn")
        })
        {
            warnings.Clear();
            CapabilityAudit.Inspect(new[] { model }, warnings.Add);
            Check(warnings.Count == 1 && warnings[0].Contains(hook), model.GetType().Name + " remains uncertain");
        }
        return checks;
    }

    private sealed class UnknownArrowReaction : AbstractModel
    { public override Task AfterArrowDislodged() => throw new Exception("Audit must not execute hooks."); }
}

namespace MegaCrit.Sts2.Core.Models
{
    public abstract partial class AbstractModel
    {
        // Names only: the audit matches overrides by name and never invokes them.
        public virtual Task AfterRoomEntered() => Task.CompletedTask;
        public virtual Task AfterDamageReceived() => Task.CompletedTask;
        public virtual Task AfterDeath() => Task.CompletedTask;
        public virtual Task AfterApplied() => Task.CompletedTask;
        public virtual Task BeforeAttack() => Task.CompletedTask;
        public virtual Task AfterAttack() => Task.CompletedTask;
        public virtual Task AfterArrowShot() => Task.CompletedTask;
        public virtual Task AfterArrowImpaled() => Task.CompletedTask;
        public virtual Task AfterArrowDislodged() => Task.CompletedTask;
        public virtual Task BeforeSideTurnStart() => Task.CompletedTask;
        public virtual Task AfterSideTurnStart() => Task.CompletedTask;
        public virtual Task AfterSideTurnEnd() => Task.CompletedTask;
        public virtual Task AfterCardPlayed() => Task.CompletedTask;
        public virtual Task BeforeCardPlayed() => Task.CompletedTask;
        public virtual Task AfterModifyingCardPlayCount() => Task.CompletedTask;
        public virtual Task AfterCardGeneratedForCombat() => Task.CompletedTask;
        public virtual Task AfterShuffle() => Task.CompletedTask;
        public virtual Task AfterAutoPostPlayPhaseEntered() => Task.CompletedTask;
        public virtual Task BeforeSideTurnEnd() => Task.CompletedTask;
        public virtual Task AfterSideTurnEndLate() => Task.CompletedTask;
        public virtual int ModifyAttackHitCount() => 0;
        public virtual bool ShouldTakeExtraTurn() => false;
    }
}
namespace MegaCrit.Sts2.Core.Models.Relics
{
    public sealed class SongBow : AbstractModel { public override Task AfterRoomEntered() => Task.CompletedTask; }
    public sealed class WhittlingKnife : AbstractModel { public override Task AfterShuffle() => Task.CompletedTask; }
    public sealed class Twine : AbstractModel { public override Task AfterArrowDislodged() => Task.CompletedTask; }
    public sealed class ObsidianVice : AbstractModel { public override Task BeforeSideTurnEnd() => Task.CompletedTask; }
    public sealed class Thumbscrew : AbstractModel { public override int ModifyAttackHitCount() => 0; }
    public sealed class Byrdine : AbstractModel
    { public override Task BeforeCombatStart() => Task.CompletedTask; public override Task AfterObtained() => Task.CompletedTask; }
}
namespace MegaCrit.Sts2.Core.Models.Cards
{
    public sealed class EpicRetrieval : AbstractModel { public override Task AfterArrowDislodged() => Task.CompletedTask; }
}
namespace MegaCrit.Sts2.Core.Models.Powers
{
    public sealed class ImpaledPower : AbstractModel
    {
        public override Task AfterApplied() => Task.CompletedTask;
        public override Task AfterDamageReceived() => Task.CompletedTask;
        public override Task AfterDeath() => Task.CompletedTask;
    }
    public sealed class AimPower : AbstractModel
    { public override Task BeforeAttack() => Task.CompletedTask; public override Task AfterAttack() => Task.CompletedTask; }
    public sealed class QuietusPower : AbstractModel
    {
        public override Task AfterArrowShot() => Task.CompletedTask;
        public override Task BeforeAttack() => Task.CompletedTask;
        public override Task AfterAttack() => Task.CompletedTask;
        public override Task AfterSideTurnEnd() => Task.CompletedTask;
    }
    public sealed class YahPower : AbstractModel
    { public override Task AfterArrowDislodged() => Task.CompletedTask; public override Task BeforeSideTurnStart() => Task.CompletedTask; }
    public sealed class CoveringFirePower : AbstractModel { public override Task AfterArrowShot() => Task.CompletedTask; }
    public sealed class ExplosiveArrowsPower : AbstractModel { public override Task AfterArrowImpaled() => Task.CompletedTask; }
    public sealed class DislikeYouPower : AbstractModel { public override Task AfterSideTurnEnd() => Task.CompletedTask; }
    public sealed class ArcheryPower : AbstractModel { public override Task AfterSideTurnStart() => Task.CompletedTask; }
    public sealed class EncorePower : AbstractModel
    {
        public override Task AfterApplied() => Task.CompletedTask;
        public override Task AfterCardPlayed() => Task.CompletedTask;
        public override Task BeforeCardPlayed() => Task.CompletedTask;
        public override Task AfterSideTurnEnd() => Task.CompletedTask;
    }
    public sealed class AllIsWellPower : AbstractModel { public override Task AfterCardGeneratedForCombat() => Task.CompletedTask; }
    public sealed class KiVersePower : AbstractModel
    { public override Task AfterModifyingCardPlayCount() => Task.CompletedTask; public override Task AfterSideTurnEnd() => Task.CompletedTask; }
    public sealed class SimpletonPower : AbstractModel { public override Task AfterAutoPostPlayPhaseEntered() => Task.CompletedTask; }
    public sealed class InsatiableDominionPower : AbstractModel
    {
        public override Task AfterApplied() => Task.CompletedTask;
        public override Task AfterSideTurnStart() => Task.CompletedTask;
        public override Task AfterSideTurnEndLate() => Task.CompletedTask;
    }
    public sealed class TemporaryThornsPower : AbstractModel { public override Task AfterSideTurnEnd() => Task.CompletedTask; }
    public sealed class ExplosivePower : AbstractModel { public override Task AfterDeath() => Task.CompletedTask; }
    public sealed class ExtraTurnPower : AbstractModel { public override bool ShouldTakeExtraTurn() => false; }
    public sealed class VigorPower : AbstractModel
    { public override Task BeforeAttack() => Task.CompletedTask; public override Task AfterAttack() => Task.CompletedTask; }
}
