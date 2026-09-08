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

internal static class GameApiCompatibilityTests
{
    private static int _checks;

    private static int Main()
    {
        try
        {
            string root = FindRepositoryRoot();
            using var game = MetadataAssembly.Open(Path.Combine(root, "references", "sts2.dll"));
            Equal(new Guid("73b63ee0-6c0a-47bb-b0d1-b21f6d94222e"), game.ModuleVersionId,
                "v0.111 module MVID");

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
                "System.Collections.Generic.IEnumerable<MegaCrit.Sts2.Core.Entities.Creatures.Creature>",
                "System.Decimal", "MegaCrit.Sts2.Core.ValueProps.ValueProp",
                "MegaCrit.Sts2.Core.Entities.Creatures.Creature", "MegaCrit.Sts2.Core.Models.CardModel",
                "MegaCrit.Sts2.Core.Entities.Cards.CardPlay");

            Check(!game.HasType("MegaCrit.Sts2.Core.Models.Powers.DiamondDiademPower"),
                "legacy DiamondDiademPower removed");
            Equal(new[] { 3 }, game.ParameterCounts(
                "MegaCrit.Sts2.Core.Models.Relics.DiamondDiadem", "AfterSideTurnStart"),
                "DiamondDiadem v0.111 hook");
            Equal(new[] { 2 }, game.ParameterCounts(
                "MegaCrit.Sts2.Core.Models.Powers.HibernatePower", "AfterPlayerTurnStart"),
                "Hibernate v0.111 hook");
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

            Console.WriteLine($"PASS {_checks} v0.111 metadata compatibility checks.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL v0.111 compatibility: " + ex);
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
        Check(mod.Calls(cards, "ReadVisual", "MegaCrit.Sts2.Core.Entities.Cards.CardEnergyCost", "GetWithModifiers", 1),
            "card display preserves negative native costs");
        Check(!mod.Calls(cards, "ReadVisual", "MegaCrit.Sts2.Core.Entities.Cards.CardEnergyCost", "GetResolved", 0),
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
        foreach (string hook in new[] { "BeforeAttack", "AfterAttack", "ModifyAttackHitCount" })
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
        string snapshot = Path.Combine(root, "references", "v111", "sts2.dll");
        if (File.Exists(snapshot))
            Check(File.ReadAllBytes(snapshot).SequenceEqual(File.ReadAllBytes(Path.Combine(root, "references", "sts2.dll"))),
                "canonical references equal audited v111 snapshot");
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
        Equal("3.6.2-v111", manifest.GetProperty("version").GetString(), "manifest version");
        Equal("0.111.0", manifest.GetProperty("min_game_version").GetString(), "minimum game version");
        Equal(false, manifest.GetProperty("affects_gameplay").GetBoolean(), "non-gameplay manifest flag");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BetterSpire2Lite.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("BetterSpire repository root");
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

        internal bool Calls(string owner, string methodName, string calleeOwner, string calleeName, int parameterCount)
        {
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
                if (declaring == calleeOwner && name == calleeName && count == parameterCount) return true;
            }
            return false;
        }

        internal IEnumerable<string> Literals(string owner, string methodName) =>
            Operands(owner, methodName, OperandType.InlineString)
                .Select(token => _metadata.GetUserString(MetadataTokens.UserStringHandle(token & 0x00ffffff)));

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
