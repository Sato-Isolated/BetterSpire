#nullable enable
using System;
using System.Runtime.InteropServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace BetterSpire2.Core;

[ModInitializer("Init")]
public class ModEntry
{
	private const string HarmonyId = "com.jdr.betterspire2lite";

	private static bool _initialized;

	private static readonly Type[] _combatPatches = new Type[11]
	{
		typeof(NCreature_RefreshIntents_Patch),
		typeof(CombatManager_SetReadyToEndTurn_Patch),
		typeof(NIntent_Process_Patch),
		typeof(CombatManager_SetUpCombat_Patch),
		typeof(CombatManager_Reset_Patch),
		typeof(CombatManager_EndCombatInternal_Patch),
		typeof(CombatManager_LoseCombat_Patch),
		typeof(Creature_DamageBlockInternal_Patch),
		typeof(Creature_LoseBlockInternal_Patch),
		typeof(Creature_ClearBlock_Patch),
		typeof(PlayerCombatState_GainEnergy_Patch)
	};

	private static readonly Type[] _uiPatches = new Type[3]
	{
		typeof(NIntent_UpdateVisuals_Patch),
		typeof(NGame_LaunchMainMenu_Patch),
		typeof(NGame_Input_Patch)
	};

	private static readonly Type[] _mapPatches = new Type[2]
	{
		typeof(NMapDrawings_HandleDrawingMessage_Patch),
		typeof(NMapDrawings_HandleClearMapDrawingsMessage_Patch)
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
		PatchGroup(harmony, "Map", _mapPatches, ref succeeded, ref failed);
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
}
