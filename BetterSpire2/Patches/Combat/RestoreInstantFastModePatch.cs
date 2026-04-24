using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;

namespace BetterSpire2.Patches.Combat;

[HarmonyPatch(typeof(NGame), "LaunchMainMenu")]
public class RestoreInstantFastModePatch
{
	private static void Postfix()
	{
		if (ModSettings.InstantFastMode)
		{
			ModLog.Info("Instant Fast Mode enabled (combat-only)");
		}
	}
}
