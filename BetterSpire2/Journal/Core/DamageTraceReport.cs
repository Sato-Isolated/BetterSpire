#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BetterSpire2.Journal.Core;

internal static class DamageTraceReport
{
    internal static IEnumerable<JournalRow> Rows(RunJournal run, CombatRecord? combat, JournalSelection selection, bool french)
    {
        string T(string en, string fr) => french ? fr : en;
        string N(long n) => n.ToString("N0", CultureInfo.GetCultureInfo(french ? "fr-FR" : "en-US"));
        string PlayerName(string id) => run.Players.TryGetValue(id, out var p) ? p.Name : T("Unattributed", "Sans attribution");
        if (combat == null) yield break;
        bool found = false;
        foreach (var e in combat.DamageTrace)
        {
            if (selection.Scope == JournalScope.Round && e.Round != selection.Round) continue;
            if (selection.PlayerId != null && e.TargetPlayerId != selection.PlayerId && !e.Shares.Any(s => s.PlayerId == selection.PlayerId)) continue;
            found = true;
            string source = e.Source.Length > 0 ? e.Source : T("Unspecified effect", "Effet non précisé");
            string phase = e.Phase switch
            {
                JournalPhase.Setup => T("Setup", "Préparation"), JournalPhase.Player => T("Player phase", "Phase joueur"),
                JournalPhase.Enemy => T("Enemy phase", "Phase ennemie"), _ => T("Unknown phase", "Phase inconnue")
            };
            string detail = $"#{e.Sequence} · {T("Round", "Tour")} {e.Round} · {phase}\n" +
                (e.Dealer.Length > 0 ? e.Dealer : T("Dealer unavailable", "Auteur indisponible")) + " → " + e.Target +
                $" [{e.TargetId}]\n{source}\n" + N(e.Hp) + T(" HP removed · ", " PV retirés · ") +
                N(e.Blocked) + T(" blocked · ", " bloqués · ") + N(e.Overkill) + T(" overkill (excluded)", " excédent (exclu)");
            detail += "\n" + (e.Attribution switch
            {
                DamageAttributionKind.NativeDealer => T("Player supplied by native damage history.", "Joueur fourni par l’historique natif."),
                DamageAttributionKind.PetOwner => T("Pet damage credited to its owner once.", "Dégâts du familier crédités une fois à son propriétaire."),
                DamageAttributionKind.CardOwner => T("Player identified from the source card, not a native dealer.", "Joueur identifié par la carte source, pas par l’auteur natif."),
                DamageAttributionKind.PoisonConvention => T("Proportional poison-stack convention, not unique native ownership.", "Convention proportionnelle des stacks de poison, pas une propriété native unique."),
                DamageAttributionKind.Unattributed => T("No reliable player attribution.", "Aucune attribution fiable à un joueur."),
                _ => T("Not part of outgoing enemy damage totals.", "Hors des totaux de dégâts infligés aux ennemis.")
            });
            foreach (var share in e.Shares)
                detail += $"\n{PlayerName(share.PlayerId)} : {N(share.Hp)} / {N(share.Blocked)} / {N(share.Overkill)}";
            if (e.Fatal) detail += "\n" + T("Native result marked lethal; later revival/death hooks are separate.", "Résultat natif marqué mortel ; résurrections et effets ultérieurs sont distincts.");
            yield return new JournalRow($"#{e.Sequence} · {source} → {e.Target}",
                N(e.Hp) + T(" HP · ", " PV · ") + N(e.Blocked) + T(" block · ", " bloc · ") + N(e.Overkill) + T(" extra", " exc."), Detail: detail);
        }
        if (!found) yield return new JournalRow(T("No retained damage for this selection", "Aucun dégât conservé pour cette sélection"), "");
    }
}
