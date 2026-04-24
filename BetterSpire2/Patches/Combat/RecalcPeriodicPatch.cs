using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace BetterSpire2.Patches.Combat;

[HarmonyPatch(typeof(NIntent), "_Process")]
public class RecalcPeriodicPatch
{
	private static ulong _lastRecalcMs;

	private static void Postfix()
	{
		ulong ticksMsec = Time.GetTicksMsec();
		if (ticksMsec - _lastRecalcMs >= 250)
		{
			_lastRecalcMs = ticksMsec;
			DamageTracker.Recalculate();
		}
	}
}
