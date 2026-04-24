using Godot;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace BetterSpire2.Patches.Combat;

/// <summary>
/// Periodically repositions and refreshes combat overlays while intent nodes animate each frame.
/// </summary>
[HarmonyPatch(typeof(NIntent), nameof(NIntent._Process))]
internal static class NIntent_Process_Patch
{
	

	[HarmonyPostfix]
	private static void Postfix()
	{
		ulong ticksMsec = Time.GetTicksMsec();
		if (DamageTrackerRefreshThrottle.TryAcquireIntentProcessRefresh(ticksMsec, 250))
		{
			DamageTracker.Recalculate();
		}
	}
}
