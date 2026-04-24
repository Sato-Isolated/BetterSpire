using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Flavor;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace BetterSpire2.Patches.Map;

/// <summary>
/// Prevents muted teammates' map drawings from appearing on the local client.
/// </summary>
[HarmonyPatch(typeof(NMapDrawings), "HandleDrawingMessage", new[] { typeof(MapDrawingMessage), typeof(ulong) })]
internal static class NMapDrawings_HandleDrawingMessage_Patch
{
	[HarmonyPrepare]
	private static bool Prepare(MethodBase original)
	{
		if (original is not null)
		{
			return true;
		}

		ModLog.Info($"Target method not found for {nameof(NMapDrawings_HandleDrawingMessage_Patch)} - patch skipped.");
		return false;
	}

	[HarmonyPrefix]
	private static bool Prefix(NMapDrawings __instance, ulong senderId)
	{
		PartyManager.MapDrawings = __instance;
		return !PartyManager.IsDrawingMuted(senderId);
	}
}
