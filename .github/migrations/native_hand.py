from pathlib import Path
r=Path('.')
(r/'BetterSpire2/HandViewer/DetachedCardPreview.cs').write_text('''#nullable enable
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
''')
p=r/'BetterSpire2/HandViewer/PlayerHandSection.cs';s=p.read_text()
s=s.replace('                card.AfflictionChanged += OnCardChanged; card.KeywordsChanged += OnCardChanged;', '                card.AfflictionChanged += OnCardChanged; card.KeywordsChanged += OnCardChanged;\n                card.Forged += OnCardChanged; card.ReplayCountChanged += OnCardChanged;')
s=s.replace('                card.AfflictionChanged -= OnCardChanged; card.KeywordsChanged -= OnCardChanged;', '                card.AfflictionChanged -= OnCardChanged; card.KeywordsChanged -= OnCardChanged;\n                card.Forged -= OnCardChanged; card.ReplayCountChanged -= OnCardChanged;')
s=s.replace('            foreach (var card in cards) TryUpdateDynamicVarPreview(card);\n','')
a=s.index('        private void TryUpdateDynamicVarPreview(');b=s.index('        private void RefreshStatus()',a);s=s[:a]+s[b:];p.write_text(s)
p=r/'BetterSpire2/HandViewer/TeammateHandViewer.Text.cs';s=p.read_text()
s=s.replace('            // The bundled DLL exposes this overload. No reflection or dynamic dispatch on hover.\n            string description = card.GetDescriptionForPile(card.Pile?.Type ?? PileType.Hand, null);\n            return CleanDescriptionText(description);','''            string description = DetachedCardPreview.Description(card);
            return CleanDescriptionText(description) + "\\n\\n" + (ModText.IsFrench
                ? "Aperçu sans cible sélectionnée." : "Preview without a selected target.");''')
s=s.replace('return CleanDescriptionText(card.Description?.GetFormattedText() ?? "");','''// No fallback writes or formatting on shared live variables.
            return ModText.IsFrench ? "Aperçu isolé indisponible pour cette carte. Les statistiques restent actives."
                : "Isolated preview unavailable for this card. Combat statistics remain active.";''');p.write_text(s)
p=r/'BetterSpire2/HandViewer/PlayerHandCards.cs';s=p.read_text()
s=s.replace('            internal int Generation;\n            internal CardView(CardVisual visual, Control control) { Visual = visual; Control = control; }','''            internal int Generation;
            internal TextureRect Portrait;
            internal Label Missing, Cost;
            internal StyleBoxFlat Border;
            internal CardView(CardVisual visual, Control control, TextureRect portrait,
                Label missing, Label cost, StyleBoxFlat border)
            { Visual = visual; Control = control; Portrait = portrait; Missing = missing; Cost = cost; Border = border; }''')
s=s.replace(' || view.Visual != visual','')
s=s.replace('                    var control = BuildCompactCard(visual.Portrait, visual.Cost, visual.TypeColor);\n                    view = new CardView(visual, control);','                    view = BuildCompactCard(visual);')
s=s.replace('                    _cardRow!.AddChild(control);','                    _cardRow!.AddChild(view.Control);')
s=s.replace('                view.Generation = _viewGeneration;','                else if (view.Visual != visual) UpdateCard(view, visual);\n                view.Generation = _viewGeneration;')
a=s.index('        private static Control BuildCompactCard(');b=s.index('        private void ClearCards()',a)
s=s[:a]+'''        private static CardView BuildCompactCard(CardVisual visual)
        {
            var card = new Control
            {
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
                MouseFilter = Control.MouseFilterEnum.Stop, ClipContents = true
            };
            var image = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            image.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); card.AddChild(image);
            var missing = UiHelpers.CreateLabel("?", HudTheme.Muted, 12, HorizontalAlignment.Center);
            missing.VerticalAlignment = VerticalAlignment.Center;
            missing.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            missing.MouseFilter = Control.MouseFilterEnum.Ignore;
            HudTheme.Outline(missing); card.AddChild(missing);
            var edge = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
            edge.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var border = UiHelpers.CreatePanelStyle(Colors.Transparent, new Color(visual.TypeColor, .7f), 1, 2, 0);
            edge.AddThemeStyleboxOverride("panel", border); card.AddChild(edge);
            var cost = UiHelpers.CreateLabel("", Colors.White, 10);
            cost.MouseFilter = Control.MouseFilterEnum.Ignore;
            cost.Position = new Vector2(2, 0); cost.ClipText = true;
            cost.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            cost.AddThemeStyleboxOverride("normal", UiHelpers.CreatePanelStyle(new Color(0, 0, 0, .72f), Colors.Transparent, 0, 2, 0));
            HudTheme.Outline(cost); card.AddChild(cost);
            var view = new CardView(visual, card, image, missing, cost, border);
            UpdateCard(view, visual, force: true);
            return view;
        }
        private static void UpdateCard(CardView view, CardVisual visual, bool force = false)
        {
            // Description-only changes touch no Godot properties or node structure.
            if (force || view.Visual.Scale != visual.Scale)
            {
                view.Control.CustomMinimumSize = new Vector2(CardWidth, PortraitHeight);
                view.Cost.Size = new Vector2(Math.Max(1, CardWidth - 4), 14);
            }
            if (force || view.Visual.Portrait != visual.Portrait)
            {
                view.Portrait.Texture = visual.Portrait;
                view.Portrait.Visible = visual.Portrait != null;
                view.Missing.Visible = visual.Portrait == null;
            }
            if (force || view.Visual.Cost != visual.Cost)
            { view.Cost.Text = visual.Cost; view.Cost.Visible = visual.Cost.Length > 0; }
            if (force || view.Visual.TypeColor != visual.TypeColor)
                view.Border.BorderColor = new Color(visual.TypeColor, .7f);
            view.Visual = visual;
        }

'''+s[b:];p.write_text(s)
