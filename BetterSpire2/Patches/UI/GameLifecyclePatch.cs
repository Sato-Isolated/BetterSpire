using System;
using BetterSpire2.Runtime;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;

namespace BetterSpire2.Patches.UI;

[HarmonyPatch(typeof(NGame), nameof(NGame._Ready))]
internal static class GameReadyPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try { ModRuntime.EnsureStarted(); }
        catch (Exception ex) { ModLog.Error("Runtime.Start", ex); }
    }
}

[HarmonyPatch(typeof(NGame), nameof(NGame._ExitTree))]
internal static class GameExitPatch
{
    [HarmonyPrefix]
    private static void Prefix() => ModRuntime.Stop();
}
