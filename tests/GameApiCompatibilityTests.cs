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
using System.Text.Json;

internal static partial class GameApiCompatibilityTests
{

    private static int Main()
    {
        try
        {
            string root = FindRepositoryRoot();
            using var game = MetadataAssembly.Open(Path.Combine(root, "references", "sts2.dll"));
            Equal(new Guid("ad562b4e-7352-4ca2-ae45-0ff44cd284bc"), game.ModuleVersionId,
                "v0.113 module MVID");

            Equal(new[] { 0, 1 }, game.ParameterCounts(
                "MegaCrit.Sts2.Core.Combat.CombatManager", "EndCombatInternal"),
                "EndCombatInternal overloads");
            Check(game.Calls("MegaCrit.Sts2.Core.Combat.CombatManager", "CheckWinCondition",
                "MegaCrit.Sts2.Core.Combat.CombatManager", "EndCombatInternal", 1),
                "normal victory state machine calls one-argument EndCombatInternal");
            Check(!game.Calls("MegaCrit.Sts2.Core.Combat.CombatManager", "CheckWinCondition",
                "MegaCrit.Sts2.Core.Combat.CombatManager", "EndCombatInternal", 0),
                "normal victory bypasses parameterless test wrapper");
            game.RequireMethod("MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState", "GainEnergy",
                "System.Decimal");
            game.RequireMethod("MegaCrit.Sts2.Core.Hooks.Hook", "ModifyDamage",
                "MegaCrit.Sts2.Core.Runs.IRunState", "MegaCrit.Sts2.Core.Combat.ICombatState",
                "MegaCrit.Sts2.Core.Entities.Creatures.Creature", "MegaCrit.Sts2.Core.Entities.Creatures.Creature",
                "System.Decimal", "MegaCrit.Sts2.Core.ValueProps.ValueProp", "MegaCrit.Sts2.Core.Models.CardModel",
                "MegaCrit.Sts2.Core.Entities.Cards.CardPlay", "MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType",
                "MegaCrit.Sts2.Core.Entities.Cards.CardPreviewMode",
                "System.Collections.Generic.IEnumerable<MegaCrit.Sts2.Core.Models.AbstractModel>&");
            game.RequireMethod("MegaCrit.Sts2.Core.Models.AbstractModel", "ModifyDamageCap",
                "MegaCrit.Sts2.Core.Entities.Creatures.Creature", "MegaCrit.Sts2.Core.ValueProps.ValueProp",
                "MegaCrit.Sts2.Core.Entities.Creatures.Creature", "MegaCrit.Sts2.Core.Models.CardModel",
                "MegaCrit.Sts2.Core.Entities.Cards.CardPlay");
            game.RequireMethod("MegaCrit.Sts2.Core.Commands.CreatureCmd", "Damage",
                "MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext",
                "MegaCrit.Sts2.Core.Entities.Creatures.Creature", "System.Decimal",
                "MegaCrit.Sts2.Core.ValueProps.ValueProp", "MegaCrit.Sts2.Core.Models.CardModel",
                "MegaCrit.Sts2.Core.Entities.Cards.CardPlay");
            game.RequireMethod("MegaCrit.Sts2.Core.Commands.CreatureCmd", "Damage",
                "MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext",
                "System.Collections.Generic.IEnumerable<MegaCrit.Sts2.Core.Entities.Creatures.DamageTargetData>",
                "System.Decimal", "MegaCrit.Sts2.Core.ValueProps.ValueProp",
                "MegaCrit.Sts2.Core.Entities.Creatures.Creature", "MegaCrit.Sts2.Core.Models.CardModel",
                "MegaCrit.Sts2.Core.Entities.Cards.CardPlay", "MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType");

            Check(!game.HasType("MegaCrit.Sts2.Core.Models.Powers.DiamondDiademPower"),
                "legacy DiamondDiademPower removed");
            Equal(new[] { 3 }, game.ParameterCounts(
                "MegaCrit.Sts2.Core.Models.Relics.DiamondDiadem", "AfterSideTurnStart"),
                "DiamondDiadem v0.113 hook");
            Equal(new[] { 2 }, game.ParameterCounts(
                "MegaCrit.Sts2.Core.Models.Powers.HibernatePower", "AfterPlayerTurnStart"),
                "Hibernate v0.113 hook");
            Check(game.ParameterCounts("MegaCrit.Sts2.Core.Modding.AssemblyInfo", "ModForType").Contains(2),
                "AssemblyInfo.ModForType available");
            Check(game.HasField("MegaCrit.Sts2.Core.Modding.ModManifest", "minGameVersion"),
                "minimum game version manifest field");

            using var mod = MetadataAssembly.Open(Path.Combine(root, "bin", "Release", "net9.0",
                "BetterSpire2Lite.dll"));
            foreach (string resolver in new[] { "SingleDamage", "ManyDamage", "PoisonMoveNext" })
                Check(mod.ParameterCounts("BetterSpire2.Patches.Combat.PoisonNativeMethods", resolver).Contains(0),
                    "BetterSpire poison resolver " + resolver);
            VerifyManifest(root);
            VerifyNativeBoundaryCalls(mod);
            VerifyWinPatchTarget(game, mod);
            VerifyAdditionalContracts(game, mod, root);
            VerifyNativeObserver(game, mod);
            VerifyEffectDamage(game, mod);
            VerifyJudgementDamage(game, mod);
            VerifyCompletedDamage(game, mod);
            VerifyForecastReactions(mod);
            VerifyWuppyForecast(game, mod);
            VerifyContinuousHpDisplay(mod);
            VerifyRemainingPatchTargets(game);
            VerifyFoolArrowWindow(game);
            VerifyV113EndTurnEffects(game);
            VerifyModStructure(mod);

            Console.WriteLine($"PASS {Contract.Checks} v0.113 metadata compatibility checks.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL v0.113 compatibility: " + ex);
            return 1;
        }
    }

    private static void VerifyNativeBoundaryCalls(MetadataAssembly mod)
    {
        const string manager = "MegaCrit.Sts2.Core.Combat.CombatManager";
        const string adapter = "BetterSpire2.Guardian.Game.GameForecastAdapter";
        const string observer = "BetterSpire2.Guardian.Game.ForecastEventObserver";
        Check(mod.Calls(adapter, "BuildPlayerEndTurn", manager, "IsPartOfPlayerTurn", 1),
            "compiled adapter binds native participation predicate");
        Check(mod.Calls(adapter, "BuildPlayerEndTurn", "BetterSpire2.Guardian.Game.ForecastTurnOrder", "Participants", 2),
            "compiled adapter uses tested participant selection");
        Check(mod.Calls(adapter, "BuildEnemyTurn", "BetterSpire2.Guardian.Game.ForecastTurnOrder", "AttackHits", 2),
            "compiled adapter uses tested hit-major ordering");
        Check(mod.Calls("BetterSpire2.Trackers.DamageTracker", "Tick", manager, "IsExecutingCardOrPotionEffect", 1),
            "compiled tracker checks active card/potion effects");
        foreach (string signal in new[] { "PlayerEndedTurn", "PlayerUnendedTurn" })
        {
            Check(mod.Calls(observer, "Observe", manager, "add_" + signal, 1), "observer attaches " + signal);
            Check(mod.Calls(observer, "Dispose", manager, "remove_" + signal, 1), "observer detaches " + signal);
        }
        Check(mod.Calls(observer, "Observe", "MegaCrit.Sts2.Core.Combat.CombatStateTracker", "add_CombatStateChanged", 1),
            "native tracker subscription exists in compiled mod");
        Check(mod.Calls(observer, "Dispose", "MegaCrit.Sts2.Core.Combat.CombatStateTracker", "remove_CombatStateChanged", 1),
            "native tracker unsubscription exists in compiled mod");
        const string cards = "BetterSpire2.HandViewer.TeammateHandViewer+PlayerHandSection";
        Check(mod.Calls(cards, "ReadVisual", cards, "ReadCost", 1) &&
            mod.Calls(cards, "ReadCost", "MegaCrit.Sts2.Core.Entities.Cards.CardEnergyCost", "GetWithModifiers", 1),
            "card display preserves negative native costs");
        Check(!mod.Calls(cards, "ReadCost", "MegaCrit.Sts2.Core.Entities.Cards.CardEnergyCost", "GetResolved", 0),
            "card display does not use post-play resolved costs");
        Check(mod.Calls(cards, "ReadVisual", "MegaCrit.Sts2.Core.Models.CardModel", "get_Title", 0) &&
            !mod.Literals(cards, "ReadVisual").Contains("+"), "card display uses native upgrade title without appending plus");
    }

    private static void VerifyWinPatchTarget(MetadataAssembly game, MetadataAssembly mod)
    {
        // Never Assembly.Load sts2 here: its module initializer requires the game
        // runtime (including Sentry/Godot). Read IL only; exercise the source-linked
        // resolver against overload test doubles in GameBoundary.Core.Tests.
        const string turnState = "MegaCrit.Sts2.Core.Combat.CombatTurnState";
        game.RequireMethod("MegaCrit.Sts2.Core.Combat.CombatManager", "EndCombatInternal", turnState);
        const string patch = "BetterSpire2.Patches.Combat.CombatManager_EndCombatInternal_Patch";
        Check(mod.Literals(patch, "TargetMethod").Contains(turnState), "compiled resolver explicitly names runtime turn state");
        Check(mod.Literals(patch, "TargetMethod").Contains("EndCombatInternal"), "compiled resolver explicitly names victory method");
        Check(mod.Calls(patch, "TargetMethod", "HarmonyLib.AccessTools", "DeclaredMethod", 4), "compiled resolver uses exact declared-method lookup");
        foreach (string hook in new[] { "BeforeAttack", "AfterAttack", "ModifyAttackHitCount", "ShouldTakeExtraTurnEarly" })
            Check(mod.Literals("BetterSpire2.Guardian.Game.CapabilityAudit", ".cctor").Contains(hook),
                "compiled coverage guard includes " + hook);
    }

    private static void VerifyAdditionalContracts(MetadataAssembly game, MetadataAssembly mod, string root)
    {
        const string card = "MegaCrit.Sts2.Core.Models.CardModel";
        const string model = "MegaCrit.Sts2.Core.Models.AbstractModel";
        const string state = "MegaCrit.Sts2.Core.Combat.CombatState";
        const string manager = "MegaCrit.Sts2.Core.Combat.CombatManager";
        const string runManager = "MegaCrit.Sts2.Core.Runs.RunManager";
        const string variables = "MegaCrit.Sts2.Core.Localization.DynamicVars.DynamicVarSet";
        game.RequireContract("MegaCrit.Sts2.Core.Models.MonsterModel", "get_IsInteractable", "System.Boolean",
            MethodAttributes.Public, false);
        Check(mod.Calls("BetterSpire2.Guardian.UI.GuardianOverheadHud", "ShouldTrack",
            "MegaCrit.Sts2.Core.Models.MonsterModel", "get_IsInteractable", 0),
            "pet HP visibility binds the v113 native interactability rule");
        game.RequireContract(model, "MutableClone", model, MethodAttributes.Public, false);
        game.RequireContract(card, "UpdateDynamicVarPreview", "System.Void", MethodAttributes.Public, false,
            "MegaCrit.Sts2.Core.Entities.Cards.CardPreviewMode", "MegaCrit.Sts2.Core.Entities.Creatures.Creature", variables);
        game.RequireContract(card, "GetDescriptionForPile", "System.String", MethodAttributes.Public, false,
            "MegaCrit.Sts2.Core.Entities.Cards.PileType", "MegaCrit.Sts2.Core.Entities.Creatures.Creature");
        game.RequireContract("MegaCrit.Sts2.Core.Combat.History.CombatHistoryEntry", "get_RoundNumber",
            "System.Int32", MethodAttributes.Private, false);
        game.RequireContract("MegaCrit.Sts2.Core.Combat.History.CombatHistoryEntry", "get_CurrentSide",
            "MegaCrit.Sts2.Core.Combat.CombatSide", MethodAttributes.Private, false);
        game.RequireContract("MegaCrit.Sts2.Core.Models.Powers.PoisonPower", "get_TriggerCount",
            "System.Int32", MethodAttributes.Private, false);
        game.RequireContract("MegaCrit.Sts2.Core.Models.Relics.BeatingRemnant", "get_DamageReceivedThisTurn",
            "System.Decimal", MethodAttributes.Private, false);
        foreach (string prefix in new[] { "add_", "remove_" })
        {
            game.RequireContract(runManager, prefix + "RunStarted", "System.Void", MethodAttributes.Public, false,
                "System.Action<MegaCrit.Sts2.Core.Runs.RunState>");
            game.RequireContract("MegaCrit.Sts2.Core.Combat.CombatStateTracker", prefix + "CombatStateChanged",
                "System.Void", MethodAttributes.Public, false, "System.Action<" + state + ">");
        }
        foreach (string name in new[] { "Tick", "CanTrack", "OnCombatSetUp" })
            Check(!mod.Calls("BetterSpire2.Journal.Game.JournalService", name, manager, "DebugOnlyGetState", 0),
                "journal " + name + " never polls debug combat state");
        Check(mod.Calls("BetterSpire2.Runtime.RunLifecycle", "EnsureAttached", runManager, "add_RunStarted", 1),
            "native run observer attaches");
        Check(mod.Calls("BetterSpire2.Runtime.RunLifecycle", "Stop", runManager, "remove_RunStarted", 1),
            "native run observer detaches");
        const string preview = "BetterSpire2.HandViewer.DetachedCardPreview";
        Check(mod.Calls(preview, "Description", model, "MutableClone", 0), "native deep clone for isolated preview");
        Check(!mod.Calls(preview, "Description", card, "CreateClone", 0), "preview never registers with CardScope");
        Check(mod.Calls(preview, "Description", card, "UpdateDynamicVarPreview", 3), "native preview writes detached output");
        const string hand = "BetterSpire2.HandViewer.TeammateHandViewer+PlayerHandSection";
        Check(!mod.Calls(hand, "RefreshCards", card, "UpdateDynamicVarPreview", 3), "hand no longer writes live preview");
        Check(!mod.Calls(hand, "UpdateCard", "Godot.Node", "QueueFree", 0), "card changes preserve node subtree");
        Check(mod.ParameterCounts("BetterSpire2.Guardian.Core.ForecastHookPolicy", "IsReaction").Contains(1),
            "conservative lifecycle policy included");
        // Test the inspected native hazard as well as source-linked isolation behavior.
        Check(game.Calls(card, "DeepCloneFields", card, "EnchantInternal", 2), "native clone reapplies enchantment");
        Check(game.Calls(card, "EnchantInternal", "System.Action", "Invoke", 0), "native clone can invoke shallow event subscribers");
    }

    private static void VerifyNativeObserver(MetadataAssembly game, MetadataAssembly mod)
    {
        const string ns = "MegaCrit.Sts2.Core.";
        const string helper = ns + "Modding.ModHelper";
        const string observer = "BetterSpire2.Runtime.Native.BetterSpireCombatObserver";
        const string bridge = "BetterSpire2.Runtime.Native.NativeCombatHooks";
        const string model = ns + "Models.AbstractModel";
        const string state = ns + "Combat.CombatState";
        const string context = ns + "GameActions.Multiplayer.PlayerChoiceContext";
        const string creature = ns + "Entities.Creatures.Creature";
        const string result = ns + "Entities.Creatures.DamageResult";
        const string card = ns + "Models.CardModel";
        const string play = ns + "Entities.Cards.CardPlay";
        const string attack = ns + "Commands.Builders.AttackCommand";
        const string props = ns + "ValueProps.ValueProp";
        const string side = ns + "Combat.CombatSide";
        const string participants = "System.Collections.Generic.IReadOnlyList<" + creature + ">";
        game.RequireContract(helper, "SubscribeForCombatStateHooks", "System.Void", MethodAttributes.Public, true,
            "System.String", ns + "Modding.CombatHookSubscriptionDelegate");
        game.RequireContract(ns + "Modding.CombatHookSubscriptionDelegate", "Invoke",
            "System.Collections.Generic.IEnumerable<" + model + ">", MethodAttributes.Public, false, state);
        var hooks = new (string Name, string[] Parameters)[]
        {
            ("BeforeAttack", [attack]), ("AfterAttack", [context, attack]),
            ("BeforeCardPlayed", [play]), ("AfterCardPlayed", [context, play]),
            ("AfterDamageGiven", [context, creature, result, props, creature, card]),
            ("AfterDamageReceived", [context, creature, result, props, creature, card]),
            ("AfterPowerAmountChanged", [context, ns + "Models.PowerModel", "System.Decimal", creature, card]),
            ("AfterBlockGained", [creature, "System.Decimal", props, card]),
            ("AfterCurrentHpChanged", [creature, "System.Decimal"]),
            ("BeforeSideTurnStart", [context, side, participants, ns + "Combat.ICombatState"]),
            ("AfterSideTurnStart", [side, participants, ns + "Combat.ICombatState"]),
            ("AfterSideTurnEnd", [context, side, "System.Collections.Generic.IEnumerable<" + creature + ">"]),
            ("AfterPlayerTurnStart", [context, ns + "Entities.Players.Player"])
        };
        foreach (var (name, parameters) in hooks)
        {
            game.RequireContract(model, name, "System.Threading.Tasks.Task", MethodAttributes.Public, false, parameters);
            mod.RequireContract(observer, name, "System.Threading.Tasks.Task", MethodAttributes.Public, false, parameters);
            Check(mod.Calls(observer, name, observer, "Changed", 0), "native observer routes " + name + " to flag-only callback");
        }
        mod.RequireNotificationOnlyOverrides(observer, hooks.Select(h => h.Name));
        Check(mod.Calls(bridge, "Start", helper, "SubscribeForCombatStateHooks", 2), "one native combat provider");
        Check(!mod.Calls(bridge, "Start", helper, "SubscribeForRunStateHooks", 2), "no duplicate run provider");
        Check(mod.Calls(bridge, "Provide", ns + "Models.ModelDb", "GetById", 1), "observer uses canonical native model registry");
        Check(mod.Calls(bridge, "Provide", model, "MutableClone", 0), "observer cloned per live combat");
        Check(!mod.Calls(bridge, "Provide", ns + "Models.ModelDb", "Inject", 1), "no late ModelDb injection");
        Check(mod.Calls(observer, "Changed", bridge, "Notify", 1), "hook callback only flags observer");
        const string manager = ns + "Combat.CombatManager";
        const string hook = ns + "Hooks.Hook";
        Check(game.Calls(manager, "get_IsOverOrEnding", manager, "get_IsEnding", 0) &&
            game.Calls(manager, "get_IsEnding", manager, "IsCombatEnding", 1) &&
            game.Calls(manager, "IsCombatEnding", hook, "ShouldStopCombatFromEnding", 1) &&
            game.Calls(hook, "ShouldStopCombatFromEnding", ns + "Combat.ICombatState", "IterateHookListeners", 0) &&
            game.Calls(state, "IterateHookListeners", helper, "IterateAllCombatStateSubscribers", 1),
            "native last-kill ending predicate re-enters mod combat providers");
        foreach (string method in new[] { "Provide", "IsLive", "IsActive", "Notify" })
            foreach (string predicate in new[] { "get_IsOverOrEnding", "get_IsEnding", "IsCombatEnding" })
                Check(!mod.Calls(bridge, method, manager, predicate, predicate == "IsCombatEnding" ? 1 : 0),
                    "hook-stack " + method + " never queries recursive " + predicate);
        Check(mod.Calls(bridge, "FlushPending", manager, "get_IsOverOrEnding", 0),
            "ending notifications filtered outside native hook dispatch");
        foreach (string method in new[] { "Start", "Provide", "Notify", "FlushPending" })
            Check(!mod.Calls(bridge, method, ns + "Combat.CombatManager", "DebugOnlyGetState", 0), "no debug reads in " + method);
        foreach (string caller in new[] { "Observe", "Dispose" })
            Check(mod.Calls("BetterSpire2.Guardian.Game.ForecastEventObserver", caller, bridge,
                caller == "Observe" ? "add_StateChanged" : "remove_StateChanged", 1), "forecast hooks " + caller);
        Check(mod.Calls("BetterSpire2.Journal.Game.JournalService", "OnCombatSetUp", bridge, "add_StateChanged", 1), "journal attaches native observer");
        Check(mod.Calls("BetterSpire2.Journal.Game.JournalService", "Detach", bridge, "remove_StateChanged", 1), "journal detaches native observer");
        Check(!mod.Calls("BetterSpire2.Journal.Game.JournalService", "OnNativeStateChanged",
            "BetterSpire2.Journal.Core.JournalSession", "Append", 1), "hooks never append duplicate statistics");
        Check(mod.Calls("BetterSpire2.Runtime.ModRuntime", "Tick", bridge, "FlushPending", 0), "heartbeat flushes native notifications");
        Check(mod.Calls("BetterSpire2.Runtime.CombatLifecycle", "ForgetCombat", bridge, "ForgetCombat", 0), "combat reset releases observer");
    }

    private static void VerifyManifest(string root)
    {
        string path = Path.Combine(root, "bin", "Release", "net9.0", "BetterSpire2Lite.json");
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
        var manifest = json.RootElement;
        Equal("3.6.5-v113", manifest.GetProperty("version").GetString(), "manifest version");
        Equal("0.113.0", manifest.GetProperty("min_game_version").GetString(), "minimum game version");
        Equal(false, manifest.GetProperty("affects_gameplay").GetBoolean(), "non-gameplay manifest flag");
    }

    private static void VerifyEffectDamage(MetadataAssembly game, MetadataAssembly mod)
    {
        const string ns = "MegaCrit.Sts2.Core.";
        const string command = ns + "Commands.CreatureCmd";
        const string creature = ns + "Entities.Creatures.Creature";
        const string result = ns + "Entities.Creatures.DamageResult";
        const string strangle = ns + "Models.Powers.StranglePower";
        const string doom = ns + "Models.Powers.DoomPower";
        const string observer = "BetterSpire2.Journal.Game.EffectDamageObserver";
        const string service = "BetterSpire2.Journal.Game.JournalService";
        const string reader = "BetterSpire2.Journal.Game.HistoryReader";
        game.RequireContract(strangle, "AfterCardPlayed", "System.Threading.Tasks.Task", MethodAttributes.Public, false,
            ns + "GameActions.Multiplayer.PlayerChoiceContext", ns + "Entities.Cards.CardPlay");
        game.RequireContract(doom, "DoomKill", "System.Threading.Tasks.Task", MethodAttributes.Public, true,
            "System.Collections.Generic.IReadOnlyList<" + creature + ">");
        game.RequireContract(command, "KillWithoutCheckingWinCondition", "System.Threading.Tasks.Task", MethodAttributes.Private, true,
            creature, ns + "Models.CardModel", "System.Boolean", "System.Int32");
        game.RequireContract(creature, "LoseHpInternal", result, MethodAttributes.Public, false, "System.Decimal", ns + "ValueProps.ValueProp");
        Equal(1, game.CallCount(strangle, "AfterCardPlayed", command, "Damage", 6), "one native Strangle damage origin");
        Equal(1, game.CallCount(doom, "DoomKill", command, "Kill", 3), "one direct native Doom kill origin");
        Equal(3, game.CallCount(command, "Damage", creature, "LoseHpInternal", 2), "three native damage receiver HP-loss sites");
        Equal(1, game.CallCount(command, "KillWithoutCheckingWinCondition", creature, "LoseHpInternal", 2), "one direct native kill HP-loss site");
        Check(!game.Calls(command, "KillWithoutCheckingWinCondition", ns + "Combat.History.CombatHistory", "DamageReceived", 5),
            "native direct kills omit damage history");
        Check(mod.Calls(observer, "StrangleDamage", command, "Damage", 6) && mod.Calls(observer, "DoomKill", command, "Kill", 3),
            "effect wrappers retain original native commands");
        Equal(1, mod.CallCount(observer, "LoseHp", creature, "LoseHpInternal", 2), "observed native HP loss executed exactly once");
        Check(mod.Calls(observer, "LoseHp", service, "RecordEffectDamage", 4), "effect result routed before native death hooks");
        Check(mod.Calls(service, "RecordEffectDamage", reader, "RecordDamage", 8) &&
            mod.Calls(reader, "Map", reader, "RecordDamage", 8), "effects and native history share one result writer");
        Check(!mod.Calls(service, "RecordEffectDamage", ns + "Combat.History.CombatHistory", "DamageReceived", 5),
            "effect observer never injects gameplay history");
    }

    private static void VerifyJudgementDamage(MetadataAssembly game, MetadataAssembly mod)
    {
        const string ns = "MegaCrit.Sts2.Core.";
        const string command = ns + "Commands.CreatureCmd";
        const string creature = ns + "Entities.Creatures.Creature";
        const string judgement = ns + "Models.Cards.Judgement";
        const string observer = "BetterSpire2.Journal.Game.JudgementDamageObserver";
        game.RequireContract(judgement, "OnPlay", "System.Threading.Tasks.Task", MethodAttributes.Family, false,
            ns + "GameActions.Multiplayer.PlayerChoiceContext", ns + "Entities.Cards.CardPlay");
        game.RequireContract(command, "SetCurrentHp", "System.Threading.Tasks.Task", MethodAttributes.Public, true,
            creature, "System.Decimal");
        game.RequireContract(creature, "SetCurrentHpInternal", "System.Void", MethodAttributes.Public, false, "System.Decimal");
        game.RequireContract(ns + "Entities.Creatures.DamageResult", ".ctor", "System.Void", MethodAttributes.Public, false,
            creature, ns + "ValueProps.ValueProp");
        game.RequireContract(ns + "Entities.Creatures.DamageResult", "set_UnblockedDamage", "System.Void", MethodAttributes.Public,
            false, "System.Int32");
        game.RequireContract(ns + "Entities.Creatures.DamageResult", "set_WasTargetKilled", "System.Void", MethodAttributes.Public,
            false, "System.Boolean");
        Equal(1, game.CallCount(judgement, "OnPlay", command, "SetCurrentHp", 2), "one native Judgement HP-setting origin");
        Equal(1, game.CallCount(command, "SetCurrentHp", creature, "SetCurrentHpInternal", 1), "one synchronous native HP setter");
        Check(!game.Calls(command, "SetCurrentHp", ns + "Combat.History.CombatHistory", "DamageReceived", 5),
            "native HP setting omits damage history");
        Equal(2, mod.CallCount(observer, "SetCurrentHp", command, "SetCurrentHp", 2), "Judgement fallback and observed branches retain native command calls");
        Equal(1, mod.CallCount(observer, "SetHp", creature, "SetCurrentHpInternal", 1), "Judgement observer preserves one native setter");
        Check(mod.Calls(observer, "SetHp", "BetterSpire2.Journal.Game.JournalService", "RecordNativeDamage", 5),
            "Judgement accounting uses the central combat identity fence");
        Check(!mod.Calls(observer, "SetHp", ns + "Combat.History.CombatHistory", "DamageReceived", 5),
            "Judgement accounting never fabricates native gameplay history");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BetterSpire2Lite.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("BetterSpire repository root");
    }

    private static void VerifyCompletedDamage(MetadataAssembly game, MetadataAssembly mod)
    {
        const string command = "MegaCrit.Sts2.Core.Commands.CreatureCmd";
        const string result = "MegaCrit.Sts2.Core.Entities.Creatures.DamageResult";
        const string observer = "BetterSpire2.Journal.Game.NativeDamageObserver";
        const string service = "BetterSpire2.Journal.Game.JournalService";
        game.RequireCompletedDamagePattern(command, result);
        Check(mod.Calls(observer, "ReadCompletedHp", service, "RecordNativeDamage", 5),
            "completed results route to central journal accounting");
        Check(mod.Calls(service, "RecordNativeDamage", "BetterSpire2.Journal.Game.HistoryReader", "RecordDamage", 8),
            "supplemental and history results share identity deduplication");
        Check(!mod.Calls(observer, "ReadCompletedHp", "MegaCrit.Sts2.Core.Combat.History.CombatHistory", "DamageReceived", 5),
            "supplemental results never fabricate native history");
    }

    private static void VerifyForecastReactions(MetadataAssembly mod)
    {
        const string adapter = "BetterSpire2.Guardian.Game.GameForecastAdapter";
        Check(mod.Literals(adapter, ".cctor").Contains("ad562b4e-7352-4ca2-ae45-0ff44cd284bc"),
            "runtime forecast whitelist uses the audited v113 game module");
        Check(mod.Calls(adapter, "CaptureActor", adapter, "CaptureDamageReaction", 2),
            "forecast snapshots include native retaliation powers");
        Check(mod.Calls(adapter, "BuildEnemyTurn", adapter, "DamageCommand", 7),
            "enemy attacks preserve one native command per AOE hit");
        Check(mod.Calls(adapter, "BuildPlayerEndTurn", adapter, "RandomTargetDamage", 5),
            "player end-turn builder includes passive lightning and Obsidian Vice branches");
        Check(mod.Calls(adapter, "BuildPlayerEndTurn", adapter, "AddDoomCheck", 3),
            "player end-turn builder includes native Doom checks");
        Check(mod.Calls(adapter, "BuildEnemyTurn", adapter, "AddDoomCheck", 3),
            "enemy turn builder includes native Doom checks");
        Check(mod.Calls(adapter, "BuildPlayerEndTurn", "MegaCrit.Sts2.Core.Hooks.Hook", "ModifyOrbPassiveTriggerCount", 4),
            "passive orb trigger count uses the native modifier hook");
        mod.RequireReadOnlyForecast(adapter);
        mod.RequireReadOnlyForecast("BetterSpire2.Guardian.Core.ForecastSimulator");
    }

    private static void VerifyWuppyForecast(MetadataAssembly game, MetadataAssembly mod)
    {
        const string ns = "MegaCrit.Sts2.Core.";
        const string creature = ns + "Entities.Creatures.Creature";
        const string loyalty = ns + "Models.Powers.WuppysLoyaltyPower";
        const string relic = ns + "Models.Relics.Wuppy";
        const string adapter = "BetterSpire2.Guardian.Game.GameForecastAdapter";
        const string snapshot = "BetterSpire2.Guardian.Core.ActorSnapshot";
        game.RequireContract(loyalty, "ModifyPreBlockDamageTarget", creature, MethodAttributes.Public, false,
            creature, creature, "System.Decimal", ns + "ValueProps.ValueProp", creature);
        Check(game.Calls(loyalty, "ModifyPreBlockDamageTarget", ns + "ValueProps.ValuePropExtensions", "IsPoweredAttack", 1),
            "native Wuppy redirection is limited to powered attacks");
        Check(game.Calls(loyalty, "ModifyPreBlockDamageTarget", creature, "get_IsDead", 0) &&
            game.Calls(loyalty, "ModifyPreBlockDamageTarget", creature, "get_PetOwner", 0),
            "native Wuppy redirection checks pet life and owner identity");
        Check(game.Calls(ns + "Commands.CreatureCmd", "Damage", ns + "Hooks.Hook", "ModifyPreBlockDamageTarget", 5),
            "native damage still dispatches the pre-block redirect phase");
        game.RequireContract(relic, "AfterCurrentHpChanged", "System.Threading.Tasks.Task", MethodAttributes.Public, false,
            creature, "System.Decimal");
        Check(game.Calls(relic, "AfterCurrentHpChanged", relic, "set_CurrentHp", 1), "Wuppy HP notification maintains its relic counter");
        Check(mod.UsesTypeArgument(adapter, "CaptureActor", loyalty), "live snapshots capture actual Wuppy loyalty powers");
        Check(mod.Calls("BetterSpire2.Guardian.Core.ForecastSimulator", "ResolveDamage", snapshot,
            "get_RedirectPoweredAttacksBeforeBlockTo", 0), "simulator consumes the captured pre-block redirect");
        var supported = mod.Literals("BetterSpire2.Guardian.Game.CapabilityAudit", ".cctor").ToArray();
        Check(supported.Contains("WuppysLoyaltyPower") && supported.Contains("ModifyPreBlockDamageTarget") &&
            supported.Contains("Wuppy") && supported.Contains("AfterCurrentHpChanged"),
            "compiled coverage guard recognizes native Wuppy redirection and bookkeeping");
    }

    private static void VerifyModStructure(MetadataAssembly mod)
    {
        // ModEntry patches classes one by one (per-class failure isolation); an unregistered class is silently inert.
        var patches = mod.TypesWithAttribute("HarmonyLib.HarmonyPatch");
        var registered = mod.TypeofTargets("BetterSpire2.Core.ModEntry", ".cctor");
        Check(patches.Length > 30, "Harmony patch classes discovered in the compiled mod");
        foreach (string patch in patches)
            Check(registered.Contains(patch), "ModEntry registers Harmony patch " + patch);
        // The journal is the source of truth; views such as the damage meter read it, never the reverse.
        var reverse = mod.CallsAcross("BetterSpire2.Journal", "BetterSpire2.DamageMeter");
        Check(reverse.Length == 0, "journal does not drive its views: " + string.Join("; ", reverse));
        Check(mod.Calls("BetterSpire2.Runtime.ModRuntime", "Tick", "BetterSpire2.DamageMeter.DamageMeterController", "Tick", 0),
            "runtime ticks the damage meter itself");
        // Combat boundaries reach features only through CombatLifecycle, which isolates each step.
        const string lifecycle = "BetterSpire2.Runtime.CombatLifecycle";
        foreach (var (patch, phase) in new[] { ("CombatManager_LoseCombat_Patch", "Ending"),
            ("CombatManager_EndCombatInternal_Patch", "Ending"), ("CombatManager_Reset_Patch", "AfterReset") })
        {
            string type = "BetterSpire2.Patches.Combat." + patch;
            Check(mod.CallsAcross(type, "BetterSpire2").All(call => call.EndsWith(lifecycle + "." + phase, StringComparison.Ordinal) ||
                    call.EndsWith(lifecycle + ".BeforeReset", StringComparison.Ordinal)),
                patch + " delegates to a CombatLifecycle phase only: " + string.Join("; ", mod.CallsAcross(type, "BetterSpire2")));
            Check(mod.CallsAcross(type, "BetterSpire2").Length > 0, patch + " calls its lifecycle phase");
        }
    }
    private static void VerifyV113EndTurnEffects(MetadataAssembly game)
    {
        // PlayerEndTurnBuilder mirrors exactly one native damage command for each.
        const string ns = "MegaCrit.Sts2.Core.";
        const string command = ns + "Commands.CreatureCmd";
        Equal(1, game.CallCount(ns + "Models.Relics.ObsidianVice", "BeforeSideTurnEnd", command, "Damage", 7),
            "one Obsidian Vice end-turn random-target hit");
        Check(game.Calls(ns + "Models.Relics.ObsidianVice", "BeforeSideTurnEnd", ns + "Entities.Cards.CardPile", "get_Cards", 0),
            "Obsidian Vice scales with the current hand");
        Equal(1, game.CallCount(ns + "Models.Powers.InsatiableDominionPower", "AfterSideTurnEndLate", command, "Damage", 6),
            "one Insatiable Dominion DamageVar self-hit");
    }
    private static void VerifyFoolArrowWindow(MetadataAssembly game)
    {
        // CapabilityAudit whitelists arrow shot/impale listeners because only card plays reach them.
        const string ns = "MegaCrit.Sts2.Core.";
        const string arrows = ns + "Commands.ArrowCmd";
        const string impaled = ns + "Models.Powers.ImpaledPower";
        bool FromCard(string type) => type.StartsWith(ns + "Models.Cards.", StringComparison.Ordinal);
        foreach (string hook in new[] { "AfterArrowShot", "AfterArrowImpaled", "AfterArrowDislodged" })
            Check(game.CallerTypes(ns + "Hooks.Hook", hook).SetEquals(new[] { arrows }), hook + " dispatched only by ArrowCmd");
        foreach (string command in new[] { "ShootArrowAtTarget", "TryShootArrow", "ImpaleTarget" })
        {
            var callers = game.CallerTypes(arrows, command);
            Check(callers.Count > 0 && callers.All(t => t == arrows || FromCard(t)), "arrows " + command + " only from card plays");
        }
        var dislodgers = game.CallerTypes(arrows, "DislodgeArrow");
        dislodgers.UnionWith(game.CallerTypes(impaled, "DislodgeAllArrows"));
        Check(dislodgers.Count > 0 && dislodgers.All(t => t == arrows || t == impaled || FromCard(t) ||
            t == ns + "Commands.CreatureCmd"), "arrow dislodges only from cards, Impaled hits/death, or escape");
    }
    private static void VerifyContinuousHpDisplay(MetadataAssembly mod)
    {
        const string hud = "BetterSpire2.Guardian.UI.GuardianOverheadHud";
        const string label = "BetterSpire2.Guardian.UI.OverheadLabel";
        const string state = "BetterSpire2.Guardian.Core.OverheadDisplayState";
        mod.RequireContract(hud, "RenderStatus", "System.Boolean", MethodAttributes.Assembly, false, "System.Boolean");
        Check(!mod.Calls(hud, "RenderStatus", hud, "Hide", 0),
            "resolving and unavailable statuses do not hide the HP HUD");
        const string policy = "BetterSpire2.Guardian.Core.GuardianTurnVisibility";
        Check(mod.Calls(hud, "UpdatePositions", policy, "ShouldShow", 6) &&
            mod.Calls("BetterSpire2.Trackers.DamageTracker", "Tick", policy, "ShouldShow", 6),
            "frame display and forecast tracker share the native player-turn visibility policy");
        Check(mod.Calls("BetterSpire2.Trackers.DamageTracker", "Tick", "BetterSpire2.Guardian.UI.GuardianHud", "EndPlayerTurn", 0),
            "ending the local turn clears the previous forecast display");
        Check(mod.Calls(hud, "UpdatePositions", state, "Read", 0) &&
            !mod.Calls(hud, "UpdatePositions", "MegaCrit.Sts2.Core.Entities.Creatures.Creature", "get_CurrentHp", 0),
            "frame display retains the projected numbers instead of showing live HP");
        Check(mod.Calls("BetterSpire2.Guardian.UI.GuardianHud", "RenderStatus", hud, "RenderStatus", 1),
            "forecast errors reach the live readout status");
        Check(mod.Calls(label, "Bind", state, "Bind", 3),
            "successful forecast binding clears the pending or failed display state");
        Check(mod.Calls(hud, "Clear", label, "Free", 0) && mod.Calls(hud, "Clear", hud, "Hide", 0),
            "combat cleanup removes old readouts and frame subscriptions");
    }

    private static void Check(bool condition, string label) => Contract.Check(condition, label);
    private static void Equal<T>(T expected, T actual, string label) => Contract.Equal(expected, actual, label);
}
