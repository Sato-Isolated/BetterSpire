#nullable enable
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Threading.Tasks;
using BetterSpire2.DamageMeter.Core;
using BetterSpire2.Journal.Core;
using BetterSpire2.Journal.Game;
using BetterSpire2.Patches.Combat;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.ValueProps;

internal static class EffectDamageTests
{
    private static int checks;
    private static readonly PlayerChoiceContext Context = new();
    private const ValueProp Props = ValueProp.Unpowered | ValueProp.Unblockable;
    private static void Check(bool value, string label)
    { checks++; if (!value) throw new Exception(label); }
    private static Creature Begin(int hp = 100, bool lastEnemy = false)
    {
        CreatureCmd.BeforeDamage = CreatureCmd.AfterDamage = CreatureCmd.BeforeKill = CreatureCmd.AfterKill = null;
        CreatureCmd.AfterCurrentHpChanged = null;
        CreatureCmd.SealOnVictory = false;
        CombatManager.Instance = new();
        var state = new CombatState();
        var target = Enemy(state, hp);
        if (!lastEnemy) Enemy(state, 100);
        JournalService.Reset(state);
        return target;
    }
    private static Creature Enemy(CombatState state, int hp)
    {
        var target = new Creature { CombatState = state, Side = CombatSide.Enemy, CurrentHp = hp };
        state.Enemies.Add(target); return target;
    }
    private static Player Player(ulong id)
    {
        var player = new Player { NetId = id };
        player.Creature = new Creature { Side = CombatSide.Player, Player = player, CombatState = JournalService.State };
        return player;
    }
    private static StranglePower Strangle(Creature target, Player player, int amount) =>
        new() { Owner = target, Applier = player.Creature, Amount = amount, Title = new("Strangulation") };
    private static async Task Play(StranglePower power, Player player)
    {
        var play = new CardPlay { Card = new CardModel { Owner = player } };
        power.BeforeCardPlayed(play); await power.AfterCardPlayed(Context, play);
    }
    private static DoomPower Doom(Creature target, int a, int b = 0, int unknown = 0)
    {
        var power = new DoomPower { Owner = target, Amount = a + b + unknown, Title = new("Trépas") };
        target.Powers.Add(power);
        if (a > 0) Apply(power, Player(1), a);
        if (b > 0) Apply(power, Player(2), b);
        // Unknown stacks deliberately have no historical provenance (late attach).
        return power;
    }
    private static void Apply(PowerModel power, Player player, int amount) =>
        CombatManager.Instance.History.Add(new PowerReceivedEntry { Power = power, Applier = player.Creature, Amount = amount });
    private static decimal Damage(ulong id) => DamageMeterBuilder.BuildLive(JournalService.Session, false).Rows
        .Where(r => r.Id == id.ToString()).Sum(r => r.Damage);
    private static decimal Unknown() => DamageMeterBuilder.BuildLive(JournalService.Session, false).UnattributedDamage;
    private static decimal Total() => Damage(1) + Damage(2) + Unknown();

    internal static async Task Run()
    {
        // Reproduce the missing ownership and missing event before applying fixes.
        var target = Begin(); var player = Player(1);
        await Play(Strangle(target, player, 10), player);
        Check(Damage(1) == 0 && Unknown() == 10, "unpatched Strangle is missing from player damage");
        target = Begin(7); Doom(target, 10);
        await DoomPower.DoomKill(new[] { target });
        Check(Total() == 0 && target.IsDead, "unpatched Doom kill has no damage history");
        target = Begin(7); var judgement = new Judgement { Owner = Player(1) };
        await judgement.Play(Context,new CardPlay { Card = judgement, Target = target });
        Check(Total() == 0 && target.IsDead,"unpatched v113 Judgement execution has no damage result or history");

        var harmony = new Harmony("betterspire.effect-regression-tests");
        foreach (var patch in new[] { typeof(Journal_EffectCommandScope_Patch), typeof(Journal_EffectHpResult_Patch),
            typeof(Journal_StrangleDamageOrigin_Patch), typeof(Journal_DoomKillOrigin_Patch) })
            harmony.CreateClassProcessor(patch).Patch();
        try
        {
            await StrangleAccounting();
            await DoomAccounting();
            await Isolation();
            foreach (var patch in new[] { typeof(Journal_NativeDamageScope_Patch), typeof(Journal_NativeDamageResults_Patch),
                typeof(Journal_DoomBatchScope_Patch) })
                harmony.CreateClassProcessor(patch).Patch();
            await FinalBlows();
            await CompletedResults();
            await V113DamageTargets();
            await DoomBatch();
            foreach (var patch in new[] { typeof(Journal_LightningOrbDamageOrigin_Patch), typeof(Journal_DarkOrbDamageOrigin_Patch),
                typeof(Journal_ThornsDamageOrigin_Patch), typeof(Journal_FlameBarrierDamageOrigin_Patch),
                typeof(Journal_ExplosiveArrowsDamageOrigin_Patch), typeof(Journal_ViolentChordDamageOrigin_Patch) })
                harmony.CreateClassProcessor(patch).Patch();
            await DamageSources();
            foreach (var patch in new[] { typeof(Journal_JudgementDamageOrigin_Patch), typeof(Journal_JudgementCommandScope_Patch),
                typeof(Journal_JudgementHpResult_Patch) })
                harmony.CreateClassProcessor(patch).Patch();
            harmony.Patch(JudgementNativeMethods.SetCurrentHp()!,postfix:new HarmonyMethod(typeof(EffectDamageTests),nameof(CaptureSetCurrentHpTask)));
            await JudgementAccounting();
            Check(ModLog.Errors.Count == 0, "effect observer and patch paths never logged failures: " + string.Join("; ", ModLog.Errors));
            CoverageFailure();
            Console.WriteLine($"PASS {checks} damage collection/source native-boundary checks with real Harmony patches (game doubles, not Godot).");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    private static void CaptureSetCurrentHpTask(Task __result) => CreatureCmd.LastSetCurrentHpTask = __result;
    private static Task Judge(Creature target, ulong owner = 1)
    {
        var card = new Judgement { Owner = Player(owner) };
        return card.Play(Context,new CardPlay { Card = card, Target = target });
    }
    private static async Task JudgementAccounting()
    {
        var target = Begin(17); target.Block = 99;
        await Judge(target,2);
        var trace = JournalService.Session.Active!.DamageTrace.Single();
        Check(Damage(2) == 17 && Unknown() == 0 && target.Block == 99,"Judgement counts actual enemy HP for its card owner without consuming block");
        Check(trace.Hp == 17 && trace.Blocked == 0 && trace.Overkill == 0 && trace.Fatal &&
            trace.SourceId.StartsWith("card:JUDGEMENT:") && trace.Attribution == DamageAttributionKind.CardOwner,
            "Judgement source timeline uses card ownership and zero block/overkill");
        JournalService.Drain(); JournalService.Drain(); await Judge(target,2);
        Check(Damage(2) == 17 && JournalService.Session.Active.DamageTrace.Count == 1 &&
            CombatManager.Instance.History.Entries.Count == 0,"Judgement adds no native history and a zero-HP target cannot count twice");

        target = Begin(31); await Judge(target);
        Check(target.CurrentHp == 31 && Total() == 0,"native Judgement threshold remains authoritative");
        target = Begin(7,lastEnemy:true); CreatureCmd.SealOnVictory = true;
        var card = new Judgement { Owner = Player(1) };
        var task = JudgementDamageObserver.SetCurrentHp(target,0,card);
        Check(ReferenceEquals(task,CreatureCmd.LastSetCurrentHpTask),"Judgement wrapper returns the exact native task");
        await task;
        Check(Damage(1) == 7 && JournalService.Session.Active!.Outcome == CombatOutcome.Won &&
            JournalService.Session.Active.DamageTrace.Count == 1,"last-enemy Judgement recorded before native victory seals the journal");

        target = Begin(8);
        target.CurrentHpChanged = (_,_) => target.CurrentHp = 3;
        await Judge(target);
        Check(Damage(1) == 8 && target.CurrentHp == 3,"synchronous setter resurrection preserves the full HP removed by Judgement");
        target = Begin(8);
        CreatureCmd.AfterKill = creature => { creature.CurrentHp = 3; return Task.CompletedTask; };
        await Judge(target);
        Check(Damage(1) == 8 && target.CurrentHp == 3,"native death-reaction resurrection remains intact after Judgement accounting");

        target = Begin(8); var nested = Enemy(JournalService.State,5);
        target.CurrentHpChanged = (_,_) => CreatureCmd.SetCurrentHp(nested,0).GetAwaiter().GetResult();
        await Judge(target);
        Check(Damage(1) == 8 && Total() == 8 && nested.IsDead,"synchronous nested HP setter cannot inherit Judgement provenance");
        target = Begin(8);
        CreatureCmd.AfterCurrentHpChanged = async creature =>
        {
            CreatureCmd.AfterCurrentHpChanged = null; creature.CurrentHp = 5;
            await Task.Yield(); await CreatureCmd.SetCurrentHp(creature,0);
        };
        await Judge(target);
        Check(Total() == 8 && JournalService.Session.Active!.DamageTrace.Count == 1,"asynchronous nested same-target setter cannot reuse Judgement provenance");
        target = Begin(8); nested = Enemy(JournalService.State,5);
        CreatureCmd.AfterCurrentHpChanged = async _ =>
        { CreatureCmd.AfterCurrentHpChanged = null; await Judge(nested,2); };
        await Judge(target);
        Check(Damage(1) == 8 && Damage(2) == 5,"nested Judgement executions retain independent card owners");

        target = Begin(7); await CreatureCmd.SetCurrentHp(target,0);
        Check(Total() == 0,"HP setters outside Judgement do not create damage");
        target = Begin(7); await JudgementDamageObserver.SetCurrentHp(target,2,new Judgement { Owner = Player(1) });
        Check(Total() == 0 && target.CurrentHp == 2,"unexpected nonzero Judgement setter preserves native behavior without invented damage");
        target = Begin(7); await Judge(target,9);
        Check(Unknown() == 7 && Damage(1) == 0 && Damage(2) == 0,"unknown Judgement owner stays unattributed");

        target = Begin(7); var failure = new InvalidOperationException("native setter subscriber failure");
        target.CurrentHpChanged = (_,_) => throw failure;
        Exception? received = null;
        try { await Judge(target); } catch(Exception ex) { received = ex; }
        Check(ReferenceEquals(received,failure) && Total() == 0 &&
            JournalService.Session.Active!.PartialReasons.Contains("damage.interrupted"),"native setter failure is preserved and incomplete observation disclosed");
        target = Begin(7); failure = new InvalidOperationException("native HP-change hook failure");
        CreatureCmd.AfterCurrentHpChanged = _ => Task.FromException(failure);
        received = null;
        try { await Judge(target); } catch(Exception ex) { received = ex; }
        Check(ReferenceEquals(received,failure) && Total() == 7,"native post-setter fault preserves the already observed HP removal");

        target = Begin(7); target.CurrentHpChanged = (_,_) => Begin();
        await Judge(target);
        Check(Total() == 0,"synchronous combat replacement rejects stale Judgement observation");
        target = Begin(7); CreatureCmd.SealOnVictory = true;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CreatureCmd.AfterCurrentHpChanged = _ => gate.Task;
        task = Judge(target);
        Begin(); gate.SetResult(); await task;
        Check(Total() == 0 && JournalService.Session.Active!.Outcome == CombatOutcome.InProgress,
            "suspended Judgement completion cannot write to or seal a replacement combat");
    }

    private static async Task FinalBlows()
    {
        var target = Begin(7, lastEnemy: true); var player = Player(1);
        target.Block = 4;
        CreatureCmd.SealOnVictory = true;
        await CreatureCmd.Damage(Context, new[] { target }, 20, ValueProp.Unpowered, player.Creature, null, null);
        Check(Damage(1) == 7, "ordinary final blow retained when native history skips ending combat");
        var trace = JournalService.Session.Active!.DamageTrace.Single();
        Check(trace.Blocked == 4 && trace.Overkill == 9, "final blow observed after block metadata populated");
        Check(CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>().Count() == 0,
            "supplemental collector never creates native history entries");
    }

    private static async Task CompletedResults()
    {
        var target = Begin(); var player = Player(1);
        await CreatureCmd.Damage(Context, new[] { target }, 10, Props, player.Creature, null, null);
        JournalService.Drain();
        Check(Damage(1) == 10 && JournalService.Session.Active!.DamageTrace.Count == 1,
            "history and supplemental observation share result identity deduplication");
        await CreatureCmd.Damage(Context, new[] { target }, 10, Props, player.Creature, null, null);
        Check(Damage(1) == 20 && JournalService.Session.Active!.DamageTrace.Count == 2,
            "identical amounts on distinct hits are not deduplicated");

        target = Begin(5, lastEnemy: true); player = Player(1);
        var second = Enemy(JournalService.State, 8);
        CreatureCmd.SealOnVictory = true;
        await CreatureCmd.Damage(Context, new[] { target, second }, 10, Props, player.Creature, null, null);
        Check(Damage(1) == 13 && JournalService.Session.Active!.DamageTrace.Count == 2,
            "area damage includes last receiver after history starts omitting results");
        Check(JournalService.Session.Active!.Outcome == CombatOutcome.Won, "seal finishes after command observations");

        target = Begin(); player = Player(1);
        target.Block = 10;
        await CreatureCmd.Damage(Context, new[] { target }, 10, ValueProp.Unpowered, player.Creature, null, null);
        Check(Damage(1) == 0 && DamageMeterBuilder.BuildLive(JournalService.Session, true).AttributedDamage == 10,
            "fully blocked results captured after block metadata");

        target = Begin(); player = Player(1);
        var nested = Enemy(JournalService.State, 6);
        bool entered = false;
        CreatureCmd.BeforeDamage = async _ =>
        {
            if (entered) return; entered = true; await Task.Yield();
            await CreatureCmd.Damage(Context, new[] { nested }, 9, Props, Player(2).Creature, null, null);
        };
        await CreatureCmd.Damage(Context, new[] { target }, 7, Props, player.Creature, null, null);
        Check(Damage(1) == 7 && Damage(2) == 6, "nested asynchronous damage does not inherit outer dealer");

        target = Begin(); player = Player(1);
        CreatureCmd.BeforeDamage = async _ =>
        { await Task.Yield(); JournalService.State.RoundNumber = 2; JournalService.State.CurrentSide = CombatSide.Enemy; };
        await CreatureCmd.Damage(Context, new[] { target }, 5, Props, player.Creature, null, null);
        Check(JournalService.Session.Active!.DamageTrace.Single() is { Round: 2, Phase: JournalPhase.Enemy },
            "supplemental result uses native round and side at observation, not command entry");

        target = Begin(); player = Player(1);
        CreatureCmd.BeforeDamage = _ => Task.FromException(new InvalidOperationException("native failure"));
        bool faulted = false;
        try { await CreatureCmd.Damage(Context, new[] { target }, 10, Props, player.Creature, null, null); }
        catch (InvalidOperationException ex) { faulted = ex.Message == "native failure"; }
        Check(faulted && Total() == 0, "native fault reaches caller without fabricated damage");
        Check(JournalService.Session.Active!.PartialReasons.Contains("damage.interrupted"),
            "native interruption discloses potentially missing results");
        CreatureCmd.BeforeDamage = null;
        JournalService.Seal();
        Check(JournalService.Session.Active!.Outcome == CombatOutcome.Won, "faulted task releases command lifetime");

        target = Begin(); player = Player(1);
        CreatureCmd.BeforeDamage = async _ => { await Task.Yield(); CombatManager.Instance.CurrentCombatId++; };
        await CreatureCmd.Damage(Context, new[] { target }, 10, Props, player.Creature, null, null);
        Check(JournalService.Session.Active!.DamageTrace.Count == 0, "stale asynchronous context is discarded");
    }

    private static async Task V113DamageTargets()
    {
        var target = Begin(7, lastEnemy: true); var player = Player(1);
        CreatureCmd.SealOnVictory = true;
        await CreatureCmd.Damage(Context, new[] { new DamageTargetData(target) }, 10, Props,
            player.Creature, null, null, ModifyDamageHookType.Cap);
        Check(Damage(1) == 7 && JournalService.Session.Active!.DamageTrace.Count == 1,
            "direct v113 target-data overload captures final blow exactly once");

        Begin(); player = Player(1);
        var pet = new Creature { CombatState = JournalService.State, Side = CombatSide.Player,
            PetOwner = player, CurrentHp = 4 };
        player.Creature.PreBlockTarget = pet; player.Creature.Block = 5;
        await CreatureCmd.Damage(Context, new[] { player.Creature }, 6, ValueProp.Unpowered,
            null, null, null);
        JournalService.Drain();
        Check(pet.CurrentHp == 0 && player.Creature.CurrentHp == 100 && player.Creature.Block == 3,
            "v113 pre-block redirection consumes pet HP before player block");
        Check(JournalService.Session.Active!.DamageTrace.Sum(e => e.Hp) == 4 &&
            JournalService.Session.Active.DamageTrace.Count == 2 && Total() == 0,
            "redirected pet and blocked player results counted once without crediting enemy damage");
    }

    private static void CoverageFailure()
    {
        Begin();
        var unexpected = new[] { new CodeInstruction(OpCodes.Ret) };
        var transpiler = typeof(Journal_NativeDamageResults_Patch).GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { unexpected })!).ToArray();
        Check(result.Length == 1 && result[0].opcode == OpCodes.Ret, "unknown IL stays unchanged");
        Check(JournalService.Session.Active!.Partial && JournalService.Session.Active.PartialReasons.Contains("damage.results"),
            "unavailable patch marks active combat partial with persisted reason");
        Begin();
        Check(JournalService.Session.Active!.Partial, "unavailable patch also marks subsequent combat partial");
        DamageObservationCoverage.Require("damage.results", true);
        Begin();
        Check(!JournalService.Session.Active!.Partial, "restored capability does not mark next combat partial");
        ModLog.Errors.Clear();
    }

    private static async Task DoomBatch()
    {
        var first = Begin(4, lastEnemy: true);
        // A secondary creature need not keep the native encounter alive.
        var secondary = new Creature { CombatState = JournalService.State, Side = CombatSide.Enemy, CurrentHp = 3 };
        Doom(first, 9); Doom(secondary, 9);
        CreatureCmd.SealOnVictory = true;
        await DoomPower.DoomKill(new[] { first, secondary });
        Check(Damage(1) == 7, "Doom batch retains secondary kills after primary victory");
        Check(JournalService.Session.Active!.Outcome == CombatOutcome.Won, "Doom batch seals when all native kills return");
    }

    private static async Task DamageSources()
    {
        var target = Begin(); var player = Player(2);
        var lightning = new LightningOrb { Owner = player };
        await lightning.Fire(Context, target, false);
        await lightning.Fire(Context, target, true);
        await new DarkOrb { Owner = player }.Evoke(Context);
        var thorns = new ThornsPower { Owner = player.Creature, Amount = 4 };
        await thorns.BeforeDamageReceived(Context, player.Creature, 10, Props, target, null);
        var flame = new FlameBarrierPower { Owner = player.Creature, Amount = 5 };
        await flame.AfterDamageReceived(Context, player.Creature, new DamageResult(player.Creature), Props, target, null);
        var second = Enemy(JournalService.State, 50);
        await new ExplosiveArrowsPower { Owner = player.Creature, Amount = 7, Targets = new[] { target, second } }
            .AfterArrowImpaled(new ImpaledPower(), new ArrowSlot(), Context, null);
        await new ViolentChordPower { Owner = player.Creature, Amount = 2, Targets = new[] { target } }
            .AfterCardPlayed(Context, new CardPlay());
        var sources = JournalService.Session.Query(JournalScope.Combat, "combat", 1, "2").Sources;
        foreach (var (id, amount) in new[] { ("effect:lightning-passive", 3), ("effect:lightning-evoke", 8),
            ("effect:dark-orb-evoke", 6), ("effect:thorns", 4), ("effect:flame-barrier", 5),
            ("effect:explosive-arrows", 14), ("effect:violent-chord", 2) })
            Check(sources.TryGetValue(id, out var source) && source.Stats[Stat.DamageDealtHp] == amount,
                "real source observer + Harmony origin attributes " + id);
        Check(Damage(1) == 0 && Damage(2) == 42 && Unknown() == 0, "all effect sources credit their actual player");

        target = Begin(2, lastEnemy: true); player = Player(2);
        CreatureCmd.SealOnVictory = true;
        await new LightningOrb { Owner = player }.Fire(Context, target, true);
        Check(Damage(2) == 2 && JournalService.Session.Active!.DamageTrace.Single().SourceId == "effect:lightning-evoke",
            "lethal source metadata survives missing native history");

        target = Begin(); player = Player(2);
        var nestedTarget = Enemy(JournalService.State, 20);
        bool entered = false;
        CreatureCmd.BeforeDamage = async _ =>
        {
            if (entered) return; entered = true;
            await CreatureCmd.Damage(Context, new[] { nestedTarget }, 2, Props, Player(1).Creature, null, null);
        };
        await new LightningOrb { Owner = player }.Fire(Context, target, false);
        Check(JournalService.Session.Query(JournalScope.Combat, "combat", 1, "1").Sources.ContainsKey("effect:unknown") &&
            !JournalService.Session.Query(JournalScope.Combat, "combat", 1, "1").Sources.ContainsKey("effect:lightning-passive"),
            "nested ordinary damage cannot inherit an outer lightning source");
        Check(Damage(1) == 2 && Damage(2) == 3, "nested and outer source results conserve player totals");
    }

    private static async Task StrangleAccounting()
    {
        var target = Begin(); var a = Player(1); var b = Player(2);
        var strangle = Strangle(target, a, 10);
        await Play(strangle, a);
        Check(Damage(1) == 10 && Unknown() == 0 && target.CurrentHp == 90, "Strangle credited to its native applier");
        Check(CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>().Count() == 1,
            "observer does not add invented native history");
        JournalService.Drain(); JournalService.Drain();
        Check(Damage(1) == 10 && JournalService.Session.Active!.DamageTrace.Count == 1,
            "supplemental plus native history counts the same result once");
        await Play(strangle, b);
        Check(Total() == 10, "another player's card does not trigger this applier's Strangle");
        await Play(Strangle(target, b, 6), b);
        Check(Damage(1) == 10 && Damage(2) == 6, "independent appliers keep independent Strangle damage");
        var play = new CardPlay { Card = new CardModel { Owner = a } };
        strangle.BeforeCardPlayed(play); strangle.Amount = 30;
        await strangle.AfterCardPlayed(Context, play);
        Check(Damage(1) == 20, "stack changes after BeforeCardPlayed do not inflate the native result");
        Check(JournalService.Session.Active!.DamageTrace.All(e => e.SourceId == "effect:strangle" &&
            e.Attribution == DamageAttributionKind.PowerApplier), "Strangle named and attributed in F5 timeline");
        Check(JournalService.Session.Query(JournalScope.Combat, "combat", 1, "1").Sources["effect:strangle"].Name == "Strangulation",
            "Strangle named in source totals");

        target = Begin(3, lastEnemy: true); a = Player(1); CreatureCmd.SealOnVictory = true;
        await Play(Strangle(target, a, 10), a);
        Check(Damage(1) == 3 && Unknown() == 0, "lethal Strangle counts actual HP, not overkill");
        var fight = JournalService.Session.Run!.Combats.Single();
        Check(fight.Outcome == CombatOutcome.Won && fight.DamageTrace.Single().Overkill == 7,
            "last enemy damage recorded before native victory seals the journal");
        Check(!CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>().Any(),
            "last-hit accounting survives native history omission");

        target = Begin(); a = Player(9);
        await Play(Strangle(target, a, 4), a);
        Check(Damage(1) == 0 && Damage(2) == 0 && Unknown() == 4, "unknown Strangle owner is not assigned to local player");
    }

    private static async Task DoomAccounting()
    {
        var target = Begin(7, lastEnemy: true); Doom(target, 6, 4);
        JournalService.State.CurrentSide = CombatSide.Enemy;
        Check(Total() == 0, "applying Doom stacks is not damage");
        CreatureCmd.SealOnVictory = true;
        await DoomPower.DoomKill(new[] { target });
        Check(Damage(1) == 4 && Damage(2) == 3 && Unknown() == 0, "Doom splits seven actual HP by six/four stack contribution");
        var fight = JournalService.Session.Run!.Combats.Single();
        var trace = fight.DamageTrace.Single();
        Check(fight.Outcome == CombatOutcome.Won && trace.Hp == 7 && trace.Overkill == 0 && trace.Blocked == 0,
            "Doom recorded before sealing, with no invented block or overkill");
        Check(trace.SourceId == "effect:doom" && trace.Source == "Trépas" && trace.Phase == JournalPhase.Enemy &&
            trace.Attribution == DamageAttributionKind.DoomConvention, "Doom timeline discloses stack attribution");
        JournalArchive.Validate(JournalService.Session.Run);
        await DoomPower.DoomKill(new[] { target });
        Check(Total() == 7, "repeated kill of a zero-HP creature cannot add damage");

        target = Begin(5); Doom(target, 4, unknown: 6);
        await DoomPower.DoomKill(new[] { target });
        Check(Damage(1) == 2 && Unknown() == 3, "unobserved Doom stacks retain unknown contribution");

        target = Begin(9, lastEnemy: true); var second = Enemy(JournalService.State, 5);
        Doom(target, 100); Doom(second, 0, 100); CreatureCmd.SealOnVictory = true;
        await DoomPower.DoomKill(new[] { target, second });
        Check(Damage(1) == 9 && Damage(2) == 5, "multi-target Doom credits actual HP independently before final seal");
        Check(JournalService.Session.Run!.Combats.Single().DamageTrace.Count == 2, "multi-target Doom has exactly one result per receiver");

        target = Begin(8); var power = Doom(target, 4, 4);
        JournalService.Reader.ForgetDoom(power); power.Amount = 8; Apply(power, Player(2), 8);
        await DoomPower.DoomKill(new[] { target });
        Check(Damage(1) == 0 && Damage(2) == 8, "removed/reapplied Doom does not retain previous contributors");

        target = Begin(8); Doom(target, 10);
        CreatureCmd.AfterKill = creature => { creature.CurrentHp = 3; return Task.CompletedTask; };
        await DoomPower.DoomKill(new[] { target });
        Check(Damage(1) == 8 && target.CurrentHp == 3, "native resurrection remains intact; only actual HP removal counted");
    }

    private static async Task Isolation()
    {
        var target = Begin(); var a = Player(1);
        CreatureCmd.AfterDamage = async creature =>
        {
            CreatureCmd.AfterDamage = null;
            await Task.Yield(); await CreatureCmd.Damage(Context, creature, 2, Props, null, null);
        };
        await Play(Strangle(target, a, 10), a);
        await CreatureCmd.Damage(Context, target, 4, Props, null, null);
        Check(Damage(1) == 10 && Unknown() == 6, "nested and subsequent unrelated damage cannot inherit Strangle ownership");

        target = Begin(8); Doom(target, 10);
        CreatureCmd.AfterKill = async creature =>
        {
            CreatureCmd.AfterKill = null; creature.CurrentHp = 5;
            await Task.Yield(); await CreatureCmd.Kill(creature);
        };
        await DoomPower.DoomKill(new[] { target });
        Check(Total() == 8, "nested same-target kill cannot inherit Doom provenance");

        target = Begin(8); Doom(target, 10);
        CreatureCmd.BeforeKill = async creature =>
        {
            CreatureCmd.BeforeKill = null;
            await Task.Yield(); await CreatureCmd.Damage(Context, creature, 3, Props, null, null);
        };
        await DoomPower.DoomKill(new[] { target });
        Check(Damage(1) == 5 && Unknown() == 3, "damage nested inside Doom cannot steal the remaining-HP execution credit");

        target = Begin(); a = Player(1); var b = Player(2); var second = Enemy(JournalService.State, 100);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CreatureCmd.BeforeDamage = creature => ReferenceEquals(creature, target) ? release.Task : Task.CompletedTask;
        var pending = Play(Strangle(target, a, 11), a);
        await Play(Strangle(second, b, 3), b);
        release.SetResult(); await pending;
        Check(Damage(1) == 11 && Damage(2) == 3, "interleaved asynchronous effects retain their own applier");

        target = Begin(8); Doom(target, 10);
        release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CreatureCmd.BeforeKill = _ => { entered.SetResult(); return release.Task; };
        pending = DoomPower.DoomKill(new[] { target }); await entered.Task;
        Begin(); release.SetResult(); await pending;
        Check(Total() == 0, "old suspended Doom callback cannot write into replacement combat");

        target = Begin(); a = Player(1);
        CreatureCmd.BeforeDamage = _ => throw new InvalidOperationException("native failure");
        bool failed = false;
        try { await Play(Strangle(target, a, 10), a); }
        catch (InvalidOperationException ex) { failed = ex.Message == "native failure"; }
        Check(failed && Total() == 0, "native exceptions preserved without phantom damage");

        target = Begin(); a = Player(1); Context.Models.Add(new object());
        var task = EffectDamageObserver.StrangleDamage(Context, target, 5, Props, null, null, Strangle(target, a, 5));
        Check(ReferenceEquals(task, CreatureCmd.LastDamageTask), "Strangle wrapper preserves exact native task");
        var result = (await task).Single();
        Check(ReferenceEquals(result, CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>().Single().Result) &&
            Context.Models.Count == 1, "native result identity and choice stack preserved");
        target = Begin(8); Doom(target, 10);
        var killTask = EffectDamageObserver.DoomKill(target, null, false);
        Check(ReferenceEquals(killTask, CreatureCmd.LastKillTask), "Doom wrapper preserves exact native task");
        await killTask;
    }
}
