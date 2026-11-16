#nullable enable
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using System;
using System.Runtime.InteropServices;

namespace BetterSpire2.Core;

[ModInitializer("Init")]
public class ModEntry
{
    private const string HarmonyId = "com.jdr.betterspire2lite";

    private static bool _initialized;

    private static readonly Type[] _combatPatches = new Type[]
    {
        typeof(CombatManager_Reset_Patch),
        typeof(CombatManager_EndCombatInternal_Patch),
        typeof(CombatManager_LoseCombat_Patch),
        typeof(Creature_LoseBlockInternal_Patch),
        typeof(Creature_ClearBlock_Patch),
        typeof(Journal_BlockClearDecision_Patch),
        typeof(PlayerCombatState_GainEnergy_Patch),
        typeof(Journal_HistoryClear_Patch),
        typeof(Journal_RunCleanup_Patch),
        typeof(Journal_Healing_Patch)
    };

    private static readonly Type[] _poisonPatches = new Type[4]
    {
        typeof(Journal_DamageCommandScope_Patch),
        typeof(Journal_PoisonHistoryTag_Patch),
        typeof(Journal_PoisonRemoved_Patch),
        typeof(Journal_PoisonDamageOrigin_Patch)
    };

    private static readonly Type[] _uiPatches = new Type[5]
    {
        typeof(NIntent_UpdateVisuals_Patch),
        typeof(NGame_LaunchMainMenu_Patch),
        typeof(NGame_Input_Patch),
        typeof(GameReadyPatch),
        typeof(GameExitPatch)
    };

    private static readonly Type[] _effectPatches = new Type[]
    {
        typeof(Journal_NativeDamageScope_Patch),
        typeof(Journal_NativeDamageResults_Patch),
        typeof(Journal_DoomBatchScope_Patch),
        typeof(Journal_LightningOrbDamageOrigin_Patch),
        typeof(Journal_DarkOrbDamageOrigin_Patch),
        typeof(Journal_ThornsDamageOrigin_Patch),
        typeof(Journal_FlameBarrierDamageOrigin_Patch),
        typeof(Journal_ExplosiveArrowsDamageOrigin_Patch),
        typeof(Journal_ViolentChordDamageOrigin_Patch),
        typeof(Journal_EffectCommandScope_Patch),
        typeof(Journal_EffectHpResult_Patch),
        typeof(Journal_StrangleDamageOrigin_Patch),
        typeof(Journal_DoomKillOrigin_Patch),
        typeof(Journal_JudgementDamageOrigin_Patch),
        typeof(Journal_JudgementCommandScope_Patch),
        typeof(Journal_JudgementHpResult_Patch)
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
        int skipped = 0;
        int failed = 0;
        PatchGroup(harmony, "Combat", _combatPatches, ref succeeded, ref skipped, ref failed);
        PatchGroup(harmony, "Poison", _poisonPatches, ref succeeded, ref skipped, ref failed);
        PatchGroup(harmony, "Effect damage", _effectPatches, ref succeeded, ref skipped, ref failed);
        PatchGroup(harmony, "UI", _uiPatches, ref succeeded, ref skipped, ref failed);
        PatchGroup(harmony, "Map", _mapPatches, ref succeeded, ref skipped, ref failed);
        ModLog.Info($"Harmony patching complete: {succeeded} succeeded, {skipped} skipped, {failed} failed");
        ModLog.Info("ModEntry.Init() complete — BetterSpire 3.6.5 for game v0.113");
        ModRuntime.EnsureStarted();
        if (ModSettings.ShowClock)
        {
            ClockDisplay.Toggle(on: true);
        }
    }

    private static void PatchGroup(Harmony harmony, string groupName, Type[] patchClasses, ref int succeeded, ref int skipped, ref int failed)
    {
        ModLog.Info($"Patching {groupName}...");
        foreach (Type patchClass in patchClasses)
        {
            PatchClass(harmony, patchClass, ref succeeded, ref skipped, ref failed);
        }
    }

    private static void PatchClass(Harmony harmony, Type patchClass, ref int succeeded, ref int skipped, ref int failed)
    {
        try
        {
            var patched = harmony.CreateClassProcessor(patchClass).Patch();
            if (patched == null || patched.Count == 0)
            {
                ReportMissingObservation(patchClass);
                ModLog.Info("  Skipped: " + patchClass.Name + " (no patches applied)");
                skipped++;
                return;
            }
            ModLog.Info($"  Patched: {patchClass.Name} ({patched.Count} methods)");
            succeeded++;
        }
        catch (Exception ex)
        {
            ReportMissingObservation(patchClass);
            ModLog.Error("Patch " + patchClass.Name, ex);
            failed++;
        }
    }
    private static void ReportMissingObservation(Type patchClass)
    {
        if (patchClass.Name.StartsWith("Journal_", StringComparison.Ordinal))
            BetterSpire2.Journal.Game.DamageObservationCoverage.Require("patch:" + patchClass.Name, false);
    }
}
