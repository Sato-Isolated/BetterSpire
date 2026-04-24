using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;

namespace BetterSpire2.Patches.Combat;

[HarmonyPatch(typeof(CombatManager), "LoseCombat")]
public class HideOnLosePatch
{
	private static void Prefix()
	{
		DamageTracker.Hide();
		DeckTracker.Hide();
		TurnSummaryTracker.Hide();
		InstantSpeedHelper.OnCombatEnd();
	}
}
