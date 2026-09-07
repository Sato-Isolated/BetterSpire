#nullable enable
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace BetterSpire2.Infrastructure;

public static class PartyManager
{
    private static readonly HashSet<ulong> _mutedDrawings = new HashSet<ulong>();

    public static NMapDrawings? MapDrawings;

    private static readonly MethodInfo _getDrawingState = AccessTools.Method(typeof(NMapDrawings), "GetDrawingStateForPlayer");

    private static readonly MethodInfo _clearForPlayer = AccessTools.Method(typeof(NMapDrawings), "ClearAllLinesForPlayer");

    internal static long Revision { get; private set; }
    private static NMapDrawings? CurrentDrawings
    {
        get
        {
            var screen = NMapScreen.Instance;
            var current = UiHelpers.IsValid(screen) ? screen!.Drawings : null;
            return UiHelpers.IsValid(current) ? current : UiHelpers.IsValid(MapDrawings) ? MapDrawings : null;
        }
    }
    internal static bool CanClearDrawings => CurrentDrawings != null;

    public static bool IsDrawingMuted(ulong netId)
    {
        return _mutedDrawings.Contains(netId);
    }

    public static void ToggleDrawingMute(ulong netId)
    {
        Revision++;
        if (!_mutedDrawings.Remove(netId))
        {
            _mutedDrawings.Add(netId);
        }
    }

    public static void ClearDrawingsForPlayer(ulong netId)
    {
        try
        {
            var drawings = CurrentDrawings;
            if (drawings == null || _getDrawingState == null || _clearForPlayer == null)
            {
                return;
            }

            object? drawingState = _getDrawingState.Invoke(drawings, new object[] { netId });
            if (drawingState != null)
            {
                _clearForPlayer.Invoke(drawings, new object[] { drawingState });
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
            CurrentDrawings?.ClearAllLines();
        }
        catch (Exception ex)
        {
            ModLog.Error("PartyManager.ClearAllDrawings", ex);
        }
    }

    public static void ClearMutes()
    {
        _mutedDrawings.Clear();
        Revision++;
    }
}
