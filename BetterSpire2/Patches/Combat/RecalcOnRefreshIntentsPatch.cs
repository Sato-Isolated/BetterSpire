using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace BetterSpire2.Patches.Combat;

[HarmonyPatch(typeof(NCreature), "RefreshIntents")]
public class RecalcOnRefreshIntentsPatch
{
	private static void Postfix()
	{
		InstantSpeedHelper.OnCombatStart();
		DamageTracker.Recalculate();
	}
}
