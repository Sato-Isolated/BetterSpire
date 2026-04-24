using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;

namespace BetterSpire2.Patches.UI;

/// <summary>
/// Lets players skip the logo sequence and restores BetterSpire menu-side state when returning to the main menu.
/// </summary>
[HarmonyPatch(typeof(NGame), "LaunchMainMenu")]
internal static class NGame_LaunchMainMenu_Patch
{
	

	[HarmonyPrefix]
	private static void Prefix(ref bool skipLogo)
	{
		if (ModSettings.SkipSplash)
		{
			skipLogo = true;
		}
		PartyManager.ClearMutes();
	}

	[HarmonyPostfix]
	private static void Postfix()
	{
		if (ModSettings.InstantFastMode)
		{
			ModLog.Info("Instant Fast Mode enabled (combat-only)");
		}
	}
}
