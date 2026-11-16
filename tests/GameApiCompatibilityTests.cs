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
    private static int _checks;

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

            Console.WriteLine($"PASS {_checks} v0.113 metadata compatibility checks.");
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

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        _checks++;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        bool equal = expected is Array expectedArray && actual is Array actualArray
            ? expectedArray.Cast<object>().SequenceEqual(actualArray.Cast<object>())
            : EqualityComparer<T>.Default.Equals(expected, actual);
        if (!equal) throw new InvalidOperationException($"{label}: expected {Format(expected)}, got {Format(actual)}");
        _checks++;
    }

    private static string Format<T>(T value) => value is Array array
        ? "[" + string.Join(", ", array.Cast<object>()) + "]"
        : value?.ToString() ?? "null";

    private sealed class MetadataAssembly : IDisposable
    {
        private readonly FileStream _stream;
        private readonly PEReader _pe;
        private readonly MetadataReader _metadata;
        private readonly SignatureNames _names;

        private MetadataAssembly(string path)
        {
            _stream = File.OpenRead(path);
            _pe = new PEReader(_stream);
            _metadata = _pe.GetMetadataReader();
            _names = new SignatureNames();
        }

        internal static MetadataAssembly Open(string path) => new(path);
        internal Guid ModuleVersionId => _metadata.GetGuid(_metadata.GetModuleDefinition().Mvid);

        internal bool HasType(string fullName) => FindType(fullName).HasValue;

        internal bool HasField(string fullName, string fieldName)
        {
            var handle = FindType(fullName);
            return handle.HasValue && _metadata.GetTypeDefinition(handle.Value).GetFields()
                .Any(field => _metadata.GetString(_metadata.GetFieldDefinition(field).Name) == fieldName);
        }

        internal int[] ParameterCounts(string fullName, string methodName)
        {
            var handle = FindType(fullName) ?? throw new TypeLoadException(fullName);
            return _metadata.GetTypeDefinition(handle).GetMethods()
                .Select(method => _metadata.GetMethodDefinition(method))
                .Where(method => _metadata.GetString(method.Name) == methodName)
                .Select(method => method.DecodeSignature(_names, genericContext: null).ParameterTypes.Length)
                .Order().ToArray();
        }

        internal void RequireMethod(string fullName, string methodName, params string[] parameters)
        {
            var handle = FindType(fullName) ?? throw new TypeLoadException(fullName);
            bool found = _metadata.GetTypeDefinition(handle).GetMethods()
                .Select(method => _metadata.GetMethodDefinition(method))
                .Where(method => _metadata.GetString(method.Name) == methodName)
                .Select(method => method.DecodeSignature(_names, genericContext: null).ParameterTypes)
                .Any(observed => observed.SequenceEqual(parameters));
            Check(found, fullName + "." + methodName + "(" + string.Join(", ", parameters) + ")");
        }

        internal void RequireContract(string owner, string name, string returnType,
            MethodAttributes access, bool isStatic, params string[] parameters)
        {
            var type = _metadata.GetTypeDefinition(FindType(owner) ?? throw new TypeLoadException(owner));
            bool found = type.GetMethods().Select(h => _metadata.GetMethodDefinition(h)).Any(method =>
            {
                if (_metadata.GetString(method.Name) != name) return false;
                var signature = method.DecodeSignature(_names, null);
                return signature.ReturnType == returnType && signature.ParameterTypes.SequenceEqual(parameters) &&
                    (method.Attributes & MethodAttributes.MemberAccessMask) == access &&
                    ((method.Attributes & MethodAttributes.Static) != 0) == isStatic;
            });
            Check(found, "visibility/static/return/parameters: " + owner + "." + name);
        }

        internal void RequireNotificationOnlyOverrides(string owner, IEnumerable<string> hooks)
        {
            var expected = hooks.Concat(new[] { "get_ShouldReceiveCombatHooks", "AfterCloned" }).ToHashSet(StringComparer.Ordinal);
            var definition = _metadata.GetTypeDefinition(FindType(owner) ?? throw new TypeLoadException(owner));
            Check((definition.Attributes & TypeAttributes.Sealed) != 0, "observer is sealed; no subclass allowlist bypass");
            foreach (var handle in definition.GetMethods())
            {
                var method = _metadata.GetMethodDefinition(handle);
                if ((method.Attributes & MethodAttributes.Virtual) == 0) continue;
                Check(expected.Remove(_metadata.GetString(method.Name)), "only audited notification/lifetime overrides");
            }
            Check(expected.Count == 0, "all audited observer overrides present");
            // SavedProperty attributes would put observer state into serialization.
            foreach (var property in definition.GetProperties())
                foreach (var attr in _metadata.GetPropertyDefinition(property).GetCustomAttributes())
                {
                    var ctor = _metadata.GetCustomAttribute(attr).Constructor;
                    if (ctor.Kind != HandleKind.MemberReference) continue;
                    var parent = _metadata.GetMemberReference((MemberReferenceHandle)ctor).Parent;
                    if (parent.Kind != HandleKind.TypeReference) continue;
                    string attribute = _names.GetTypeFromReference(_metadata, (TypeReferenceHandle)parent, 0);
                    Check(!attribute.EndsWith("SavedPropertyAttribute", StringComparison.Ordinal), "observer has no saved properties");
                }
        }

        private TypeDefinitionHandle? FindType(string fullName)
        {
            foreach (var handle in _metadata.TypeDefinitions)
            {
                string observed = DefinitionName(handle);
                if (observed == fullName) return handle;
            }
            return null;
        }

        private string DefinitionName(TypeDefinitionHandle handle)
        {
            var type = _metadata.GetTypeDefinition(handle);
            string name = _metadata.GetString(type.Name);
            if (!type.GetDeclaringType().IsNil) return DefinitionName(type.GetDeclaringType()) + "+" + name;
            string ns = _metadata.GetString(type.Namespace);
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }

        internal HashSet<string> CallerTypes(string calleeOwner, string calleeName)
        {
            // Top-level declaring types, so async state machines report their owning model.
            var callers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var typeHandle in _metadata.TypeDefinitions)
                foreach (var methodHandle in _metadata.GetTypeDefinition(typeHandle).GetMethods())
                {
                    var method = _metadata.GetMethodDefinition(methodHandle);
                    if (method.RelativeVirtualAddress == 0) continue;
                    foreach (var (op, token) in ReadInstructions(method))
                    {
                        if (op.OperandType != OperandType.InlineMethod) continue;
                        var called = MetadataTokens.EntityHandle(token);
                        if (called.Kind == HandleKind.MethodSpecification)
                            called = _metadata.GetMethodSpecification((MethodSpecificationHandle)called).Method;
                        if (called.Kind != HandleKind.MethodDefinition) continue;
                        var target = _metadata.GetMethodDefinition((MethodDefinitionHandle)called);
                        if (_metadata.GetString(target.Name) == calleeName && DefinitionName(target.GetDeclaringType()) == calleeOwner)
                            callers.Add(DefinitionName(typeHandle).Split('+')[0]);
                    }
                }
            return callers;
        }
        internal bool Calls(string owner, string methodName, string calleeOwner, string calleeName, int parameterCount) =>
            CallCount(owner, methodName, calleeOwner, calleeName, parameterCount) > 0;

        internal void RequireReadOnlyForecast(string owner)
        {
            foreach (var handle in _metadata.TypeDefinitions)
            {
                string typeName = DefinitionName(handle);
                if (typeName != owner && !typeName.StartsWith(owner + "+", StringComparison.Ordinal)) continue;
                foreach (var methodHandle in _metadata.GetTypeDefinition(handle).GetMethods())
                {
                    var method = _metadata.GetMethodDefinition(methodHandle);
                    if (method.RelativeVirtualAddress == 0) continue;
                    foreach (var instruction in ReadInstructions(method))
                    {
                        if (instruction.Op.OperandType != OperandType.InlineMethod) continue;
                        var target = MetadataTokens.EntityHandle(instruction.Token);
                        if (target.Kind == HandleKind.MethodSpecification)
                            target = _metadata.GetMethodSpecification((MethodSpecificationHandle)target).Method;
                        if (target.Kind != HandleKind.MemberReference) continue;
                        var member = _metadata.GetMemberReference((MemberReferenceHandle)target);
                        if (member.Parent.Kind != HandleKind.TypeReference) continue;
                        string declaring = _names.GetTypeFromReference(_metadata, (TypeReferenceHandle)member.Parent, 0);
                        string name = _metadata.GetString(member.Name);
                        bool native = declaring.StartsWith("MegaCrit.Sts2.", StringComparison.Ordinal);
                        Check(!(native && (name == "get_Rng" || declaring.Contains("Rng", StringComparison.Ordinal) ||
                            declaring.StartsWith("MegaCrit.Sts2.Core.Commands.", StringComparison.Ordinal) ||
                            name.StartsWith("set_", StringComparison.Ordinal))),
                            "forecast does not call native RNG, commands or setters: " + declaring + "." + name);
                    }
                }
            }
        }

        internal void RequireCompletedDamagePattern(string owner, string result)
        {
            var type = _metadata.GetTypeDefinition(FindType(owner) ?? throw new TypeLoadException(owner));
            int matches = 0;
            foreach (var nested in type.GetNestedTypes())
            {
                var definition = _metadata.GetTypeDefinition(nested);
                if (!_metadata.GetString(definition.Name).StartsWith("<Damage>d__", StringComparison.Ordinal)) continue;
                string nestedName = DefinitionName(nested);
                if (!Calls(nestedName, "MoveNext", "MegaCrit.Sts2.Core.Combat.History.CombatHistory", "DamageReceived", 5)) continue;
                foreach (var handle in definition.GetMethods())
                {
                    var method = _metadata.GetMethodDefinition(handle);
                    if (_metadata.GetString(method.Name) != "MoveNext") continue;
                    var instructions = ReadInstructions(method);
                    bool IsGetter(int token, string name)
                    {
                        if (token == 0) return false;
                        var h = MetadataTokens.EntityHandle(token);
                        if (h.Kind != HandleKind.MethodDefinition) return false;
                        var called = _metadata.GetMethodDefinition((MethodDefinitionHandle)h);
                        return DefinitionName(called.GetDeclaringType()) == result && _metadata.GetString(called.Name) == name;
                    }
                    int index = instructions.FindIndex(i => i.Op.OperandType == OperandType.InlineMethod && IsGetter(i.Token, "get_UnblockedDamage"));
                    Check(index >= 0 && index + 4 < instructions.Count &&
                        instructions[index + 1].Op.Name!.StartsWith("ldloc", StringComparison.Ordinal) &&
                        instructions[index + 2].Op == OpCodes.Ldfld &&
                        IsGetter(instructions[index + 3].Token, "get_OverkillDamage") &&
                        instructions[index + 4].Op == OpCodes.Add,
                        "v113 completed-result HP + overkill observation pattern");
                    matches++;
                }
            }
            Equal(1, matches, "exactly one central damage result observation loop");
        }

        private List<(OpCode Op, int Token)> ReadInstructions(MethodDefinition method)
        {
            var instructions = new List<(OpCode, int)>();
            var reader = _pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
            while (reader.RemainingBytes > 0)
            {
                byte first = reader.ReadByte();
                var op = Opcodes[first == 0xfe ? unchecked((short)(0xfe00 | reader.ReadByte())) : first];
                if (op.OperandType == OperandType.InlineMethod)
                { instructions.Add((op, reader.ReadInt32())); continue; }
                instructions.Add((op, 0));
                int size = op.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => checked(reader.ReadInt32() * 4),
                    _ => 4
                };
                reader.Offset += size;
            }
            return instructions;
        }

        internal int CallCount(string owner, string methodName, string calleeOwner, string calleeName, int parameterCount)
        {
            int matches = 0;
            foreach (int token in Operands(owner, methodName, OperandType.InlineMethod))
            {
                EntityHandle called = MetadataTokens.EntityHandle(token);
                if (called.Kind == HandleKind.MethodSpecification)
                    called = _metadata.GetMethodSpecification((MethodSpecificationHandle)called).Method;
                string declaring, name; int count;
                if (called.Kind == HandleKind.MethodDefinition)
                {
                    var target = _metadata.GetMethodDefinition((MethodDefinitionHandle)called);
                    declaring = DefinitionName(target.GetDeclaringType());
                    name = _metadata.GetString(target.Name);
                    count = target.DecodeSignature(_names, null).ParameterTypes.Length;
                }
                else if (called.Kind == HandleKind.MemberReference)
                {
                    var target = _metadata.GetMemberReference((MemberReferenceHandle)called);
                    declaring = target.Parent.Kind switch
                    {
                        HandleKind.TypeReference => _names.GetTypeFromReference(_metadata, (TypeReferenceHandle)target.Parent, 0),
                        HandleKind.TypeDefinition => DefinitionName((TypeDefinitionHandle)target.Parent),
                        _ => ""
                    };
                    name = _metadata.GetString(target.Name);
                    count = target.DecodeMethodSignature(_names, null).ParameterTypes.Length;
                }
                else continue;
                if (declaring == calleeOwner && name == calleeName && count == parameterCount) matches++;
            }
            return matches;
        }

        internal IEnumerable<string> Literals(string owner, string methodName) =>
            Operands(owner, methodName, OperandType.InlineString)
                .Select(token => _metadata.GetUserString(MetadataTokens.UserStringHandle(token & 0x00ffffff)));

        internal bool UsesTypeArgument(string owner, string methodName, string type) =>
            Operands(owner, methodName, OperandType.InlineMethod).Select(MetadataTokens.EntityHandle)
                .Where(handle => handle.Kind == HandleKind.MethodSpecification)
                .Any(handle => _metadata.GetMethodSpecification((MethodSpecificationHandle)handle)
                    .DecodeSignature(_names, null).Contains(type));

        private List<int> Operands(string owner, string methodName, OperandType operandType)
        {
            var operands = new List<int>();
            var type = _metadata.GetTypeDefinition(FindType(owner) ?? throw new TypeLoadException(owner));
            var methods = type.GetMethods().Where(h => _metadata.GetString(_metadata.GetMethodDefinition(h).Name) == methodName).ToList();
            // An async method's real calls live in MoveNext, not the wrapper body.
            foreach (var nested in type.GetNestedTypes())
            {
                var definition = _metadata.GetTypeDefinition(nested);
                if (_metadata.GetString(definition.Name).StartsWith("<" + methodName + ">d__", StringComparison.Ordinal))
                    methods.AddRange(definition.GetMethods().Where(h => _metadata.GetString(_metadata.GetMethodDefinition(h).Name) == "MoveNext"));
            }
            foreach (var handle in methods)
            {
                var method = _metadata.GetMethodDefinition(handle);
                if (method.RelativeVirtualAddress == 0) continue;
                var reader = _pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
                while (reader.RemainingBytes > 0)
                {
                    byte first = reader.ReadByte();
                    short code = first == 0xfe ? unchecked((short)(0xfe00 | reader.ReadByte())) : first;
                    var op = Opcodes[code];
                    if (op.OperandType == operandType)
                    {
                        operands.Add(reader.ReadInt32());
                        continue;
                    }
                    int size = op.OperandType switch
                    {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineSwitch => checked(reader.ReadInt32() * 4),
                        _ => 4
                    };
                    reader.Offset += size;
                }
            }
            return operands;
        }

        private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(op => op.Value);

        public void Dispose() { _pe.Dispose(); _stream.Dispose(); }
    }

    private sealed class SignatureNames : ISignatureTypeProvider<string, object?>
    {
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "methodptr";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> arguments) =>
            genericType.Split('`')[0] + "<" + string.Join(",", arguments) + ">";
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetModifiedType(string modifierType, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Boolean => "System.Boolean", PrimitiveTypeCode.Byte => "System.Byte",
            PrimitiveTypeCode.Char => "System.Char", PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.Int16 => "System.Int16", PrimitiveTypeCode.Int32 => "System.Int32",
            PrimitiveTypeCode.Int64 => "System.Int64", PrimitiveTypeCode.IntPtr => "System.IntPtr",
            PrimitiveTypeCode.Object => "System.Object", PrimitiveTypeCode.SByte => "System.SByte",
            PrimitiveTypeCode.Single => "System.Single", PrimitiveTypeCode.String => "System.String",
            PrimitiveTypeCode.TypedReference => "System.TypedReference", PrimitiveTypeCode.UInt16 => "System.UInt16",
            PrimitiveTypeCode.UInt32 => "System.UInt32", PrimitiveTypeCode.UInt64 => "System.UInt64",
            PrimitiveTypeCode.UIntPtr => "System.UIntPtr", PrimitiveTypeCode.Void => "System.Void",
            _ => typeCode.ToString()
        };
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeDefinition(handle);
            return FullName(reader, type.Namespace, type.Name);
        }
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            return FullName(reader, type.Namespace, type.Name);
        }
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext,
            TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        private static string FullName(MetadataReader reader, StringHandle ns, StringHandle name)
        {
            string namespaceName = reader.GetString(ns);
            return string.IsNullOrEmpty(namespaceName) ? reader.GetString(name) :
                namespaceName + "." + reader.GetString(name);
        }
    }
}
