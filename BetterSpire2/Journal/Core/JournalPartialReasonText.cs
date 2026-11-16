#nullable enable
using System;

namespace BetterSpire2.Journal.Core;

public static class JournalPartialReasonText
{
    public static string Format(string reason, bool french)
    {
        var text = reason switch
        {
            "damage.command" => ("Damage command patch unavailable", "Patch de commande de dégâts indisponible"),
            "damage.results" => ("Native damage-result observer unavailable", "Observateur des résultats natifs de dégâts indisponible"),
            "damage.interrupted" => ("Damage collection was interrupted", "La collecte des dégâts a été interrompue"),
            "poison.origin" => ("Poison owner could not be identified", "L’origine du poison n’a pas pu être identifiée"),
            "poison.scope" => ("Poison damage scope unavailable", "Portée des dégâts de poison indisponible"),
            "poison.history" => ("Poison history could not be read", "L’historique du poison n’a pas pu être lu"),
            "effect.strangle" => ("Strangle damage observation unavailable", "Observation des dégâts d’Étranglement indisponible"),
            "effect.doom" => ("Doom damage observation unavailable", "Observation des dégâts de Trépas indisponible"),
            "effect.doom-batch" => ("Doom batch-damage observation unavailable", "Observation des dégâts groupés de Trépas indisponible"),
            "effect.judgement" => ("Judgement damage source unavailable", "Source des dégâts de Jugement indisponible"),
            "effect.judgement.scope" => ("Judgement damage scope unavailable", "Portée des dégâts de Jugement indisponible"),
            "effect.judgement.hp" => ("Judgement HP-loss observation unavailable", "Observation des PV retirés par Jugement indisponible"),
            "effect.scope" => ("Effect damage scope unavailable", "Portée des dégâts d’effet indisponible"),
            "effect.hp" => ("Effect HP-damage observers unavailable", "Observateurs des dégâts d’effet aux PV indisponibles"),
            "effect.hp:damage" => ("Effect HP-damage observer unavailable", "Observateur des dégâts d’effet aux PV indisponible"),
            "effect.hp:kill" => ("Effect fatal-damage observer unavailable", "Observateur des dégâts mortels d’effet indisponible"),
            "damage-source:lightning" => ("Lightning source could not be identified", "La source de foudre n’a pas pu être identifiée"),
            "damage-source:dark-orb" => ("Dark orb source could not be identified", "La source de l’orbe des ténèbres n’a pas pu être identifiée"),
            "damage-source:thorns" => ("Thorns source could not be identified", "La source des épines n’a pas pu être identifiée"),
            "damage-source:flame-barrier" => ("Flame Barrier source could not be identified", "La source de Barrière de feu n’a pas pu être identifiée"),
            "damage-source:explosive-arrows" => ("Explosive Arrows source could not be identified", "La source de Flèches explosives n’a pas pu être identifiée"),
            "damage-source:violent-chord" => ("Violent Chord source could not be identified", "La source d’Accord violent n’a pas pu être identifiée"),
            _ when reason.StartsWith("patch:", StringComparison.Ordinal) =>
                ($"Patch unavailable: {reason[6..]}", $"Observation indisponible : {reason[6..]}"),
            _ when reason.StartsWith("damage-source:", StringComparison.Ordinal) =>
                ($"Damage source could not be identified: {reason[14..]}", $"Source des dégâts non identifiée : {reason[14..]}"),
            _ => (reason, reason)
        };
        return french ? text.Item2 : text.Item1;
    }
}
