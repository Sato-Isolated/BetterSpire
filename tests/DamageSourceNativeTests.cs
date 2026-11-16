#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Threading.Tasks;
using BetterSpire2.Journal.Core;

internal static class DamageSourceNativeTests
{

    private static int Main()
    {
        try
        {
            VerifyOneShotForwarding();
            string root = FindRepositoryRoot();
            using var game = MetadataAssembly.Open(Path.Combine(root, "references", "sts2.dll"));
            const string command = "MegaCrit.Sts2.Core.Commands.CreatureCmd";
            const string creatures = "MegaCrit.Sts2.Core.Entities.Creatures.Creature";
            const string context = "MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext";
            const string orbs = "MegaCrit.Sts2.Core.Models.Orbs.";
            const string powers = "MegaCrit.Sts2.Core.Models.Powers.";
            const string judgement = "MegaCrit.Sts2.Core.Models.Cards.Judgement";
            game.RequireUniqueStateField(judgement, "OnPlay", judgement);

            game.RequireCallCount(orbs + "LightningOrb", "ApplyLightningDamage", command, "Damage", 5, 1,
                "one Lightning passive/evoke damage origin");
            game.RequireCallCount(orbs + "DarkOrb", "Evoke", command, "Damage", 5, 1,
                "one Dark Orb evoke damage origin");
            game.RequireCallCount(powers + "ThornsPower", "BeforeDamageReceived", command, "Damage", 7, 1,
                "one Thorns retaliation damage origin");
            game.RequireCallCount(powers + "FlameBarrierPower", "AfterDamageReceived", command, "Damage", 5, 1,
                "one Flame Barrier retaliation damage origin");
            game.RequireMethod(orbs + "LightningOrb", "ApplyLightningDamage", "System.Decimal", creatures,
                context, "System.Boolean");
            game.RequireStateField(orbs + "LightningOrb", "ApplyLightningDamage", "isEvoke", "System.Boolean");
            game.RequireUniqueStateField(orbs + "LightningOrb", "ApplyLightningDamage", "System.Boolean");
            game.RequireUniqueStateField(orbs + "LightningOrb", "ApplyLightningDamage", orbs + "LightningOrb");
            game.RequireUniqueStateField(orbs + "DarkOrb", "Evoke", orbs + "DarkOrb");
            game.RequireUniqueStateField(powers + "ThornsPower", "BeforeDamageReceived", powers + "ThornsPower");
            game.RequireUniqueStateField(powers + "FlameBarrierPower", "AfterDamageReceived", powers + "FlameBarrierPower");
            foreach (var (power, hook) in new[] { ("ExplosiveArrowsPower", "AfterArrowImpaled"), ("ViolentChordPower", "AfterCardPlayed") })
            {
                game.RequireCallCount(powers + power, hook, command, "Damage", 5, 1, "one Fool " + power + " damage origin");
                game.RequireUniqueStateField(powers + power, hook, powers + power);
            }

            using var mod = MetadataAssembly.Open(Path.Combine(root, "bin", "Release", "net9.0", "BetterSpire2Lite.dll"));
            const string observer = "BetterSpire2.Journal.Game.DamageSourceObserver";
            const string patches = "BetterSpire2.Patches.Combat.";
            const string sourceScope = "BetterSpire2.Journal.Core.OneShotDamageScope<BetterSpire2.Journal.Core.EffectDamageCredit>";
            Check(mod.HasMethod(observer, "get_CurrentCredit"), "central command can read effect credit");
            Check(mod.Calls(observer, "EnterCommand", sourceScope, "EnterCommand", 0),
                "central command enters the one-shot source scope");
            Check(mod.Calls("BetterSpire2.Journal.Game.NativeDamageObserver+Command", ".ctor", observer, "EnterCommand", 0) &&
                mod.Calls("BetterSpire2.Journal.Game.NativeDamageObserver+Command", ".ctor", observer, "get_CurrentCredit", 0),
                "central damage command consumes source credit before handling results");
            Check(mod.Calls(observer, "Request", "BetterSpire2.Journal.Game.DamageObservationCoverage", "Require", 2),
                "source capture failures mark journal coverage partial");
            mod.RequireCallCount(observer, "LightningDamage", command, "Damage", 5, 1, "Lightning wrapper preserves one native command");
            mod.RequireCallCount(observer, "DarkOrbEvokeDamage", command, "Damage", 5, 1, "Dark Orb wrapper preserves one native command");
            mod.RequireCallCount(observer, "ThornsDamage", command, "Damage", 7, 1, "Thorns wrapper preserves one native command");
            mod.RequireCallCount(observer, "FlameBarrierDamage", command, "Damage", 5, 1, "Flame Barrier wrapper preserves one native command");
            mod.RequireCallCount(observer, "ExplosiveArrowsDamage", command, "Damage", 5, 1, "Explosive Arrows wrapper preserves one native command");
            mod.RequireCallCount(observer, "ViolentChordDamage", command, "Damage", 5, 1, "Violent Chord wrapper preserves one native command");
            foreach (string wrapper in new[] { "LightningDamage", "DarkOrbEvokeDamage", "ThornsDamage", "FlameBarrierDamage",
                "ExplosiveArrowsDamage", "ViolentChordDamage" })
                Check(mod.Calls(observer, wrapper, observer, "Request", 3), wrapper + " requests one command credit");
            Check(mod.Calls(observer, "Request", sourceScope, "RequestNext", 1), "credit requests are one-shot");

            VerifyOrigin(mod);
            VerifyPatch(mod, patches + "Journal_LightningOrbDamageOrigin_Patch", "LightningDamage", "damage-source:lightning");
            VerifyPatch(mod, patches + "Journal_DarkOrbDamageOrigin_Patch", "DarkOrbEvokeDamage", "damage-source:dark-orb");
            VerifyPatch(mod, patches + "Journal_ThornsDamageOrigin_Patch", "ThornsDamage", "damage-source:thorns");
            VerifyPatch(mod, patches + "Journal_FlameBarrierDamageOrigin_Patch", "FlameBarrierDamage", "damage-source:flame-barrier");
            VerifyPatch(mod, patches + "Journal_ExplosiveArrowsDamageOrigin_Patch", "ExplosiveArrowsDamage", "damage-source:explosive-arrows");
            VerifyPatch(mod, patches + "Journal_ViolentChordDamageOrigin_Patch", "ViolentChordDamage", "damage-source:violent-chord");

            var sourceLiterals = mod.AllLiterals(observer);
            Check(sourceLiterals.Contains("effect:lightning-passive") &&
                sourceLiterals.Contains("effect:lightning-evoke"),
                "Lightning passive and evoke have separate source identities");
            Check(sourceLiterals.Contains("effect:dark-orb-evoke"),
                "Dark Orb evoke has an explicit source identity");
            Check(sourceLiterals.Contains("effect:thorns") && sourceLiterals.Contains("effect:flame-barrier"),
                "retaliation powers have explicit source identities");
            foreach (var pair in new[] {
                ("LightningDamage", "damage-source:lightning"), ("DarkOrbEvokeDamage", "damage-source:dark-orb"),
                ("ThornsDamage", "damage-source:thorns"), ("FlameBarrierDamage", "damage-source:flame-barrier"),
                ("ExplosiveArrowsDamage", "damage-source:explosive-arrows"), ("ViolentChordDamage", "damage-source:violent-chord") })
                Check(mod.Literals(observer, pair.Item1).Contains(pair.Item2), pair.Item1 + " marks its source capability");

            Console.WriteLine($"PASS {Contract.Checks} v0.113 native damage-source metadata checks.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL v0.113 native damage-source metadata: " + ex);
            return 1;
        }
    }

    private static void VerifyPatch(MetadataAssembly mod, string patch, string wrapper, string capability)
    {
        const string origin = "BetterSpire2.Patches.Combat.DamageSourceOrigin";
        Check(mod.Calls(patch, "Prepare", origin, "Prepare", 0) && mod.Calls(patch, "Transpiler", origin, "Transpile", 2),
            patch + " delegates availability and transpiling to its declared origin");
        var declaration = mod.Literals(patch, ".cctor").ToArray();
        Check(declaration.Contains(capability), patch + " declares the stable capability key");
        Check(declaration.Contains(wrapper), patch + " targets its exact source wrapper");
    }

    private static void VerifyOrigin(MetadataAssembly mod)
    {
        const string origin = "BetterSpire2.Patches.Combat.DamageSourceOrigin";
        const string coverage = "BetterSpire2.Journal.Game.DamageObservationCoverage";
        Check(mod.Calls(origin, "Prepare", coverage, "Require", 2), "origin reports patch availability");
        Check(mod.Calls(origin, "Transpile", coverage, "Require", 2), "origin reports whether the wrapper was installed");
        Check(mod.Calls(origin, "Transpile", "BetterSpire2.Patches.Combat.DamageSourceTranspiler", "Replace", 6),
            "origin uses the shared shape-checked transpiler");
    }

    private static void VerifyOneShotForwarding()
    {
        var native = new DamageCommandDouble();
        var thornsGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var flameGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string?> thorns = native.ThornsSingleTarget(thornsGate.Task);
        Check(ReferenceEquals(thorns, native.LastForwardedTask), "source wrapper returns the original single-target damage task");
        Task<string?> flameBarrier = native.FlameBarrierSingleTarget(flameGate.Task);
        Check(ReferenceEquals(flameBarrier, native.LastForwardedTask), "second source wrapper also returns the original damage task");
        thornsGate.SetResult(); flameGate.SetResult();
        Check(thorns.GetAwaiter().GetResult() == "thorns|nested:none", "single-target forwarding carries Thorns source once");
        Check(flameBarrier.GetAwaiter().GetResult() == "flame-barrier|nested:none",
            "interleaved single-target forwarding carries Flame Barrier source");
        Check(native.UnrelatedDamage(Task.CompletedTask).GetAwaiter().GetResult() == "none|nested:none",
            "completed source request cannot leak to unrelated damage");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BetterSpire2Lite.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("BetterSpire repository root");
    }

    private static void Check(bool condition, string label) => Contract.Check(condition, label);

    /// <summary>Models v113's single-target Damage forwarding into the shared async damage command.</summary>
    private sealed class DamageCommandDouble
    {
        private readonly OneShotDamageScope<string> _scope = new();
        internal Task<string?>? LastForwardedTask { get; private set; }

        internal Task<string?> ThornsSingleTarget(Task gate) => RequestAndForward("thorns", gate);
        internal Task<string?> FlameBarrierSingleTarget(Task gate) => RequestAndForward("flame-barrier", gate);
        internal Task<string?> UnrelatedDamage(Task gate) => ForwardToManyTarget(gate);

        private Task<string?> RequestAndForward(string source, Task gate)
        {
            using var pending = _scope.RequestNext(source);
            LastForwardedTask = SingleTargetDamage(gate);
            return LastForwardedTask;
        }

        private Task<string?> SingleTargetDamage(Task gate) => ForwardToManyTarget(gate);

        private async Task<string?> ForwardToManyTarget(Task gate)
        {
            using var command = _scope.EnterCommand();
            string? source = _scope.Current;
            await gate.ConfigureAwait(false);
            using var nested = _scope.EnterCommand();
            return (source ?? "none") + "|nested:" + (_scope.Current ?? "none");
        }
    }

}
