#nullable enable
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace BetterSpire2.Core;

[ModInitializer("Init")]
public class ModEntry
{
	private const string HarmonyId = "com.jdr.betterspire2lite";

	private static bool _initialized;

	private static readonly Type[] _combatPatches = new Type[10]
	{
		typeof(RecalcOnRefreshIntentsPatch),
		typeof(RecalcOnEndTurnPatch),
		typeof(RecalcPeriodicPatch),
		typeof(CombatSetUpPatch),
		typeof(HideOnResetPatch),
		typeof(HideOnWinPatch),
		typeof(HideOnLosePatch),
		typeof(TrackBlockConsumedPatch),
		typeof(TrackBlockLostPatch),
		typeof(TrackBlockClearedPatch)
	};

	private static readonly Type[] _uiPatches = new Type[4]
	{
		typeof(IntentLabelPatch),
		typeof(SkipSplashPatch),
		typeof(RestoreInstantFastModePatch),
		typeof(InputPatch)
	};

	public static void Init()
	{
		if (_initialized)
		{
			return;
		}
		_initialized = true;
		ModLog.Init();
		ModLog.Info("ModEntry.Init() starting");
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			try
			{
				ModLog.Info("Linux detected — loading libgcc_s for Harmony compatibility");
				nint num = LinuxNative.dlopen("libgcc_s.so.1", 258);
				if (num == IntPtr.Zero)
				{
					ModLog.Info("  dlopen failed: " + Marshal.PtrToStringAnsi(LinuxNative.dlerror()));
				}
				else
				{
					ModLog.Info("  libgcc_s loaded successfully");
				}
			}
			catch (DllNotFoundException)
			{
				ModLog.Info("  libdl.so.2 not found — skipping (likely Android)");
			}
		}
		ModSettings.Load();
		ModLog.Info("Settings loaded");
		Harmony harmony = new Harmony(HarmonyId);
		int succeeded = 0;
		int failed = 0;
		PatchGroup(harmony, "Combat", _combatPatches, ref succeeded, ref failed);
		PatchGroup(harmony, "UI", _uiPatches, ref succeeded, ref failed);
		PatchDrawingMethods(harmony, ref succeeded, ref failed);
		ModLog.Info($"Harmony patching complete: {succeeded} succeeded, {failed} failed");
		ModLog.Info("ModEntry.Init() complete");
		if (ModSettings.ShowClock)
		{
			ClockDisplay.Toggle(on: true);
		}
	}

	private static void PatchGroup(Harmony harmony, string groupName, Type[] patchClasses, ref int succeeded, ref int failed)
	{
		ModLog.Info($"Patching {groupName}...");
		foreach (Type patchClass in patchClasses)
		{
			PatchClass(harmony, patchClass, ref succeeded, ref failed);
		}
	}

	private static void PatchClass(Harmony harmony, Type patchClass, ref int succeeded, ref int failed)
	{
		try
		{
			harmony.CreateClassProcessor(patchClass).Patch();
			ModLog.Info("  Patched: " + patchClass.Name);
			succeeded++;
		}
		catch (Exception ex)
		{
			ModLog.Error("Patch " + patchClass.Name, ex);
			failed++;
		}
	}

	private static void PatchDrawingMethods(Harmony harmony, ref int succeeded, ref int failed)
	{
		ModLog.Info("Patching Map...");
		PatchMethod(harmony, "HandleDrawingMessage", typeof(MuteDrawingsPatch), "MuteDrawingsPatch", ref succeeded, ref failed);
		PatchMethod(harmony, "HandleClearMapDrawingsMessage", typeof(MuteClearDrawingsPatch), "MuteClearDrawingsPatch", ref succeeded, ref failed);
	}

	private static void PatchMethod(Harmony harmony, string methodName, Type patchType, string patchName, ref int succeeded, ref int failed)
	{
		try
		{
			MethodInfo? targetMethod = AccessTools.Method(typeof(NMapDrawings), methodName);
			if (targetMethod == null)
			{
				ModLog.Info("  Skipped: " + patchName + " — method not found");
				failed++;
				return;
			}

			HarmonyMethod prefix = new HarmonyMethod(patchType, "Prefix");
			harmony.Patch(targetMethod, prefix);
			ModLog.Info("  Patched: " + patchName + " (manual)");
			succeeded++;
		}
		catch (Exception ex)
		{
			ModLog.Error("Patch " + patchName + " (manual)", ex);
			failed++;
		}
	}
}
