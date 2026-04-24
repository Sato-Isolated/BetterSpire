using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Flavor;

namespace BetterSpire2.Patches.Map;

/// <summary>
/// Prevents muted teammates from wiping local map drawings through remote clear messages.
/// </summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapDrawings), "HandleClearMapDrawingsMessage", new[] { typeof(ClearMapDrawingsMessage), typeof(ulong) })]
internal static class NMapDrawings_HandleClearMapDrawingsMessage_Patch
{
	[HarmonyPrepare]
	private static bool Prepare(MethodBase original)
	{
		if (original is not null)
		{
			return true;
		}

		ModLog.Info($"Target method not found for {nameof(NMapDrawings_HandleClearMapDrawingsMessage_Patch)} - patch skipped.");
		return false;
	}

	[HarmonyPrefix]
	private static bool Prefix(ulong senderId)
	{
		return !PartyManager.IsDrawingMuted(senderId);
	}
}
