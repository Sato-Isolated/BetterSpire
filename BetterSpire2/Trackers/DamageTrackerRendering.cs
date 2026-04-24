#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace BetterSpire2.Trackers;

public static partial class DamageTracker
{
	private static readonly Dictionary<Creature, Label> _labels = new Dictionary<Creature, Label>();

	private static readonly Color _incomingColor = new Color(1f, 0.3f, 0.3f);

	private static readonly Color _safeColor = new Color(0.3f, 1f, 0.3f);

	private static readonly Color _petColor = new Color(1f, 0.6f, 0.2f);

	private static Font? _cachedFont;

	private static void RenderLabels(Dictionary<Creature, LabelRenderInfo> labelsToRender)
	{
		foreach (KeyValuePair<Creature, LabelRenderInfo> item in labelsToRender)
		{
			ShowLabel(item.Key, item.Value.Text, item.Value.Color);
		}

		foreach (Creature creature in _labels.Keys.Where(creature => !labelsToRender.ContainsKey(creature)).ToList())
		{
			RemoveLabel(creature);
		}
	}

	private static void RefreshLabelPositions()
	{
		NCombatRoom? instance = NCombatRoom.Instance;
		if (instance == null)
		{
			return;
		}

		foreach (Creature creature in _labels.Keys.ToList())
		{
			if (!_labels.TryGetValue(creature, out Label? label) || label == null || !GodotObject.IsInstanceValid(label))
			{
				_labels.Remove(creature);
				continue;
			}

			NCreature? creatureNode = instance.GetCreatureNode(creature);
			if (creatureNode != null)
			{
				label.GlobalPosition = new Vector2(creatureNode.GlobalPosition.X + 10f, creatureNode.GlobalPosition.Y - 320f);
			}
		}
	}

	private static void ShowLabel(Creature creature, string text, Color color)
	{
		NCombatRoom? instance = NCombatRoom.Instance;
		if (instance == null)
		{
			return;
		}

		if (!_labels.TryGetValue(creature, out Label? label) || !GodotObject.IsInstanceValid(label))
		{
			label = CreateLabel(instance);
			if (label == null)
			{
				return;
			}
			_labels[creature] = label;
		}

		label.Text = text;
		label.AddThemeColorOverride("font_color", color);
		label.Visible = true;

		NCreature? creatureNode = instance.GetCreatureNode(creature);
		if (creatureNode != null)
		{
			label.GlobalPosition = new Vector2(creatureNode.GlobalPosition.X + 10f, creatureNode.GlobalPosition.Y - 320f);
		}
	}

	public static void Hide()
	{
		foreach (Label label in _labels.Values)
		{
			if (GodotObject.IsInstanceValid(label))
			{
				label.QueueFree();
			}
		}

		_labels.Clear();
		_hasSnapshot = false;
		_lastSnapshotHash = 0;
	}

	private static void RemoveLabel(Creature creature)
	{
		if (_labels.TryGetValue(creature, out Label? label))
		{
			if (GodotObject.IsInstanceValid(label))
			{
				label.QueueFree();
			}
			_labels.Remove(creature);
		}
	}

	private static Label? CreateLabel(Node parent)
	{
		Label label = new Label();
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.ZIndex = 100;
		if (_cachedFont == null)
		{
			_cachedFont = FindGameFont(parent);
		}
		if (_cachedFont != null)
		{
			label.AddThemeFontOverride("font", _cachedFont);
		}
		label.AddThemeColorOverride("font_color", _incomingColor);
		label.AddThemeFontSizeOverride("font_size", 22);
		label.AddThemeConstantOverride("outline_size", 5);
		label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
		parent.AddChild(label, forceReadableName: false, Node.InternalMode.Disabled);
		return label;
	}

	private static Font? FindGameFont(Node root)
	{
		try
		{
			foreach (Node child in root.GetChildren())
			{
				if (child is not NCreature creatureNode)
				{
					continue;
				}

				Control? intentContainer = creatureNode.IntentContainer;
				if (intentContainer == null)
				{
					continue;
				}

				foreach (Node intentChild in intentContainer.GetChildren())
				{
					if (intentChild is not NIntent)
					{
						continue;
					}

					Control? valueNode = intentChild.GetNodeOrNull<Control>("%Value");
					if (valueNode is RichTextLabel richTextLabel)
					{
						Font? font = richTextLabel.GetThemeFont("normal_font");
						if (font != null)
						{
							return font;
						}
					}
					else if (valueNode is Label label)
					{
						Font? font = label.GetThemeFont("font");
						if (font != null)
						{
							return font;
						}
					}
				}
			}

			return FindFontRecursive(root, 3);
		}
		catch (Exception ex)
		{
			ModLog.Error("FindGameFont", ex);
			return null;
		}
	}

	private static Font? FindFontRecursive(Node node, int depth)
	{
		if (depth <= 0)
		{
			return null;
		}

		foreach (Node child in node.GetChildren())
		{
			if (child is Label label)
			{
				Font? font = label.GetThemeFont("font");
				if (font != null)
				{
					return font;
				}
			}

			Font? nestedFont = FindFontRecursive(child, depth - 1);
			if (nestedFont != null)
			{
				return nestedFont;
			}
		}

		return null;
	}
}