#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BetterSpire2.Journal.Core;

public sealed record JournalRow(string Label, string Value, string? CombatKey = null, string? Detail = null);
public sealed class JournalReport
{
    public string Context { get; init; } = "";
    public string Player { get; init; } = "";
    public string Section { get; init; } = "";
    public string Note { get; init; } = "";
    public string[] HeroValues { get; init; } = new[] { "—", "—", "—", "—" };
    public IReadOnlyList<JournalRow> Rows { get; init; } = Array.Empty<JournalRow>();
    public int Page { get; init; }
    public int Pages { get; init; } = 1;
}

/// <summary>All strings and paging can be tested without loading Godot or sts2.dll.</summary>
public static class JournalReportBuilder
{
    public const int RowsPerPage = 6;
    public static JournalReport Build(JournalSession session, JournalSelection selection, bool sources, int page, bool french, bool runDetails = false, bool timeline = false)
    {
        string T(string en, string fr) => french ? fr : en;
        string N(long n) => n.ToString("N0", CultureInfo.GetCultureInfo(french ? "fr-FR" : "en-US"));
        var run = session.Run;
        selection.Sync(run);
        var combat = selection.SelectedCombat(run);
        if (run == null || run.Combats.Count == 0)
            return new JournalReport { Context = T("No combat recorded yet", "Aucun combat enregistré"),
                Note = T("Tracking runs while this window is closed.", "Le suivi continue lorsque ce journal est fermé.") };
        var stats = session.Query(selection.Scope, selection.CombatKey, selection.Round, selection.PlayerId);
        var totals = stats.Totals;
        string player = selection.PlayerId == null ? T("Team · combined totals", "Équipe · totaux cumulés") :
            run.Players.TryGetValue(selection.PlayerId, out var p) ? p.Name + (p.IsLocal ? T(" · You", " · Vous") : "") : "";
        int number = combat == null ? 0 : run.Combats.IndexOf(combat) + 1;
        string context = selection.Scope switch
        {
            JournalScope.Run => $"{run.Combats.Count} " + T("recorded combats", "combats enregistrés") + " · " + RunStatus(run.Status, french),
            JournalScope.Combat => T("Combat ", "Combat ") + number + $" · {T("Act", "Acte")} {combat?.Act} · {T("Floor", "Étage")} {combat?.Floor} · " + Outcome(combat?.Outcome ?? CombatOutcome.InProgress, french),
            _ => (selection.Round == 0 ? T("Setup", "Préparation") : T("Round ", "Tour ") + selection.Round) + T(" · Combat ", " · Combat ") + number +
                (selection.FollowLive && combat?.Outcome == CombatOutcome.InProgress ? T(" · Live", " · En cours") : "")
        };
        var rows = new List<JournalRow>();
        string section;
        if (timeline && selection.Scope != JournalScope.Run)
        {
            section = T("Damage timeline · hover for attribution", "Chronologie des dégâts · survol : attribution");
            rows.AddRange(DamageTraceReport.Rows(run, combat, selection, french));
        }
        else if (sources)
        {
            section = T("Cards & identified sources", "Cartes et sources identifiées");
            foreach (var source in stats.Sources.Values.OrderByDescending(s => s.Stats[Stat.DamageDealtHp])
                .ThenByDescending(s => s.Stats[Stat.BlockGained]).ThenBy(s => s.Name, StringComparer.Ordinal))
            {
                var values = new List<string>();
                if (source.Stats[Stat.CardsPlayed] > 0) values.Add("×" + N(source.Stats[Stat.CardsPlayed]));
                if (source.Stats[Stat.DamageDealtHp] > 0) values.Add(N(source.Stats[Stat.DamageDealtHp]) + T(" dealt", " infligés"));
                if (source.Stats[Stat.BlockGained] > 0) values.Add("+" + N(source.Stats[Stat.BlockGained]) + T(" block", " bloc"));
                if (source.Stats[Stat.HpLost] > 0) values.Add("−" + N(source.Stats[Stat.HpLost]) + T(" HP", " PV"));
                if (values.Count == 0) continue;
                string name = source.Id == "effect:unknown" ? T("Other effects · source unavailable", "Autres effets · source non fournie") :
                    source.Id == "other:overflow" ? T("Other sources (grouped)", "Autres sources (regroupées)") : source.Name;
                rows.Add(new JournalRow(name, string.Join(" · ", values)));
            }
            if (rows.Count == 0) rows.Add(new JournalRow(T("No attributed source", "Aucune source attribuée"), ""));
        }
        else if (selection.Scope == JournalScope.Run && !runDetails)
        {
            section = T("Combat history · select a row", "Historique des combats · choisir une ligne");
            for (int index = run.Combats.Count - 1; index >= 0; index--)
            {
                var c = run.Combats[index];
                var ct = session.Query(JournalScope.Combat, c.Key, 0, selection.PlayerId).Totals;
                string label = $"{index + 1}. {T("Floor", "Étage")} {c.Floor} · {c.Encounter}";
                rows.Add(new JournalRow(label, "−" + N(ct[Stat.HpLost]) + T(" HP · ", " PV · ") + Outcome(c.Outcome, french), c.Key));
            }
        }
        else
        {
            section = T("Observed details", "Détail des valeurs observées");
            rows.Add(new JournalRow(T("Block gained / expired / removed", "Bloc gagné / dissipé / retiré"),
                $"{N(totals[Stat.BlockGained])} / {N(totals[Stat.BlockExpired])} / {N(totals[Stat.BlockRemoved])}"));
            rows.Add(new JournalRow(T("Energy spent / generated", "Énergie dépensée / générée"), $"{N(totals[Stat.EnergySpent])} / {N(totals[Stat.EnergyGained])}"));
            rows.Add(new JournalRow(T("Cards drawn / discarded / exhausted", "Cartes piochées / défaussées / épuisées"),
                $"{N(totals[Stat.CardsDrawn])} / {N(totals[Stat.CardsDiscarded])} / {N(totals[Stat.CardsExhausted])}"));
            rows.Add(new JournalRow(T("Healing received", "Soins reçus"), "+" + N(totals[Stat.Healing])));
            rows.Add(new JournalRow(T("Damage to enemy block / overkill", "Dégâts au bloc adverse / excédent mortel"), $"{N(totals[Stat.DamageDealtBlocked])} / {N(totals[Stat.Overkill])}"));
            rows.Add(new JournalRow(T("Cards generated / potions used", "Cartes générées / potions utilisées"), $"{N(totals[Stat.CardsGenerated])} / {N(totals[Stat.PotionsUsed])}"));
            if (totals[Stat.PetHpLost] > 0 || totals[Stat.PetHealing] > 0 || totals[Stat.PetDamageDealtHp] > 0)
            {
                rows.Add(new JournalRow(T("Pet HP lost / healing", "Familier : PV perdus / soins"), $"{N(totals[Stat.PetHpLost])} / {N(totals[Stat.PetHealing])}"));
                rows.Add(new JournalRow(T("Pet damage · included above", "Dégâts du familier · inclus ci-dessus"), N(totals[Stat.PetDamageDealtHp])));
            }
            if (totals[Stat.StarsGained] > 0 || totals[Stat.StarsSpent] > 0)
                rows.Add(new JournalRow(T("Stars gained / spent", "Étoiles gagnées / dépensées"), $"{N(totals[Stat.StarsGained])} / {N(totals[Stat.StarsSpent])}"));
            if (totals[Stat.OrbsChanneled] > 0) rows.Add(new JournalRow(T("Orbs channeled", "Orbes canalisés"), N(totals[Stat.OrbsChanneled])));
            var unknown = session.QueryUnattributed(selection.Scope, selection.CombatKey, selection.Round);
            if (unknown[Stat.DamageDealtHp] > 0 || unknown[Stat.DamageDealtBlocked] > 0)
                rows.Add(new JournalRow(T("Unattributed enemy HP / block · all players", "Sans attribution : PV / bloc · global"),
                    $"{N(unknown[Stat.DamageDealtHp])} / {N(unknown[Stat.DamageDealtBlocked])}"));
        }
        bool partial = selection.Scope == JournalScope.Run ? run.Partial || run.Combats.Any(c => c.Partial) : combat?.Partial == true;
        string note = partial ? T("Incomplete record: some earlier observations are missing.", "Relevé incomplet : certaines observations antérieures manquent.") :
            selection.Scope == JournalScope.Round && selection.Round > 0 ? T("Player and enemy phases of this round. Setup is separate.", "Phases joueur et ennemie de ce tour. Préparation séparée.") :
            T("Actual combat values. Healing does not erase HP lost.", "Valeurs réelles de combat. Les soins n’effacent pas les PV perdus.");
        if (!partial && combat?.ReplacedAttempts > 0 && selection.Scope != JournalScope.Run)
            note = T("Reloaded combat: old attempt replaced, not added.", "Combat rechargé : l’ancienne tentative est remplacée, pas additionnée.");
        if (timeline && selection.Scope != JournalScope.Run)
            note = combat?.DamageTraceVersion != 1
                ? T("Legacy combat: totals are available, but no timeline was recorded.", "Ancien combat : totaux disponibles, sans chronologie enregistrée.")
                : combat.DamageTraceTruncated
                ? T("Old trace rows were pruned; aggregate totals are unchanged.", "Anciennes lignes retirées ; les totaux restent inchangés.")
                : T("Native results. Poison shares are conventional. Pre-damage reductions are not reconstructed.",
                    "Résultats natifs. Parts de poison conventionnelles. Réductions initiales non reconstituées.");
        int pages = Math.Max(1, (rows.Count + RowsPerPage - 1) / RowsPerPage);
        page = Math.Clamp(page, 0, pages - 1);
        return new JournalReport { Context = context, Player = player, Section = section, Note = note,
            HeroValues = new[] { N(totals[Stat.DamageDealtHp]), N(totals[Stat.HpLost]), N(totals[Stat.DamageBlocked]), N(totals[Stat.CardsPlayed]) },
            Rows = rows.Skip(page * RowsPerPage).Take(RowsPerPage).ToArray(), Page = page, Pages = pages };
    }
    private static string Outcome(CombatOutcome outcome, bool fr) => outcome switch
    {
        CombatOutcome.Won => fr ? "Victoire" : "Won", CombatOutcome.Lost => fr ? "Défaite" : "Lost",
        CombatOutcome.Interrupted => fr ? "Interrompu" : "Interrupted", _ => fr ? "En cours" : "Live"
    };
    private static string RunStatus(string status, bool fr) => status switch
    {
        "won" => fr ? "Partie gagnée" : "Run won", "lost" => fr ? "Partie perdue" : "Run lost",
        "abandoned" => fr ? "Abandonnée" : "Abandoned", "suspended" => fr ? "En pause" : "Paused",
        _ => fr ? "Partie en cours" : "Current run"
    };
}
