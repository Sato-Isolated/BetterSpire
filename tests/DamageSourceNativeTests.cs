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
    private static int _checks;

    private static int Main()
    {
        try
        {
            VerifyOneShotForwarding();
            string root = FindRepositoryRoot();
            using var game = Image.Open(Path.Combine(root, "references", "sts2.dll"));
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

            using var mod = Image.Open(Path.Combine(root, "bin", "Release", "net9.0", "BetterSpire2Lite.dll"));
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

            Console.WriteLine($"PASS {_checks} v0.113 native damage-source metadata checks.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL v0.113 native damage-source metadata: " + ex);
            return 1;
        }
    }

    private static void VerifyPatch(Image mod, string patch, string wrapper, string capability)
    {
        Check(mod.HasMethod(patch, "Prepare"), patch + " has a capability check");
        Check(mod.Calls(patch, "Prepare", "BetterSpire2.Journal.Game.DamageObservationCoverage", "Require", 2),
            patch + " reports patch availability");
        Check(mod.Literals(patch, "Prepare").Contains(capability), patch + " reports the stable capability key");
        Check(mod.Literals(patch, "Transpiler").Contains(wrapper), patch + " targets its exact source wrapper");
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

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        _checks++;
    }

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

    private sealed class Image : IDisposable
    {
        private readonly FileStream _stream;
        private readonly PEReader _pe;
        private readonly MetadataReader _metadata;
        private readonly Names _names = new();

        private Image(string path)
        {
            _stream = File.OpenRead(path);
            _pe = new PEReader(_stream);
            _metadata = _pe.GetMetadataReader();
        }

        internal static Image Open(string path) => new(path);
        internal bool HasMethod(string owner, string name) => FindType(owner) is { } type &&
            _metadata.GetTypeDefinition(type).GetMethods().Any(h => _metadata.GetString(_metadata.GetMethodDefinition(h).Name) == name);

        internal void RequireMethod(string owner, string name, params string[] parameters)
        {
            var type = _metadata.GetTypeDefinition(FindType(owner) ?? throw new TypeLoadException(owner));
            bool found = type.GetMethods().Select(_metadata.GetMethodDefinition)
                .Where(m => _metadata.GetString(m.Name) == name)
                .Any(m => m.DecodeSignature(_names, null).ParameterTypes.SequenceEqual(parameters));
            Check(found, owner + "." + name + " signature matches v113");
        }

        internal void RequireStateField(string owner, string method, string fieldName, string fieldType)
        {
            var state = StateMachine(owner, method);
            bool found = _metadata.GetTypeDefinition(state).GetFields().Any(handle =>
            {
                var field = _metadata.GetFieldDefinition(handle);
                return _metadata.GetString(field.Name) == fieldName && field.DecodeSignature(_names, null) == fieldType;
            });
            Check(found, owner + "." + method + " captures " + fieldName + " as " + fieldType);
        }

        internal void RequireUniqueStateField(string owner, string method, string fieldType)
        {
            var fields = _metadata.GetTypeDefinition(StateMachine(owner, method)).GetFields()
                .Select(handle => _metadata.GetFieldDefinition(handle))
                .Count(field => field.DecodeSignature(_names, null) == fieldType);
            Check(fields == 1, owner + "." + method + " has one captured " + fieldType + " field");
        }

        private TypeDefinitionHandle StateMachine(string owner, string method)
        {
            var type = _metadata.GetTypeDefinition(FindType(owner) ?? throw new TypeLoadException(owner));
            return type.GetNestedTypes().SingleOrDefault(handle =>
            {
                string name = _metadata.GetString(_metadata.GetTypeDefinition(handle).Name);
                return name.StartsWith("<" + method + ">d__", StringComparison.Ordinal);
            }) is { IsNil: false } state ? state : throw new TypeLoadException(owner + ".<" + method + "> state machine");
        }

        internal int CallCount(string owner, string method, string calleeOwner, string calleeName, int parameterCount)
        {
            int count = 0;
            foreach (int token in MethodOperands(owner, method, OperandType.InlineMethod))
            {
                EntityHandle target = MetadataTokens.EntityHandle(token);
                if (target.Kind == HandleKind.MethodSpecification)
                    target = _metadata.GetMethodSpecification((MethodSpecificationHandle)target).Method;
                string declaring, name;
                int parameters;
                if (target.Kind == HandleKind.MethodDefinition)
                {
                    var definition = _metadata.GetMethodDefinition((MethodDefinitionHandle)target);
                    declaring = TypeName(definition.GetDeclaringType());
                    name = _metadata.GetString(definition.Name);
                    parameters = definition.DecodeSignature(_names, null).ParameterTypes.Length;
                }
                else if (target.Kind == HandleKind.MemberReference)
                {
                    var reference = _metadata.GetMemberReference((MemberReferenceHandle)target);
                    declaring = reference.Parent.Kind switch
                    {
                        HandleKind.TypeReference => _names.ReferenceName(_metadata, (TypeReferenceHandle)reference.Parent),
                        HandleKind.TypeDefinition => TypeName((TypeDefinitionHandle)reference.Parent),
                        HandleKind.TypeSpecification => _metadata.GetTypeSpecification((TypeSpecificationHandle)reference.Parent)
                            .DecodeSignature(_names, null),
                        _ => ""
                    };
                    name = _metadata.GetString(reference.Name);
                    parameters = reference.DecodeMethodSignature(_names, null).ParameterTypes.Length;
                }
                else continue;
                if (declaring == calleeOwner && name == calleeName && parameters == parameterCount) count++;
            }
            return count;
        }

        internal bool Calls(string owner, string method, string calleeOwner, string calleeName, int parameterCount) =>
            CallCount(owner, method, calleeOwner, calleeName, parameterCount) > 0;

        internal void RequireCallCount(string owner, string method, string calleeOwner, string calleeName,
            int parameterCount, int expected, string label) =>
            Check(CallCount(owner, method, calleeOwner, calleeName, parameterCount) == expected, label);

        internal IEnumerable<string> Literals(string owner, string method) =>
            MethodOperands(owner, method, OperandType.InlineString).Select(token =>
                _metadata.GetUserString(MetadataTokens.UserStringHandle(token & 0x00ffffff)));

        internal IEnumerable<string> AllLiterals(string owner)
        {
            var definition = _metadata.GetTypeDefinition(FindType(owner) ?? throw new TypeLoadException(owner));
            var methods = definition.GetMethods().ToList();
            foreach (var nested in definition.GetNestedTypes())
                methods.AddRange(_metadata.GetTypeDefinition(nested).GetMethods());
            return ReadOperands(methods, OperandType.InlineString).Select(token =>
                _metadata.GetUserString(MetadataTokens.UserStringHandle(token & 0x00ffffff))).ToArray();
        }

        private IEnumerable<int> MethodOperands(string owner, string methodName, OperandType wanted)
        {
            var definition = _metadata.GetTypeDefinition(FindType(owner) ?? throw new TypeLoadException(owner));
            var methods = definition.GetMethods().Where(h => _metadata.GetString(_metadata.GetMethodDefinition(h).Name) == methodName).ToList();
            foreach (var nested in definition.GetNestedTypes())
            {
                var stateMachine = _metadata.GetTypeDefinition(nested);
                if (_metadata.GetString(stateMachine.Name).StartsWith("<" + methodName + ">d__", StringComparison.Ordinal))
                    methods.AddRange(stateMachine.GetMethods().Where(h => _metadata.GetString(_metadata.GetMethodDefinition(h).Name) == "MoveNext"));
            }
            return ReadOperands(methods, wanted);
        }

        private IEnumerable<int> ReadOperands(IEnumerable<MethodDefinitionHandle> methods, OperandType wanted)
        {
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
                    if (op.OperandType == wanted)
                    {
                        yield return reader.ReadInt32();
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
        }

        private TypeDefinitionHandle? FindType(string fullName)
        {
            foreach (var type in _metadata.TypeDefinitions)
                if (TypeName(type) == fullName) return type;
            return null;
        }

        private string TypeName(TypeDefinitionHandle handle)
        {
            var type = _metadata.GetTypeDefinition(handle);
            string name = _metadata.GetString(type.Name);
            if (!type.GetDeclaringType().IsNil) return TypeName(type.GetDeclaringType()) + "+" + name;
            string ns = _metadata.GetString(type.Namespace);
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }

        private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(op => op.Value);

        public void Dispose() { _pe.Dispose(); _stream.Dispose(); }
    }

    private sealed class Names : ISignatureTypeProvider<string, object?>
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
            PrimitiveTypeCode.UInt16 => "System.UInt16", PrimitiveTypeCode.UInt32 => "System.UInt32",
            PrimitiveTypeCode.UInt64 => "System.UInt64", PrimitiveTypeCode.Void => "System.Void",
            _ => typeCode.ToString()
        };
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            DefinitionName(reader, handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => ReferenceName(reader, handle);
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        internal string ReferenceName(MetadataReader reader, TypeReferenceHandle handle)
        {
            var type = reader.GetTypeReference(handle);
            string ns = reader.GetString(type.Namespace), name = reader.GetString(type.Name);
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }
        private static string DefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
        {
            var type = reader.GetTypeDefinition(handle);
            string name = reader.GetString(type.Name);
            if (!type.GetDeclaringType().IsNil) return DefinitionName(reader, type.GetDeclaringType()) + "+" + name;
            string ns = reader.GetString(type.Namespace);
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }
    }
}
