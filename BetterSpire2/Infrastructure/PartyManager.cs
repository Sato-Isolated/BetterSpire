#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace BetterSpire2.Infrastructure;

public static class PartyManager
{
	private static readonly HashSet<ulong> _mutedDrawings = new HashSet<ulong>();

	public static NMapDrawings? MapDrawings;

	private static readonly MethodInfo _getDrawingState = AccessTools.Method(typeof(NMapDrawings), "GetDrawingStateForPlayer");

	private static readonly MethodInfo _clearForPlayer = AccessTools.Method(typeof(NMapDrawings), "ClearAllLinesForPlayer");

	private static readonly MethodInfo _clearAll = AccessTools.Method(typeof(NMapDrawings), "ClearAllLines");

	public static bool IsDrawingMuted(ulong netId)
	{
		return _mutedDrawings.Contains(netId);
	}

	public static void ToggleDrawingMute(ulong netId)
	{
		if (!_mutedDrawings.Remove(netId))
		{
			_mutedDrawings.Add(netId);
		}
	}

	public static void ClearDrawingsForPlayer(ulong netId)
	{
		try
		{
			if (MapDrawings == null || _getDrawingState == null || _clearForPlayer == null)
			{
				return;
			}

			object? drawingState = _getDrawingState.Invoke(MapDrawings, new object[] { netId });
			if (drawingState != null)
			{
				_clearForPlayer.Invoke(MapDrawings, new object[] { drawingState });
			}
		}
		catch (Exception ex)
		{
			ModLog.Error("PartyManager.ClearDrawingsForPlayer", ex);
		}
	}

	public static void ClearAllDrawings()
	{
		try
		{
			if (MapDrawings == null || _clearAll == null)
			{
				return;
			}

			_clearAll.Invoke(MapDrawings, null);
		}
		catch (Exception ex)
		{
			ModLog.Error("PartyManager.ClearAllDrawings", ex);
		}
	}

	public static void ClearMutes()
	{
		_mutedDrawings.Clear();
	}
}
