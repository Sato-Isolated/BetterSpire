#nullable enable
using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace BetterSpire2.UI;

public static class ClockDisplay
{
	private static Label? _label;

	private static CanvasLayer? _layer;

	private static ulong _lastRefreshAt;

	private static bool _isDragging;

	private static Vector2 _dragOffset;

	public static void Toggle(bool on)
	{
		if (on)
		{
			EnsureCreated();
		}
		else
		{
			Remove();
		}
	}

	public static void Update()
	{
		if (!ModSettings.ShowClock)
		{
			Remove();
			return;
		}

		ulong ticksMsec = Time.GetTicksMsec();
		if (ticksMsec - _lastRefreshAt < 1000)
		{
			return;
		}

		_lastRefreshAt = ticksMsec;
		EnsureCreated();
		RefreshIfVisible();
	}

	public static void HandleInput(InputEvent @event)
	{
		if (!ModSettings.ShowClock || !UiHelpers.IsValid(_label))
		{
			return;
		}

		if (@event is InputEventMouseButton inputEventMouseButton && inputEventMouseButton.ButtonIndex == MouseButton.Left)
		{
			HandleMouseButton(inputEventMouseButton);
		}
		else if (@event is InputEventMouseMotion inputEventMouseMotion && _isDragging)
		{
			_label!.Position = inputEventMouseMotion.Position - _dragOffset;
		}
	}

	public static void RefreshIfVisible()
	{
		if (UiHelpers.IsValid(_label))
		{
			_label!.Text = GetCurrentTimeText();
		}
	}

	private static void EnsureCreated()
	{
		if (UiHelpers.IsValid(_label))
		{
			return;
		}

		NGame? instance = NGame.Instance;
		if (instance == null)
		{
			return;
		}

		_layer = UiHelpers.CreateCanvasLayer(100);
		_label = UiHelpers.CreateLabel(GetCurrentTimeText(), new Color(0.7f, 0.7f, 0.7f, 0.6f), 14, HorizontalAlignment.Right);
		_label.Size = new Vector2(190f, 20f);
		_label.Position = GetInitialPosition(instance);
		_layer.AddChild(_label, forceReadableName: false, Node.InternalMode.Disabled);
		instance.AddChild(_layer, forceReadableName: false, Node.InternalMode.Disabled);
	}

	private static void Remove()
	{
		PersistPosition();

		if (UiHelpers.IsValid(_layer))
		{
			_layer!.QueueFree();
		}

		_layer = null;
		_label = null;
		_isDragging = false;
	}

	private static void HandleMouseButton(InputEventMouseButton inputEvent)
	{
		Rect2 clockRect = new Rect2(_label!.GlobalPosition, _label.Size);
		Vector2 mousePosition = inputEvent.Position;

		if (inputEvent.Pressed)
		{
			if (clockRect.HasPoint(mousePosition))
			{
				_isDragging = true;
				_dragOffset = mousePosition - _label.Position;
			}
			return;
		}

		if (_isDragging)
		{
			_isDragging = false;
			PersistPosition();
		}
	}

	private static void PersistPosition()
	{
		if (!UiHelpers.IsValid(_label))
		{
			return;
		}

		ModSettings.ClockPosition = _label!.Position;
		ModSettings.Save();
	}

	private static Vector2 GetInitialPosition(NGame game)
	{
		Vector2? savedPosition = ModSettings.ClockPosition;
		if (savedPosition.HasValue)
		{
			return savedPosition.Value;
		}

		Vector2 viewportSize = game.GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920f, 1080f);
		return new Vector2(viewportSize.X - 200f, 148f);
	}

	private static string GetCurrentTimeText()
	{
		return ModSettings.Clock24Hour ? DateTime.Now.ToString("HH:mm") : DateTime.Now.ToString("h:mm tt");
	}
}
