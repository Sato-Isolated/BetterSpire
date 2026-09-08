#nullable enable
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace BetterSpire2.Patches.UI;

/// <summary>Display-only multi-hit totals. Never invalidates the survival forecast.</summary>
[HarmonyPatch(typeof(NIntent), "UpdateVisuals")]
internal static class NIntent_UpdateVisuals_Patch
{
    private sealed class LabelCache
    {
        internal AbstractIntent? Intent;
        internal string Input = "", Output = "";
        internal int Repeats;
    }
    private static readonly ConditionalWeakTable<NIntent, LabelCache> Labels = new();
    private static ulong _lastError;

    [HarmonyPostfix]
    private static void Postfix(NIntent __instance, AbstractIntent ____intent, Creature ____owner,
        IEnumerable<Creature> ____targets, MegaRichTextLabel ____valueLabel)
    {
        if (!ModSettings.MultiHitTotals || ____intent is not AttackIntent attack ||
            ____targets == null || ____owner == null || ____valueLabel == null) return;
        try
        {
            using var measurement = PerformanceProbe.Measure(ProbeSection.IntentLabel);
            string text = ____valueLabel.Text?.Trim() ?? "";
            if (text.Length == 0 || text.Contains('(')) return;
            // Exact native types only: custom intent overrides keep their own GetTotalDamage semantics.
            if (attack.GetType() == typeof(SingleAttackIntent)) return;
            if (attack.GetType() == typeof(MultiAttackIntent))
            {
                int repeats = attack.Repeats;
                if (repeats <= 1) return;
                var cached = Labels.GetValue(__instance, static _ => new LabelCache());
                // Native UpdateVisuals has just produced the damage/repeat label for the current state.
                if (ReferenceEquals(cached.Intent, attack) && cached.Input == text && cached.Repeats == repeats)
                { if (cached.Output != text) ____valueLabel.SetTextAutoSize(cached.Output); return; }
                int damage = attack.GetSingleDamage(____targets, ____owner);
                // Verified native MultiAttackIntent.GetTotalDamage is singleDamage * Repeats.
                int total = checked(damage * repeats);
                string output = damage > 0 ? text + $" ({total})" : text;
                cached.Intent = attack; cached.Input = text; cached.Repeats = repeats; cached.Output = output;
                if (output != text) ____valueLabel.SetTextAutoSize(output);
                return;
            }
            int single = attack.GetSingleDamage(____targets, ____owner);
            int customTotal = attack.GetTotalDamage(____targets, ____owner);
            if (single > 0 && customTotal > single) ____valueLabel.SetTextAutoSize(text + $" ({customTotal})");
        }
        catch (Exception ex)
        {
            // A display-only enhancement must not break the game's native intent label or spam disk I/O.
            ulong now = Time.GetTicksMsec();
            if (_lastError == 0 || now - _lastError >= 10000)
            { _lastError = now; ModLog.Error(nameof(NIntent_UpdateVisuals_Patch), ex); }
        }
    }
}
