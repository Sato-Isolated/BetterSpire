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

/// <summary>Shared assertion counter for IL contract test executables.</summary>
internal static class Contract
{
    internal static int Checks;
    internal static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Checks++;
    }
    internal static void Equal<T>(T expected, T actual, string label)
    {
        bool equal = expected is Array expectedArray && actual is Array actualArray
            ? expectedArray.Cast<object>().SequenceEqual(actualArray.Cast<object>())
            : EqualityComparer<T>.Default.Equals(expected, actual);
        if (!equal) throw new InvalidOperationException($"{label}: expected {Format(expected)}, got {Format(actual)}");
        Checks++;
    }
    private static string Format<T>(T value) => value is Array array
        ? "[" + string.Join(", ", array.Cast<object>()) + "]"
        : value?.ToString() ?? "null";
}

/// <summary>Reads a compiled assembly's metadata and IL without loading it (sts2 needs the game runtime).</summary>
internal sealed class MetadataAssembly : IDisposable
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

    internal bool HasMethod(string owner, string name) => FindType(owner) is { } type &&
        _metadata.GetTypeDefinition(type).GetMethods().Any(h => _metadata.GetString(_metadata.GetMethodDefinition(h).Name) == name);

    internal void RequireStateField(string owner, string method, string fieldName, string fieldType) =>
        Contract.Check(StateFields(owner, method).Any(f => f.Name == fieldName && f.Type == fieldType),
            owner + "." + method + " captures " + fieldName + " as " + fieldType);

    /// <summary>Transpilers locate captured async state by field type, so it must be unambiguous.</summary>
    internal void RequireUniqueStateField(string owner, string method, string fieldType) =>
        Contract.Check(StateFields(owner, method).Count(f => f.Type == fieldType) == 1,
            owner + "." + method + " has one captured " + fieldType + " field");

    private IEnumerable<(string Name, string Type)> StateFields(string owner, string method)
    {
        var type = _metadata.GetTypeDefinition(FindType(owner) ?? throw new TypeLoadException(owner));
        var state = type.GetNestedTypes().SingleOrDefault(handle => _metadata.GetString(_metadata.GetTypeDefinition(handle).Name)
            .StartsWith("<" + method + ">d__", StringComparison.Ordinal));
        if (state.IsNil) throw new TypeLoadException(owner + ".<" + method + "> state machine");
        return _metadata.GetTypeDefinition(state).GetFields().Select(_metadata.GetFieldDefinition)
            .Select(f => (_metadata.GetString(f.Name), f.DecodeSignature(_names, null))).ToArray();
    }

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
        Contract.Check(found, fullName + "." + methodName + "(" + string.Join(", ", parameters) + ")");
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
        Contract.Check(found, "visibility/static/return/parameters: " + owner + "." + name);
    }

    internal void RequireNotificationOnlyOverrides(string owner, IEnumerable<string> hooks)
    {
        var expected = hooks.Concat(new[] { "get_ShouldReceiveCombatHooks", "AfterCloned" }).ToHashSet(StringComparer.Ordinal);
        var definition = _metadata.GetTypeDefinition(FindType(owner) ?? throw new TypeLoadException(owner));
        Contract.Check((definition.Attributes & TypeAttributes.Sealed) != 0, "observer is sealed; no subclass allowlist bypass");
        foreach (var handle in definition.GetMethods())
        {
            var method = _metadata.GetMethodDefinition(handle);
            if ((method.Attributes & MethodAttributes.Virtual) == 0) continue;
            Contract.Check(expected.Remove(_metadata.GetString(method.Name)), "only audited notification/lifetime overrides");
        }
        Contract.Check(expected.Count == 0, "all audited observer overrides present");
        // SavedProperty attributes would put observer state into serialization.
        foreach (var property in definition.GetProperties())
            foreach (var attr in _metadata.GetPropertyDefinition(property).GetCustomAttributes())
            {
                var ctor = _metadata.GetCustomAttribute(attr).Constructor;
                if (ctor.Kind != HandleKind.MemberReference) continue;
                var parent = _metadata.GetMemberReference((MemberReferenceHandle)ctor).Parent;
                if (parent.Kind != HandleKind.TypeReference) continue;
                string attribute = _names.GetTypeFromReference(_metadata, (TypeReferenceHandle)parent, 0);
                Contract.Check(!attribute.EndsWith("SavedPropertyAttribute", StringComparison.Ordinal), "observer has no saved properties");
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

    internal HashSet<string> CallerTypes(string calleeOwner, string calleeName) =>
        InternalCalls().Where(c => c.CalleeName == calleeName && c.CalleeOwner == calleeOwner)
            .Select(c => c.Caller).ToHashSet(StringComparer.Ordinal);

    /// <summary>"Caller -> Callee.Method" for calls from a namespace (or one type) into a namespace, inside this assembly.</summary>
    internal string[] CallsAcross(string callerNamespace, string calleeNamespace) =>
        InternalCalls().Where(c => (c.Caller == callerNamespace ||
                c.Caller.StartsWith(callerNamespace + ".", StringComparison.Ordinal)) &&
                c.CalleeOwner.StartsWith(calleeNamespace + ".", StringComparison.Ordinal))
            .Select(c => c.Caller + " -> " + c.CalleeOwner + "." + c.CalleeName).Distinct().ToArray();

    /// <summary>Every call to a method defined in this assembly. Callers are top-level types, so
    /// async state machines and closures report their owning type.</summary>
    private IEnumerable<(string Caller, string CalleeOwner, string CalleeName)> InternalCalls()
    {
        foreach (var typeHandle in _metadata.TypeDefinitions)
        {
            string caller = DefinitionName(typeHandle).Split('+')[0];
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
                    yield return (caller, DefinitionName(target.GetDeclaringType()), _metadata.GetString(target.Name));
                }
            }
        }
    }

    /// <summary>Types defined here that carry an attribute declared elsewhere (e.g. HarmonyLib.HarmonyPatch).</summary>
    internal string[] TypesWithAttribute(string attributeType) => _metadata.TypeDefinitions
        .Where(handle => _metadata.GetTypeDefinition(handle).GetCustomAttributes().Any(a =>
        {
            var ctor = _metadata.GetCustomAttribute(a).Constructor;
            return ctor.Kind == HandleKind.MemberReference &&
                _metadata.GetMemberReference((MemberReferenceHandle)ctor).Parent is { Kind: HandleKind.TypeReference } parent &&
                _names.GetTypeFromReference(_metadata, (TypeReferenceHandle)parent, 0) == attributeType;
        }))
        .Select(DefinitionName).ToArray();

    /// <summary>Types of this assembly loaded with typeof(...) (ldtoken) inside a method.</summary>
    internal HashSet<string> TypeofTargets(string owner, string methodName) =>
        Operands(owner, methodName, OperandType.InlineTok).Select(MetadataTokens.EntityHandle)
            .Where(h => h.Kind == HandleKind.TypeDefinition).Select(h => DefinitionName((TypeDefinitionHandle)h))
            .ToHashSet(StringComparer.Ordinal);
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
                    Contract.Check(!(native && (name == "get_Rng" || declaring.Contains("Rng", StringComparison.Ordinal) ||
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
                Contract.Check(index >= 0 && index + 4 < instructions.Count &&
                    instructions[index + 1].Op.Name!.StartsWith("ldloc", StringComparison.Ordinal) &&
                    instructions[index + 2].Op == OpCodes.Ldfld &&
                    IsGetter(instructions[index + 3].Token, "get_OverkillDamage") &&
                    instructions[index + 4].Op == OpCodes.Add,
                    "v113 completed-result HP + overkill observation pattern");
                matches++;
            }
        }
        Contract.Equal(1, matches, "exactly one central damage result observation loop");
    }

    private List<(OpCode Op, int Token)> ReadInstructions(MethodDefinition method)
    {
        var instructions = new List<(OpCode, int)>();
        var reader = _pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
        while (reader.RemainingBytes > 0)
        {
            byte first = reader.ReadByte();
            var op = Opcodes[first == 0xfe ? unchecked((short)(0xfe00 | reader.ReadByte())) : first];
            if (op.OperandType is OperandType.InlineMethod or OperandType.InlineString or OperandType.InlineTok)
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
                    // Generic owners such as OneShotDamageScope<T>.
                    HandleKind.TypeSpecification => _metadata.GetTypeSpecification((TypeSpecificationHandle)target.Parent)
                        .DecodeSignature(_names, null),
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

    internal void RequireCallCount(string owner, string method, string calleeOwner, string calleeName,
        int parameterCount, int expected, string label) =>
        Contract.Check(CallCount(owner, method, calleeOwner, calleeName, parameterCount) == expected, label);

    internal IEnumerable<string> Literals(string owner, string methodName) =>
        Operands(owner, methodName, OperandType.InlineString)
            .Select(token => _metadata.GetUserString(MetadataTokens.UserStringHandle(token & 0x00ffffff)));

    /// <summary>String literals of every method of the type and its nested (closure/state machine) types.</summary>
    internal string[] AllLiterals(string owner)
    {
        var definition = _metadata.GetTypeDefinition(FindType(owner) ?? throw new TypeLoadException(owner));
        var methods = definition.GetMethods().ToList();
        foreach (var nested in definition.GetNestedTypes())
            methods.AddRange(_metadata.GetTypeDefinition(nested).GetMethods());
        return methods.Select(_metadata.GetMethodDefinition).Where(m => m.RelativeVirtualAddress != 0)
            .SelectMany(ReadInstructions).Where(i => i.Op.OperandType == OperandType.InlineString)
            .Select(i => _metadata.GetUserString(MetadataTokens.UserStringHandle(i.Token & 0x00ffffff))).ToArray();
    }

    internal bool UsesTypeArgument(string owner, string methodName, string type) =>
        Operands(owner, methodName, OperandType.InlineMethod).Select(MetadataTokens.EntityHandle)
            .Where(handle => handle.Kind == HandleKind.MethodSpecification)
            .Any(handle => _metadata.GetMethodSpecification((MethodSpecificationHandle)handle)
                .DecodeSignature(_names, null).Contains(type));

    private List<int> Operands(string owner, string methodName, OperandType operandType)
    {
        var type = _metadata.GetTypeDefinition(FindType(owner) ?? throw new TypeLoadException(owner));
        var methods = type.GetMethods().Where(h => _metadata.GetString(_metadata.GetMethodDefinition(h).Name) == methodName).ToList();
        // An async method's real calls live in MoveNext, not the wrapper body.
        foreach (var nested in type.GetNestedTypes())
        {
            var definition = _metadata.GetTypeDefinition(nested);
            if (_metadata.GetString(definition.Name).StartsWith("<" + methodName + ">d__", StringComparison.Ordinal))
                methods.AddRange(definition.GetMethods().Where(h => _metadata.GetString(_metadata.GetMethodDefinition(h).Name) == "MoveNext"));
        }
        return methods.Select(_metadata.GetMethodDefinition).Where(m => m.RelativeVirtualAddress != 0)
            .SelectMany(ReadInstructions).Where(i => i.Op.OperandType == operandType).Select(i => i.Token).ToList();
    }

    private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(op => op.Value);

    public void Dispose() { _pe.Dispose(); _stream.Dispose(); }
}

internal sealed class SignatureNames : ISignatureTypeProvider<string, object?>
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
