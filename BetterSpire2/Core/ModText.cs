#nullable enable
using Godot;
using MegaCrit.Sts2.Core.Localization;
using System;
using System.Collections.Generic;

namespace BetterSpire2.Core;

internal static class ModText
{
    private static readonly IReadOnlyDictionary<string, string> French = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Decrease"] = "Diminuer",
        ["Increase"] = "Augmenter",
        ["Synchronizing hand…"] = "Synchronisation…",
        ["Drag name to move · Page Up / Page Down: teammate · F3: close"] = "Glisser le nom : déplacer · Page haut / bas : coéquipier · F3 : fermer",
        ["Team statistics instead of cards"] = "Statistiques à la place des cartes",
        ["Resize the borderless thumbnails. At 100%, portraits are 42 x 42 viewport pixels; hover for details."] = "Redimensionne les miniatures sans panneau. À 100 %, les images font 42 × 42 pixels ; détails au survol.",
        ["Lock meter position"] = "Verrouiller la position du compteur",
        ["Make the title click-through too. Unlock to drag the meter like the clock."] = "Le titre laisse aussi passer les clics. Déverrouillez-le pour déplacer le compteur comme l’horloge.",
        ["Drag the meter title to move it; the damage rows remain click-through."] = "Faites glisser le titre ; les lignes de dégâts laissent passer les clics.",
        ["Meter position locked"] = "Position du compteur verrouillée",
        ["Partial data: some damage could not be observed."] = "Données partielles : certains dégâts n’ont pas pu être observés.",
        ["Enable incoming damage first."] = "Activez d’abord les dégâts entrants.",
        ["Enable the damage meter first."] = "Activez d’abord le compteur de dégâts.",
        ["Enable the hand viewer first."] = "Activez d’abord l’affichage des mains.",
        ["Switch to cards to change their size."] = "Passez en vue cartes pour modifier leur taille.",
        ["Enable the clock first."] = "Activez d’abord l’horloge.",
        ["On"] = "Activé",
        ["Off"] = "Désactivé",
        ["Previous teammate"] = "Coéquipier précédent",
        ["Next teammate"] = "Coéquipier suivant",
        ["Summary"] = "Résumé",
        ["Show cards"] = "Afficher les cartes",
        ["Show actual combat statistics only"] = "Afficher uniquement les statistiques réelles du combat",
        ["No teammates to display. Disable Hide your own hand to inspect your cards."] = "Aucun coéquipier à afficher. Désactivez « Masquer votre main » pour consulter vos cartes.",
        ["Page Up / Page Down: teammate · Drag: move · Edges: resize"] = "Page haut / bas : coéquipier · Glisser : déplacer · Bords : redimensionner",
        ["Hand is empty."] = "La main est vide.",
        ["Hand unavailable. Waiting for combat synchronization."] = "Main indisponible. En attente de synchronisation du combat.",
        ["Card unavailable"] = "Carte indisponible",
        ["Card preview unavailable. Actual combat statistics remain available."] = "Aperçu de carte indisponible. Les statistiques réelles du combat restent affichées.",
        ["Show actual HP, block and energy without rendering cards."] = "Affiche les PV, le blocage et l’énergie réels sans rendre les cartes.",
        ["Skip the introduction on the next game launch; this does not change the current screen."] = "Ignore l’introduction au prochain lancement du jeu ; ne modifie pas l’écran actuel.",
        ["Settings could not be saved. Changes are active for this session only."] = "Échec de sauvegarde : changements actifs uniquement pour cette session.",

        ["Settings saved."] = "Paramètres enregistrés.",
        ["Drawings muted for this session."] = "Dessins masqués pour cette session.",
        ["Drawings visible. Hiding is local to your client."] = "Dessins visibles. Le masquage ne concerne que votre affichage.",
        ["Damage meter"] = "Kikimètre",
        ["Combat / run damage meter"] = "Dégâts du combat / de la run",
        ["Current fight in combat; run total on the map. Steam nicknames in solo and co-op. F6 only hides the display."] = "Combat en cours pendant le combat, total de la run sur la carte. Pseudos Steam en solo et en coopération. F6 masque seulement l’affichage.",
        ["Show damage meter"] = "Afficher le kikimètre",
        ["Show combat damage in fights and run damage on the map, without a panel. Toggle with F6."] = "Affiche les dégâts du combat en cours, puis ceux de la run sur la carte, sans panneau. Activer ou masquer avec F6.",
        ["Include enemy block"] = "Inclure le blocage adverse",
        ["Off: actual enemy HP removed, matching F5. On: also include damage absorbed by enemy block. Overkill is never added."] = "Désactivé : PV réellement retirés, comme F5. Activé : ajoute les dégâts absorbés par le blocage ennemi. Sans excédent mortel.",
        ["Thin ranking bars"] = "Barres de classement fines",
        ["Show a thin comparative line under each player, only in multiplayer."] = "Affiche une fine ligne comparative sous chaque joueur, uniquement en multijoueur.",
        ["Only show during combat"] = "Afficher uniquement en combat",
        ["Hide the meter on the map and outside combat. Leave off for automatic combat / run display."] = "Masque le compteur sur la carte et hors combat. Laisser désactivé pour la bascule automatique combat / run.",
        ["Meter scale"] = "Taille du kikimètre",
        ["Resize the damage text and bars."] = "Redimensionne les chiffres et les barres du compteur.",
        ["Meter horizontal position"] = "Position horizontale du kikimètre",
        ["Move the meter within the screen without intercepting clicks."] = "Déplace le compteur dans l’écran sans intercepter les clics.",
        ["Meter vertical position"] = "Position verticale du kikimètre",
        ["Keep the meter away from cards and the overhead HP display."] = "Permet de placer le compteur à l’écart des cartes et des PV au-dessus des têtes.",
        ["Settings"] = "Paramètres",
        ["Combat"] = "Combat",
        ["Optional summary line"] = "Ligne de bilan optionnelle",
        ["Show a small observed-round text line. The full journal is always available with F5."] = "Affiche une petite ligne de bilan réel du tour. Le journal complet reste accessible avec F5.",
        ["Combat HUD"] = "Interface de combat",
        ["Multiplayer"] = "Multijoueur",
        ["Gameplay"] = "Confort",
        ["Party"] = "Équipe",
        ["Close"] = "Fermer",
        ["Drag the title to move"] = "Faites glisser le titre pour déplacer",
        ["F1 closes this panel"] = "F1 ferme ce panneau",
        ["Reset interface layout"] = "Réinitialiser la disposition",
        ["Reset saved positions and sizes for every BetterSpire overlay."] = "Réinitialise les positions et tailles enregistrées de toutes les interfaces BetterSpire.",
        ["Interface layout reset"] = "Disposition réinitialisée",

        ["Combat information"] = "Informations de combat",
        ["Make incoming damage and turn results easier to read."] = "Rend les dégâts entrants et les résultats du tour plus faciles à lire.",
        ["Team information"] = "Informations d’équipe",
        ["Inspect teammate hands and control multiplayer overlays."] = "Consulte les mains alliées et contrôle les interfaces multijoueur.",
        ["Game conveniences"] = "Confort de jeu",
        ["Small options that reduce waiting and keep useful information visible."] = "Options pratiques pour réduire l’attente et garder les informations utiles visibles.",

        ["Multi-hit totals"] = "Totaux des attaques multiples",
        ["Show the combined value beside enemy multi-hit intents."] = "Affiche la valeur cumulée à côté des intentions ennemies à plusieurs coups.",
        ["Show the compact Guardian damage, block and survival forecast."] = "Affiche le HUD compact Guardian : dégâts, blocage et survie prévus.",
        ["Only show danger"] = "Afficher seulement les risques",
        ["Hide zero-loss projections, but always show uncertainty and used revivals."] = "Masque les projections sans perte, mais garde les incertitudes et résurrections consommées.",
        ["Guardian scale"] = "Taille du HUD Guardian",
        ["Scale the click-through HUD without moving game elements."] = "Redimensionne le HUD traversable sans déplacer les éléments du jeu.",
        ["Guardian horizontal position"] = "Position horizontale de Guardian",
        ["Guardian vertical position"] = "Position verticale de Guardian",
        ["Move the HUD horizontally within the visible screen."] = "Déplace le HUD horizontalement dans l'écran visible.",
        ["Move the HUD vertically within the visible screen."] = "Déplace le HUD verticalement dans l'écran visible.",
        ["Show remaining HP (HP lost) above characters; F2 opens optional details."] = "Affiche les PV restants (PV perdus) au-dessus des personnages ; F2 ouvre les détails facultatifs.",
        ["Resize the overhead numbers without moving game elements."] = "Redimensionne les chiffres au-dessus des personnages sans déplacer les éléments du jeu.",
        ["Height above characters"] = "Hauteur au-dessus des personnages",
        ["Distance between the head and the HP readout, in viewport pixels."] = "Distance entre la tête et les chiffres, en pixels du viewport.",
        ["Overhead horizontal offset"] = "Décalage horizontal des chiffres",
        ["Shift the overhead numbers left or right without moving characters."] = "Décale les chiffres à gauche ou à droite sans déplacer les personnages.",
        ["Numbers above teammates"] = "Chiffres au-dessus des coéquipiers",
        ["Show a separate conditional forecast above each teammate."] = "Affiche une prévision conditionnelle propre à chaque coéquipier.",
        ["Numbers above pets"] = "Chiffres au-dessus des familiers",
        ["Show a separate readout above Osty or another pet when it is projected to lose HP."] = "Affiche les PV propres à Osty ou un autre familier quand il doit perdre des PV.",
        ["Details horizontal position"] = "Position horizontale des détails",
        ["Details vertical position"] = "Position verticale des détails",
        ["Move only the optional F2 details panel horizontally."] = "Déplace horizontalement uniquement le panneau facultatif de F2.",
        ["Move only the optional F2 details panel vertically."] = "Déplace verticalement uniquement le panneau facultatif de F2.",
        ["Incoming damage"] = "Dégâts entrants",
        ["Show projected HP damage above each player."] = "Affiche les dégâts PV prévus au-dessus de chaque joueur.",
        ["Turn summary"] = "Résumé du tour",
        ["Show the movable combat and round statistics panel."] = "Affiche le panneau déplaçable de statistiques du combat et des rounds.",

        ["Teammate hand viewer"] = "Mains des coéquipiers",
        ["Allow the F3 teammate hand and status viewer."] = "Active l’affichage des mains et états alliés avec F3.",
        ["Only close the viewer with F3 or its close button."] = "Ferme l’affichage uniquement avec F3 ou son bouton de fermeture.",
        ["Open at combat start"] = "Ouvrir au début du combat",
        ["Automatically show teammate information when combat begins."] = "Affiche automatiquement les informations alliées au début du combat.",
        ["Hide your own hand"] = "Masquer votre main",
        ["Only include teammates in the hand viewer."] = "Affiche uniquement les coéquipiers dans le panneau des mains.",
        ["Compact team view"] = "Vue d’équipe compacte",
        ["Show combat statistics without rendering cards."] = "Affiche les statistiques de combat sans rendre les cartes.",
        ["Card size"] = "Taille des cartes",
        ["Scale cards in the teammate hand viewer."] = "Ajuste la taille des cartes dans l’affichage des mains alliées.",

        ["Instant fast mode"] = "Mode rapide instantané",
        ["Remove combat speed delays while leaving menus unchanged."] = "Supprime les délais d’animation en combat sans modifier les menus.",
        ["Skip splash screen"] = "Ignorer l’écran d’introduction",
        ["Open the main menu without waiting for the splash sequence."] = "Ouvre le menu principal sans attendre la séquence d’introduction.",
        ["Show clock"] = "Afficher l’heure",
        ["Show a small movable clock overlay."] = "Affiche une petite horloge déplaçable.",
        ["24-hour clock"] = "Format 24 heures",
        ["Use 24-hour time instead of AM and PM."] = "Utilise le format 24 heures au lieu de AM et PM.",

        ["Show drawings"] = "Afficher les dessins",
        ["Hide drawings"] = "Masquer les dessins",
        ["Clear all drawings"] = "Effacer tous les dessins",
        ["You"] = "Vous",
        ["Player"] = "Joueur",
        ["Enemy"] = "Ennemi",
        ["Unknown"] = "Inconnu",
        ["Other"] = "Autre",
        ["Power / Relic"] = "Pouvoir / Relique",

        ["Party hands"] = "Mains de l’équipe",
        ["Page Up / Page Down: switch teammate · F3: close"] = "Page haut / Page bas : changer de coéquipier · F3 : fermer",
        ["F3: close · Drag the title to move"] = "F3 : fermer · Faites glisser le titre pour déplacer",
        ["Drag to move | Drag edges to resize | F3 or X to close"] = "Glisser pour déplacer | Bordures pour redimensionner | F3 ou X pour fermer",
        ["Drag to move | Drag edges to resize | Click outside to close"] = "Glisser pour déplacer | Bordures pour redimensionner | Cliquer à l’extérieur pour fermer",
        ["Hand"] = "Main",
        ["Potions:"] = "Potions :",
        ["Energy"] = "Énergie",
        ["Block"] = "Blocage",
        ["Star"] = "Étoile",
        ["Orb"] = "Orbe",

        ["All"] = "Tout",
        ["Detail"] = "Détail",
        ["Compact"] = "Compact",
        ["Waiting for data…"] = "En attente de données…",
        ["YOU"] = "VOUS",
        ["Cards"] = "Cartes",
        ["Damage out"] = "Dégâts infligés",
        ["Damage in"] = "Dégâts reçus",
        ["Stars"] = "Étoiles",
        ["Sources"] = "Sources",
        ["Out"] = "Infligés",
        ["In"] = "Reçus",
        ["Break"] = "Brisé",
        ["Draw"] = "Piochées",
        ["Play"] = "Jouées",
        ["Discard"] = "Défaussées",
        ["Gen"] = "Gén.",
        ["Generated"] = "Générées",
        ["Exhaust"] = "Épuisées",
        ["Orbs"] = "Orbes",
        ["Potions"] = "Potions",
        ["Pots"] = "Potions",
        ["Gain"] = "Gagné",
        ["Spent"] = "Dépensé",
        ["Net"] = "Net",
        ["Total"] = "Total",
        ["HP"] = "PV",
        ["Overkill"] = "Excès",
        ["Lost"] = "Perdu",
        ["Lose"] = "Perdu",
        ["Cleared"] = "Effacé",
        ["Clear"] = "Effacé",
        ["Played cards"] = "Cartes jouées",
        ["dmg"] = "dgt",
        ["blk"] = "blc",
        ["atk"] = "att",
        ["skill"] = "comp",
        ["pwr"] = "pouv"
    };

    internal static string T(string english)
    {
        return IsFrench && French.TryGetValue(english, out string? translated) ? translated : english;
    }

    internal static bool IsFrench
    {
        get
        {
            try
            {
                string? language = LocManager.Instance?.Language;
                if (!string.IsNullOrWhiteSpace(language))
                {
                    return language.Equals("fra", StringComparison.OrdinalIgnoreCase)
                        || language.StartsWith("fr", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // LocManager may not be initialized while the mod is loading.
            }

            string locale = TranslationServer.GetLocale();
            return locale.StartsWith("fr", StringComparison.OrdinalIgnoreCase);
        }
    }

}
