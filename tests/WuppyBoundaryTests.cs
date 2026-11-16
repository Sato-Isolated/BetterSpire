#nullable enable
global using BetterSpire2.Core;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using BetterSpire2.Guardian.Game;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;

internal static class WuppyBoundaryTests
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string label)
        { checks++; if (!value) throw new Exception("FAIL Wuppy forecast boundary: " + label); }
        var warnings = new List<string>();
        var native = new AbstractModel[] { new WuppysLoyaltyPower(), new Wuppy() };
        CapabilityAudit.Inspect(native, warnings.Add);
        Check(warnings.Count == 0, "native redirect and relic bookkeeping do not downgrade a simulated Wuppy forecast");
        CapabilityAudit.Inspect(native, warnings.Add);
        Check(warnings.Count == 0, "cached native audit remains supported");
        Check(!CapabilityAudit.HasUnresolvedDeathReaction(native, new Creature()), "native Wuppy adds no unhandled death reaction");

        CapabilityAudit.Inspect(new AbstractModel[] { new UnknownRedirect() }, warnings.Add);
        Check(warnings.Count == 1 && warnings[0].Contains("ModifyPreBlockDamageTarget"), "unknown native redirects remain uncertain");
        warnings.Clear();
        CapabilityAudit.Inspect(new AbstractModel[] { new UnknownHpObserver() }, warnings.Add);
        Check(warnings.Count == 1 && warnings[0].Contains("AfterCurrentHpChanged"), "Wuppy HP bookkeeping exemption is specific to that relic");
        warnings.Clear();
        CapabilityAudit.Inspect(new AbstractModel[] { new UnknownSummoner() }, warnings.Add);
        Check(warnings.Count == 1, "other model obtain/combat-start reactions remain guarded");

        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ExternalWuppyBoundary"), AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule("Mod").DefineType("ThirdParty.WuppysLoyaltyPower",
            TypeAttributes.Public, typeof(WuppysLoyaltyPower));
        type.DefineDefaultConstructor(MethodAttributes.Public);
        var external = (AbstractModel)Activator.CreateInstance(type.CreateType()!)!;
        warnings.Clear();
        CapabilityAudit.Inspect(new[] { external }, warnings.Add);
        Check(warnings.Count == 1 && warnings[0].Contains("Uncertified external mod"), "external model cannot borrow Wuppy's native short-name whitelist");
        return checks;
    }

    private sealed class UnknownRedirect : AbstractModel
    {
        public override Creature? ModifyPreBlockDamageTarget(Creature originalTarget, Creature? target,
            decimal amount, ValueProp props, Creature? dealer) => throw new Exception("Audit must not execute hooks.");
    }
    private sealed class UnknownHpObserver : AbstractModel
    {
        public override Task AfterCurrentHpChanged(Creature creature, decimal delta) => throw new Exception("Audit must not execute hooks.");
    }
    private sealed class UnknownSummoner : AbstractModel
    {
        public override Task BeforeCombatStart() => throw new Exception("Audit must not execute hooks.");
        public override Task AfterObtained() => throw new Exception("Audit must not execute hooks.");
    }
}

namespace MegaCrit.Sts2.Core.Models
{
    public abstract partial class AbstractModel
    {
        public virtual Creature? ModifyPreBlockDamageTarget(Creature originalTarget, Creature? target,
            decimal amount, ValueProp props, Creature? dealer) => target;
        public virtual Task AfterCurrentHpChanged(Creature creature, decimal delta) => Task.CompletedTask;
        public virtual Task BeforeCombatStart() => Task.CompletedTask;
        public virtual Task AfterObtained() => Task.CompletedTask;
    }
}
namespace MegaCrit.Sts2.Core.Models.Powers
{
    public class WuppysLoyaltyPower : AbstractModel
    {
        public override Creature? ModifyPreBlockDamageTarget(Creature originalTarget, Creature? target,
            decimal amount, ValueProp props, Creature? dealer) => throw new Exception("Audit must not execute hooks.");
    }
}
namespace MegaCrit.Sts2.Core.Models.Relics
{
    public sealed class Wuppy : AbstractModel
    {
        public override Task BeforeCombatStart() => throw new Exception("Audit must not execute hooks.");
        public override Task AfterObtained() => throw new Exception("Audit must not execute hooks.");
        public override Task AfterCurrentHpChanged(Creature creature, decimal delta) => throw new Exception("Audit must not execute hooks.");
    }
}
namespace MegaCrit.Sts2.Core.Entities.Creatures { public sealed class Creature { } }
namespace MegaCrit.Sts2.Core.ValueProps { public enum ValueProp { } }
namespace BetterSpire2.Core { internal static class ModText { internal static bool IsFrench => false; } }
namespace BetterSpire2.Guardian.Game
{
    internal static class GameForecastAdapter { internal static string Source(AbstractModel model) => model.GetType().Name; }
}
namespace BetterSpire2.Runtime.Native { internal sealed class BetterSpireCombatObserver : AbstractModel { } }
namespace MegaCrit.Sts2.Core.Modding
{
    public static class AssemblyInfo
    {
        public static Mod? ModForType(Type type, out bool isBaseGame)
        { isBaseGame = type.Assembly == typeof(AbstractModel).Assembly; return null; }
    }
    public sealed class Mod { public ModManifest? manifest => null; }
    public sealed class ModManifest { public string? name => null; public string? id => null; }
}
