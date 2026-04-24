#nullable enable
using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace BetterSpire2.UI;

public static partial class SettingsMenu
{
	private static readonly Color PanelBackgroundColor = new(0.1f, 0.1f, 0.15f, 0.95f);
	private static readonly Color PanelBorderColor = new(0.8f, 0.6f, 0.2f);
	private static readonly Color AccentColor = new(0.9f, 0.7f, 0.2f);
	private static readonly Color SecondaryTextColor = new(0.9f, 0.9f, 0.9f);
	private static readonly Color MutedTextColor = new(0.5f, 0.5f, 0.5f);

	private static PanelContainer? _panel;
	private static CanvasLayer? _canvasLayer;
	private static bool _isVisible;
	private static bool _isDragging;
	private static Vector2 _dragOffset;

	public static void Toggle()
	{
		if (_isVisible)
		{
			Hide();
		}
		else
		{
			Show();
		}
	}

	public static void HandleMouseInput(InputEvent @event)
	{
		if (!_isVisible || !UiHelpers.IsValid(_panel))
		{
			return;
		}

		if (@event is InputEventMouseButton inputEventMouseButton && inputEventMouseButton.ButtonIndex == MouseButton.Left)
		{
			HandleMouseButton(inputEventMouseButton);
		}
		else if (@event is InputEventMouseMotion inputEventMouseMotion && _isDragging)
		{
			_panel!.Position = inputEventMouseMotion.Position - _dragOffset;
		}
	}

	public static bool IsPointInPanel(Vector2 point)
	{
		return _isVisible && UiHelpers.IsValid(_panel) && _panel!.GetGlobalRect().HasPoint(point);
	}

	public static void Hide()
	{
		PersistPosition();

		if (UiHelpers.IsValid(_canvasLayer))
		{
			_canvasLayer!.QueueFree();
		}

		_canvasLayer = null;
		_panel = null;
		_isVisible = false;
		_isDragging = false;
	}

	private static void UpdateSetting(Action applyChange, Action? afterSave = null)
	{
		applyChange();
		ModSettings.Save();
		afterSave?.Invoke();
	}

	private static void RefreshIntents()
	{
		try
		{
			NCombatRoom? instance = NCombatRoom.Instance;
			if (instance == null)
			{
				return;
			}

			foreach (Node child in instance.GetChildren())
			{
				if (child is NCreature nCreature)
				{
					nCreature.RefreshIntents();
				}
			}
		}
		catch (Exception ex)
		{
			ModLog.Error("SettingsMenu.RefreshIntents", ex);
		}
	}
}