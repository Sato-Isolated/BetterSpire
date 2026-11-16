#nullable enable
using System;
using BetterSpire2.Guardian.Core;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace BetterSpire2.Guardian.UI;

/// <summary>A single text node. No panel, button, tooltip or mouse/focus target.</summary>
internal sealed class OverheadLabel
{
    internal readonly Label TextNode;
    internal NCreature CreatureNode;
    internal readonly OverheadDisplayState Display = new();
    internal ActorForecast? Actor => Display.Actor;
    internal int Generation;
    internal int Order;
    private string? _text;
    private OverheadSeverity? _severity;
    private int _fontSize;
    internal Vector2 Size { get; private set; }

    internal OverheadLabel(NCreature creature, Control root)
    {
        CreatureNode = creature;
        TextNode = new Label
        {
            Name = "GuardianHpReadout", Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore, FocusMode = Control.FocusModeEnum.None,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            // Do not truncate HP or loss into a misleading number.
            ClipText = false, TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming
        };
        TextNode.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        TextNode.AddThemeColorOverride("font_outline_color", new Color(0.025f, 0.025f, 0.035f, .94f));
        TextNode.AddThemeConstantOverride("outline_size", 4);
        root.AddChild(TextNode);
    }

    internal void Bind(NCreature node, ActorForecast? actor, bool partial, int generation, int order,
        bool branchEnumerationComplete = true)
    {
        CreatureNode = node;
        Display.Bind(actor, partial, branchEnumerationComplete);
        Generation = generation;
        Order = order;
    }

    internal void Apply(string text, OverheadSeverity severity)
    {
        int size = Math.Clamp((int)Math.Round(22 * ModSettings.GuardianScalePercent / 100f), 16, 33);
        bool measure = false;
        if (_fontSize != size)
        {
            _fontSize = size;
            TextNode.AddThemeFontSizeOverride("font_size", size);
            measure = true;
        }
        if (_text != text) { _text = text; TextNode.Text = text; measure = true; }
        if (_severity != severity)
        {
            _severity = severity;
            Color color = severity switch
            {
                OverheadSeverity.Lethal => new Color(1f, .32f, .30f),
                OverheadSeverity.Uncertain => new Color(1f, .79f, .40f),
                OverheadSeverity.Loss => new Color(1f, .85f, .78f),
                _ => new Color(.94f, .97f, 1f)
            };
            TextNode.AddThemeColorOverride("font_color", color);
        }
        if (!measure) return;
        TextNode.ResetSize();
        // Include the outline in the collision rectangle, not just the text's advance width.
        Size = TextNode.GetCombinedMinimumSize() + new Vector2(8, 4);
        TextNode.Size = Size;
    }

    internal void Free()
    {
        if (GodotObject.IsInstanceValid(TextNode)) { TextNode.Visible = false; TextNode.QueueFree(); }
    }
}
