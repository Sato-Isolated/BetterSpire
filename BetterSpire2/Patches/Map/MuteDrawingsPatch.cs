using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace BetterSpire2.Patches.Map;

public class MuteDrawingsPatch
{
	public static bool Prefix(NMapDrawings __instance, ulong senderId)
	{
		PartyManager.MapDrawings = __instance;
		return !PartyManager.IsDrawingMuted(senderId);
	}
}
