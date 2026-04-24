using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;

namespace BetterSpire2.Patches.UI;

[HarmonyPatch(typeof(NGame), "LaunchMainMenu")]
public class SkipSplashPatch
{
	private static void Prefix(ref bool skipLogo)
	{
		if (ModSettings.SkipSplash)
		{
			skipLogo = true;
		}
		PartyManager.ClearMutes();
	}
}
