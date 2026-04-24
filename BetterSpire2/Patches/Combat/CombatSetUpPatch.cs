using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;

namespace BetterSpire2.Patches.Combat;

[HarmonyPatch(typeof(CombatManager), "SetUpCombat")]
public class CombatSetUpPatch
{
	private static void Postfix()
	{
		try
		{
			DeckTracker.OnCombatSetUp();
			TurnSummaryTracker.OnCombatSetUp();
		}
		catch (Exception ex)
		{
			ModLog.Error("CombatSetUpPatch", ex);
		}
	}
}
