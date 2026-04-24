namespace BetterSpire2.Patches.Map;

public class MuteClearDrawingsPatch
{
	public static bool Prefix(ulong senderId)
	{
		return !PartyManager.IsDrawingMuted(senderId);
	}
}
