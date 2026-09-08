#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace BetterSpire2.HandViewer;

/// <summary>
/// An unregistered presentation copy, with no writes to original preview values.
/// v111 MutableClone deep-clones BEFORE clearing event subscribers; enchanted or
/// afflicted clones can therefore invoke the original's shallow-copied events.
/// Strip event backing fields on an unpublished shell before native deep cloning.
/// This cached reflection boundary never changes subscribers on the live model.
/// </summary>
internal static class DetachedCardPreview
{
    private static readonly MethodInfo ShallowCopy = typeof(object).GetMethod("MemberwiseClone",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly Dictionary<Type, FieldInfo[]> EventFields = new();

    internal static string Description(CardModel original)
    {
        ArgumentNullException.ThrowIfNull(original);
        EnsureNative(original);
        if (original.Enchantment != null) EnsureNative(original.Enchantment);
        if (original.Affliction != null) EnsureNative(original.Affliction);
        var shell = (CardModel)ShallowCopy.Invoke(original, null)!;
        foreach (var field in GetEventFields(original.GetType())) field.SetValue(shell, null);
        // CreateClone would register a new card with CardScope. MutableClone does not.
        var copy = (CardModel)shell.MutableClone();
        if (ReferenceEquals(copy, original) || ReferenceEquals(copy.DynamicVars, original.DynamicVars))
            throw new InvalidOperationException("Native card preview was not detached.");
        foreach (var pair in copy.DynamicVars)
            if (original.DynamicVars.TryGetValue(pair.Key, out var live) && ReferenceEquals(live, pair.Value))
                throw new InvalidOperationException("Native preview variable aliases the live card.");
        // Calculate using the real owner/card identity, but write only detached variables.
        copy.DynamicVars.ClearPreview();
        original.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, copy.DynamicVars);
        if (copy.Enchantment != null)
        {
            if (ReferenceEquals(copy.Enchantment, original.Enchantment) ||
                ReferenceEquals(copy.Enchantment.DynamicVars, original.Enchantment!.DynamicVars))
                throw new InvalidOperationException("Native enchantment preview was not detached.");
            copy.Enchantment.DynamicVars.ClearPreview();
            original.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, copy.Enchantment.DynamicVars);
        }
        return copy.GetDescriptionForPile(original.Pile?.Type ?? PileType.Hand, null);
    }
    private static void EnsureNative(AbstractModel model)
    {
        if (model.GetType().Assembly != typeof(CardModel).Assembly)
            throw new NotSupportedException("External model preview requires an audited isolation adapter.");
    }
    private static FieldInfo[] GetEventFields(Type type)
    {
        if (EventFields.TryGetValue(type, out var cached)) return cached;
        var fields = new List<FieldInfo>();
        for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
            foreach (var signal in current.GetEvents(BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var field = current.GetField(signal.Name, BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field == null || !typeof(Delegate).IsAssignableFrom(field.FieldType))
                    throw new NotSupportedException("Native event backing changed: " + signal.Name);
                fields.Add(field);
            }
        return EventFields[type] = fields.ToArray();
    }
}
