#nullable enable
using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace BetterSpire2.Trackers;

public static partial class TurnSummaryTracker
{
	private static void HandleMouseButton(InputEventMouseButton inputEvent)
	{
		Vector2 mousePosition = inputEvent.Position;
		Rect2 panelRect = _panel!.GetGlobalRect();
		Rect2 resizeRect = GetResizeHandleRect(panelRect);

		if (inputEvent.Pressed)
		{
			if ((!panelRect.HasPoint(mousePosition) && !resizeRect.HasPoint(mousePosition)) || IsPointInInteractiveControl(mousePosition))
			{
				return;
			}

			ResizeEdge resizeEdge = GetResizeEdge(mousePosition, panelRect);
			if (resizeEdge != ResizeEdge.None)
			{
				_resizing = true;
				_resizeEdge = resizeEdge;
				return;
			}

			_dragging = true;
			_dragOffset = mousePosition - _panel.Position;
			return;
		}

		if (_dragging || _resizing)
		{
			PersistLayout();
		}
		_dragging = false;
		_resizing = false;
	}

	private static bool IsPointInInteractiveControl(Vector2 point)
	{
		PruneInteractiveControls();

		foreach (Control control in _interactiveControls)
		{
			if (IsPointInControl(control, point))
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsPointInControl(Control? control, Vector2 point)
	{
		return UiHelpers.IsValid(control) && control!.GetGlobalRect().HasPoint(point);
	}

	private static ResizeEdge GetResizeEdge(Vector2 mousePos, Rect2 rect)
	{
		ResizeEdge edge = ResizeEdge.None;
		if (mousePos.X >= rect.End.X - ResizeMargin) edge |= ResizeEdge.Right;
		if (mousePos.Y >= rect.End.Y - ResizeMargin) edge |= ResizeEdge.Bottom;
		return edge;
	}

	private static void ResizePanel(Vector2 mousePosition)
	{
		Vector2 position = _panel!.Position;
		Vector2 size = _panel.Size;
		if (_resizeEdge.HasFlag(ResizeEdge.Right))
		{
			size.X = Math.Max(MinPanelWidth, mousePosition.X - position.X);
		}
		if (_resizeEdge.HasFlag(ResizeEdge.Bottom))
		{
			size.Y = Math.Max(MinPanelHeight, mousePosition.Y - position.Y);
		}
		size = ClampPanelSize(size);
		_panel.CustomMinimumSize = size;
		_panel.Size = size;
		UpdateGripPosition();
		Refresh();
	}

	private static void UpdateResizeCursor(Vector2 mousePos, Rect2 rect)
	{
		if (!GetResizeHandleRect(rect).HasPoint(mousePos))
		{
			_panel!.MouseDefaultCursorShape = Control.CursorShape.Arrow;
			return;
		}

		ResizeEdge edge = GetResizeEdge(mousePos, rect);
		_panel!.MouseDefaultCursorShape = edge switch
		{
			ResizeEdge.Right | ResizeEdge.Bottom => Control.CursorShape.Fdiagsize,
			ResizeEdge.Right => Control.CursorShape.Hsize,
			ResizeEdge.Bottom => Control.CursorShape.Vsize,
			_ => Control.CursorShape.Arrow
		};
	}

	private static Rect2 GetResizeHandleRect(Rect2 rect)
	{
		return new Rect2(rect.Position, rect.Size + new Vector2(ResizeMargin, ResizeMargin));
	}

	private static void UpdateGripPosition()
	{
		if (!UiHelpers.IsValid(_gripLabel) || !UiHelpers.IsValid(_panel)) return;
		Vector2 pos = _panel!.Position;
		Vector2 sz = _panel.Size;
		_gripLabel!.Position = new Vector2(pos.X + sz.X - 16f, pos.Y + sz.Y - 18f);
	}

	private static void PrevRound()
	{
		if (_viewMode == HistoryViewMode.Combat) return;
		_selectedRound = Math.Max(1, GetSelectedRoundNumber() - 1);
		Refresh();
	}

	private static void NextRound()
	{
		if (_viewMode == HistoryViewMode.Combat) return;
		_selectedRound = Math.Min(Math.Max(1, _trackedRound), GetSelectedRoundNumber() + 1);
		Refresh();
	}

	private static void ToggleViewMode()
	{
		_viewMode = _viewMode == HistoryViewMode.Combat ? HistoryViewMode.Round : HistoryViewMode.Combat;
		if (_viewMode == HistoryViewMode.Round && _selectedRound <= 0)
		{
			_selectedRound = Math.Max(1, _trackedRound);
		}
		Refresh();
	}

	private static void ToggleCollapsed()
	{
		ModSettings.TurnSummaryCollapsed = !ModSettings.TurnSummaryCollapsed;
		ModSettings.Save();
		Refresh();
	}

	private static void PersistLayout()
	{
		if (!UiHelpers.IsValid(_panel)) return;
		ModSettings.TurnSummaryPosition = _panel!.Position;
		ModSettings.TurnSummarySize = _panel.Size;
		ModSettings.Save();
	}

	private static void CleanupUi()
	{
		if (UiHelpers.IsValid(_canvasLayer))
		{
			_canvasLayer!.QueueFree();
		}

		_canvasLayer = null;
		_panel = null;
		_scrollContainer = null;
		_contentContainer = null;
		_titleLabel = null;
		_prevRoundButton = null;
		_scopeButton = null;
		_nextRoundButton = null;
		_collapseButton = null;
		_gripLabel = null;
		_interactiveControls.Clear();
	}

	private static void ClearContainer(Container container)
	{
		foreach (Node child in container.GetChildren())
		{
			child.QueueFree();
		}
	}
}