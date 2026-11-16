using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterSpire2.Guardian.Core;

internal static class ForecastReactionTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static void Run()
    {
        var failures = new System.Collections.Generic.List<string>();
        RunCase(ThornsRetaliatesBeforeTheEngagedHit);
        RunCase(EnemyAoeHitContinuesAcrossTargetsAfterThornsKillsDealer);
        RunCase(FlameBarrierReflectsAfterSurvivedHitsOnly);
        RunCase(FlameBarrierTriggersWhenBlockOrBufferPreventsHpLoss);
        RunCase(FlameBarrierTriggersForOstyOwnerResult);
        RunCase(LizardTailPreventsDeathAndRetainsPowers);
        RunCase(LightningBranchesAcrossLivingTargetsAndReportsRanges);
        RunCase(LightningRerollsOnlyAmongCurrentSurvivors);
        RunCase(BranchBudgetDoesNotClaimAnExhaustiveRange);
        RunCase(DoomChecksCurrentHpAtItsTurnEndPhase);
        RunCase(DoomSkipsDeadOwnersBeforeSelectingTheFirstDoomedCreature);
        RunCase(DoomCallbackMustBelongToTheFirstLivingDoomedOwner);
        RunCase(DoomDoesNotRunAfterCombatHasEnded);
        if (failures.Count != 0) throw new Exception(string.Join(" | ", failures));

        void RunCase(Action test)
        {
            try { test(); }
            catch (Exception ex) { failures.Add(test.Method.Name + ": " + ex.Message); }
        }
    }

    private static void ThornsRetaliatesBeforeTheEngagedHit()
    {
        var result = RunJson("""
        {
          "Actors": [
            { "Id": "p", "Name": "Player", "Hp": 10, "MaxHp": 10, "Block": 0,
              "DamageReactions": [{ "Kind": "Thorns", "Amount": 5, "Source": "Thorns" }] },
            { "Id": "e", "Name": "Enemy", "Hp": 4, "MaxHp": 4, "Block": 0 }
          ],
          "Events": [
            { "Kind": "Damage", "Target": "p", "Source": "Hit 1", "Amount": 4,
              "PoweredAttack": true, "TriggersThorns": true, "Attacker": "e", "RequireLivingAttacker": true },
            { "Kind": "Damage", "Target": "p", "Source": "Hit 2", "Amount": 4,
              "PoweredAttack": true, "TriggersThorns": true, "Attacker": "e", "RequireLivingAttacker": true }
          ],
          "Warnings": [], "LocalPlayerId": "p", "EnemyIds": ["e"]
        }
        """);

        Equal(6, result.Local.FinalHp, "The hit already in progress lands after fatal Thorns.");
        Equal(0, result.Actors["e"].FinalHp, "Thorns damage is applied to the attacker before the hit.");
        Equal(4, result.Local.HpLost, "A later hit from the dead attacker is skipped.");
    }

    private static void EnemyAoeHitContinuesAcrossTargetsAfterThornsKillsDealer()
    {
        EnsureEventKind("DamageCommand");
        var result = RunJson("""
        {
          "Actors": [
            { "Id": "p1", "Name": "Player 1", "Hp": 10, "MaxHp": 10, "Block": 0,
              "DamageReactions": [{ "Kind": "Thorns", "Amount": 5, "Source": "Thorns" }] },
            { "Id": "p2", "Name": "Player 2", "Hp": 10, "MaxHp": 10, "Block": 0 },
            { "Id": "e", "Name": "Enemy", "Hp": 4, "MaxHp": 4, "Block": 0 }
          ],
          "Events": [
            { "Kind": "DamageCommand", "Target": "p1", "Source": "Attack 1", "Amount": 4,
              "PoweredAttack": true, "TriggersThorns": true, "Attacker": "e", "RequireLivingAttacker": true,
              "TargetOptions": [{ "Target": "p1", "Amount": 4 }, { "Target": "p2", "Amount": 4 }] },
            { "Kind": "DamageCommand", "Target": "p1", "Source": "Attack 2", "Amount": 4,
              "PoweredAttack": true, "TriggersThorns": true, "Attacker": "e", "RequireLivingAttacker": true,
              "TargetOptions": [{ "Target": "p1", "Amount": 4 }, { "Target": "p2", "Amount": 4 }] }
          ],
          "Warnings": [], "LocalPlayerId": "p1", "EnemyIds": ["e"]
        }
        """);

        Equal(6, result.Actors["p1"].FinalHp, "The first player takes the already-engaged AOE hit.");
        Equal(6, result.Actors["p2"].FinalHp, "The other player still takes that same hit after Thorns kills its dealer.");
        Equal(0, result.Actors["e"].FinalHp, "The first player's Thorns kills the attacker.");
    }

    private static void FlameBarrierReflectsAfterSurvivedHitsOnly()
    {
        var survived = RunJson("""
        {
          "Actors": [
            { "Id": "p", "Name": "Player", "Hp": 8, "MaxHp": 8, "Block": 0,
              "DamageReactions": [{ "Kind": "FlameBarrier", "Amount": 4, "Source": "Flame Barrier" }] },
            { "Id": "e", "Name": "Enemy", "Hp": 10, "MaxHp": 10, "Block": 0 }
          ],
          "Events": [{ "Kind": "Damage", "Target": "p", "Source": "Hit", "Amount": 2,
            "PoweredAttack": true, "TriggersThorns": true, "Attacker": "e" }],
          "Warnings": [], "LocalPlayerId": "p", "EnemyIds": ["e"]
        }
        """);
        Equal(6, survived.Local.FinalHp, "The incoming hit resolves before Flame Barrier.");
        Equal(6, survived.Actors["e"].FinalHp, "Flame Barrier reflects after a survived hit.");

        var fatal = RunJson("""
        {
          "Actors": [
            { "Id": "p", "Name": "Player", "Hp": 2, "MaxHp": 2, "Block": 0,
              "DamageReactions": [{ "Kind": "FlameBarrier", "Amount": 4, "Source": "Flame Barrier" }] },
            { "Id": "e", "Name": "Enemy", "Hp": 10, "MaxHp": 10, "Block": 0 }
          ],
          "Events": [{ "Kind": "Damage", "Target": "p", "Source": "Fatal hit", "Amount": 2,
            "PoweredAttack": true, "TriggersThorns": true, "Attacker": "e" }],
          "Warnings": [], "LocalPlayerId": "p", "EnemyIds": ["e"]
        }
        """);
        Equal(0, fatal.Local.FinalHp, "A fatal hit ends the receiver's reaction sequence.");
        Equal(10, fatal.Actors["e"].FinalHp, "Flame Barrier does not trigger after an unrecovered death.");
    }

    private static void FlameBarrierTriggersWhenBlockOrBufferPreventsHpLoss()
    {
        var result = RunJson("""
        {
          "Actors": [
            { "Id": "p", "Name": "Player", "Hp": 10, "MaxHp": 10, "Block": 2,
              "HpRules": [{ "Kind": "Buffer", "Value": 0, "Charges": 1, "Source": "Buffer" }],
              "DamageReactions": [{ "Kind": "FlameBarrier", "Amount": 3, "Source": "Flame Barrier" }] },
            { "Id": "e", "Name": "Enemy", "Hp": 10, "MaxHp": 10, "Block": 0 }
          ],
          "Events": [{ "Kind": "Damage", "Target": "p", "Source": "Hit", "Amount": 5,
            "PoweredAttack": true, "Attacker": "e" }],
          "Warnings": [], "LocalPlayerId": "p", "EnemyIds": ["e"]
        }
        """);

        Equal(10, result.Local.FinalHp, "Block and Buffer prevent player HP loss.");
        Equal(1, result.Local.BuffersUsed, "Buffer is consumed by the unblocked hit.");
        Equal(7, result.Actors["e"].FinalHp, "A surviving powered hit still triggers Flame Barrier.");
    }

    private static void FlameBarrierTriggersForOstyOwnerResult()
    {
        var result = RunJson("""
        {
          "Actors": [
            { "Id": "p", "Name": "Player", "Hp": 8, "MaxHp": 8, "Block": 0,
              "RedirectPoweredAttacksTo": "pet",
              "DamageReactions": [{ "Kind": "FlameBarrier", "Amount": 4, "Source": "Flame Barrier" }] },
            { "Id": "pet", "Name": "Osty", "Hp": 10, "MaxHp": 10, "Block": 0, "SharedBlockOwner": "p" },
            { "Id": "e", "Name": "Enemy", "Hp": 10, "MaxHp": 10, "Block": 0 }
          ],
          "Events": [{ "Kind": "Damage", "Target": "p", "Source": "Hit", "Amount": 3,
            "PoweredAttack": true, "Attacker": "e" }],
          "Warnings": [], "LocalPlayerId": "p", "EnemyIds": ["e"]
        }
        """);

        Equal(8, result.Local.FinalHp, "The redirected hit does not damage the player.");
        Equal(7, result.Actors["pet"].FinalHp, "Osty receives the attack.");
        Equal(6, result.Actors["e"].FinalHp, "The zero-loss owner result triggers Flame Barrier after the redirected result.");
    }

    private static void LizardTailPreventsDeathAndRetainsPowers()
    {
        var result = RunJson("""
        {
          "Actors": [
            { "Id": "p", "Name": "Player", "Hp": 2, "MaxHp": 10, "Block": 0, "ReviveHp": 5,
              "IsPlayer": true, "DoomThreshold": 5,
              "DamageReactions": [{ "Kind": "Thorns", "Amount": 5, "Source": "Thorns" }] },
            { "Id": "q", "Name": "Teammate", "Hp": 4, "MaxHp": 10, "Block": 0,
              "IsPlayer": true, "DoomThreshold": 5 },
            { "Id": "e1", "Name": "Enemy 1", "Hp": 4, "MaxHp": 4, "Block": 0 },
            { "Id": "e2", "Name": "Enemy 2", "Hp": 4, "MaxHp": 4, "Block": 0 }
          ],
          "Events": [
            { "Kind": "Damage", "Target": "p", "Source": "Fatal hit", "Amount": 2,
              "PoweredAttack": true, "TriggersThorns": true, "Attacker": "e1" },
            { "Kind": "Damage", "Target": "p", "Source": "Later hit", "Amount": 1,
              "PoweredAttack": true, "TriggersThorns": true, "Attacker": "e2" },
            { "Kind": "DoomCheck", "Target": "p", "Source": "Doom", "AffectedActors": ["p", "q"],
              "EligibleOwners": ["q"] }
          ],
          "Warnings": [], "LocalPlayerId": "p", "EnemyIds": ["e1", "e2"]
        }
        """);

        Equal(4, result.Local.FinalHp, "The player revives at 5 HP and takes the later hit.");
        Equal(0, result.Actors["e1"].FinalHp, "Thorns kills the first attacker before the engaged hit.");
        Equal(0, result.Actors["e2"].FinalHp, "Lizard Tail prevents death, so the player's Thorns remains active.");
        Equal(4, result.Actors["q"].FinalHp, "The revived player keeps Doom and remains first in the doomed-actor order.");
    }

    private static void LightningBranchesAcrossLivingTargetsAndReportsRanges()
    {
        EnsureEventKind("RandomTargetDamage");
        var json = """
        {
          "Actors": [
            { "Id": "p", "Name": "Player", "Hp": 20, "MaxHp": 20, "Block": 0 },
            { "Id": "a", "Name": "Enemy A", "Hp": 2, "MaxHp": 2, "Block": 0 },
            { "Id": "b", "Name": "Enemy B", "Hp": 10, "MaxHp": 10, "Block": 0 }
          ],
          "Events": [
            { "Kind": "RandomTargetDamage", "Target": "a", "Source": "Lightning orb",
              "Amount": 3, "Attacker": "p", "RequireLivingAttacker": true,
              "TargetOptions": [
                { "Target": "a", "Amount": 3 }, { "Target": "b", "Amount": 3 }
              ] },
            { "Kind": "Damage", "Target": "p", "Source": "Enemy A", "Amount": 4,
              "Attacker": "a", "RequireLivingAttacker": true },
            { "Kind": "Damage", "Target": "p", "Source": "Enemy B", "Amount": 4,
              "Attacker": "b", "RequireLivingAttacker": true }
          ],
          "Warnings": [], "LocalPlayerId": "p", "EnemyIds": ["a", "b"]
        }
        """;

        var first = RunJson(json);
        var second = RunJson(json);
        Equal(12, Property<int>(first.Local, "FinalHpMinimum"), "The branch where both attackers survive is the lower bound.");
        Equal(16, Property<int>(first.Local, "FinalHpMaximum"), "Killing one attacker is the upper bound.");
        Equal(2, Property<int>(first, "ScenarioCount"), "Only the two distinct outcomes are retained.");
        Equal(true, Property<bool>(first, "BranchEnumerationComplete"), "Both target outcomes are exhaustive.");
        Equal(JsonSerializer.Serialize(first, Json), JsonSerializer.Serialize(second, Json), "Branching does not consume game RNG.");
        var readout = OverheadPresentation.Format(first.Local, first.IsPartial);
        Equal("12–16 (4–8)", readout.Text, "HUD shows the full projected HP and loss ranges.");
        Equal(OverheadSeverity.Uncertain, readout.Severity, "Different target outcomes use uncertainty styling.");
    }

    private static void LightningRerollsOnlyAmongCurrentSurvivors()
    {
        var result = RunJson("""
        {
          "Actors": [
            { "Id": "p", "Name": "Player", "Hp": 20, "MaxHp": 20, "Block": 0 },
            { "Id": "a", "Name": "Enemy A", "Hp": 2, "MaxHp": 2, "Block": 0 },
            { "Id": "b", "Name": "Enemy B", "Hp": 10, "MaxHp": 10, "Block": 0 }
          ],
          "Events": [
            { "Kind": "RandomTargetDamage", "Target": "a", "Source": "Lightning 1", "Amount": 3,
              "Attacker": "p", "TargetOptions": [{ "Target": "a", "Amount": 3 }, { "Target": "b", "Amount": 3 }] },
            { "Kind": "RandomTargetDamage", "Target": "a", "Source": "Lightning 2", "Amount": 3,
              "Attacker": "p", "TargetOptions": [{ "Target": "a", "Amount": 3 }, { "Target": "b", "Amount": 3 }] }
          ],
          "Warnings": [], "LocalPlayerId": "p", "EnemyIds": ["a", "b"]
        }
        """);

        Equal(0, result.Actors["a"].FinalHp, "The first passive can kill its selected target.");
        Equal(4, result.Actors["b"].FinalHp, "The next passive targets only the surviving enemy.");
        Equal(0, result.Actors["a"].MinimumFinalHp, "One outcome kills Enemy A.");
        Equal(2, result.Actors["a"].MaximumFinalHp, "Other outcomes leave Enemy A alive after the first strike.");
        Equal(4, result.Actors["b"].MinimumFinalHp, "A second passive may retarget Enemy B after Enemy A dies.");
        Equal(7, result.Actors["b"].MaximumFinalHp, "A branch retargets Enemy A while Enemy B remains at 7 HP.");
        Equal(3, result.ScenarioCount, "Distinct attack chronology states are retained even when some final totals match.");
    }

    private static void BranchBudgetDoesNotClaimAnExhaustiveRange()
    {
        EnsureEventKind("RandomTargetDamage");
        var actors = string.Join(",", Enumerable.Range(0, 9).Select(i =>
            $"{{\"Id\":\"e{i}\",\"Name\":\"E{i}\",\"Hp\":100,\"MaxHp\":100,\"Block\":0}}"));
        var candidates = string.Join(",", Enumerable.Range(0, 9).Select(i =>
            $"{{\"Target\":\"e{i}\",\"Amount\":1}}"));
        var strikes = string.Join(",", Enumerable.Range(0, 6).Select(i =>
            $"{{\"Kind\":\"RandomTargetDamage\",\"Target\":\"e0\",\"Source\":\"orb {i}\",\"Amount\":1,\"Attacker\":\"p\",\"TargetOptions\":[{candidates}]}}"));
        var result = RunJson($"{{\"Actors\":[{{\"Id\":\"p\",\"Name\":\"Player\",\"Hp\":20,\"MaxHp\":20,\"Block\":0}},{actors}],\"Events\":[{strikes}],\"Warnings\":[],\"LocalPlayerId\":\"p\",\"EnemyIds\":[{string.Join(",", Enumerable.Range(0, 9).Select(i => $"\"e{i}\""))}]}}");

        Equal(false, Property<bool>(result, "BranchEnumerationComplete"), "The 256-state budget marks truncated exploration.");
        Equal(true, result.IsPartial, "A truncated forecast is partial.");
        Equal(true, Property<bool>(result, "BranchLimitReached"), "The result identifies the exact reason for truncation.");
        Equal(true, (result with { BranchLimitReached = false }).IsPartial,
            "Any incomplete branch enumeration remains partial even without a separate limit flag.");
        Equal("?", OverheadPresentation.Format(result.Local, result.IsPartial,
            Property<bool>(result, "BranchEnumerationComplete")).Text,
            "The compact HUD does not present sampled bounds as exhaustive.");
    }

    private static void DoomChecksCurrentHpAtItsTurnEndPhase()
    {
        EnsureEventKind("DoomCheck");
        var result = RunJson("""
        {
          "Actors": [
            { "Id": "p1", "Name": "Player 1", "Hp": 5, "MaxHp": 10, "Block": 0, "DoomThreshold": 5 },
            { "Id": "p2", "Name": "Player 2", "Hp": 3, "MaxHp": 10, "Block": 0, "DoomThreshold": 5 }
          ],
          "Events": [
            { "Kind": "Heal", "Target": "p1", "Source": "End-turn heal", "Amount": 1 },
            { "Kind": "DoomCheck", "Target": "p1", "Source": "Doom", "Phase": "After player turn",
              "AffectedActors": ["p1", "p2"], "EligibleOwners": ["p1", "p2"] }
          ],
          "Warnings": [], "LocalPlayerId": "p1"
        }
        """);
        Equal(6, result.Actors["p1"].FinalHp, "Doom sees the HP after preceding end-turn effects.");
        Equal(0, result.Actors["p2"].FinalHp, "One eligible Doom owner triggers kills for all doomed teammates.");

        var notParticipant = RunJson("""
        {
          "Actors": [{ "Id": "p", "Name": "Player", "Hp": 3, "MaxHp": 10, "Block": 0, "DoomThreshold": 5 }],
          "Events": [{ "Kind": "DoomCheck", "Target": "p", "Source": "Doom", "AffectedActors": ["p"], "EligibleOwners": [] }],
          "Warnings": [], "LocalPlayerId": "p"
        }
        """);
        Equal(3, notParticipant.Local.FinalHp, "Doom does not resolve for an owner outside the native turn participants.");
    }

    private static void DoomSkipsDeadOwnersBeforeSelectingTheFirstDoomedCreature()
    {
        var result = RunJson("""
        {
          "Actors": [
            { "Id": "dead", "Name": "Poisoned enemy", "Hp": 1, "MaxHp": 8, "Block": 0, "DoomThreshold": 5 },
            { "Id": "live", "Name": "Doomed enemy", "Hp": 4, "MaxHp": 8, "Block": 0, "DoomThreshold": 5 },
            { "Id": "p", "Name": "Player", "Hp": 10, "MaxHp": 10, "Block": 0 }
          ],
          "Events": [
            { "Kind": "Damage", "Target": "dead", "Source": "Poison", "Amount": 1, "BypassesBlock": true },
            { "Kind": "DoomCheck", "Target": "live", "Source": "Doom", "Phase": "Enemy turn end",
              "AffectedActors": ["dead", "live"], "EligibleOwners": ["dead", "live"] }
          ],
          "Warnings": [], "LocalPlayerId": "p", "EnemyIds": ["dead", "live"]
        }
        """);

        Equal(0, result.Actors["dead"].FinalHp, "The first actor has already died to poison.");
        Equal(0, result.Actors["live"].FinalHp, "The dead owner's removed Doom power does not block the next living owner.");
    }

    private static void DoomCallbackMustBelongToTheFirstLivingDoomedOwner()
    {
        var result = RunJson("""
        {
          "Actors": [
            { "Id": "p1", "Name": "Player 1", "Hp": 3, "MaxHp": 10, "Block": 0,
              "IsPlayer": true, "DoomThreshold": 5 },
            { "Id": "p2", "Name": "Player 2", "Hp": 4, "MaxHp": 10, "Block": 0,
              "IsPlayer": true, "DoomThreshold": 5 },
            { "Id": "e", "Name": "Enemy", "Hp": 10, "MaxHp": 10, "Block": 0 }
          ],
          "Events": [
            { "Kind": "DoomCheck", "Target": "p1", "Source": "Doom p2", "AffectedActors": ["p1", "p2"],
              "EligibleOwners": ["p2"] },
            { "Kind": "DoomCheck", "Target": "p1", "Source": "Doom p1", "AffectedActors": ["p1", "p2"],
              "EligibleOwners": ["p1"] }
          ],
          "Warnings": [], "LocalPlayerId": "p1", "EnemyIds": ["e"]
        }
        """);

        Equal(0, result.Actors["p1"].FinalHp, "The later callback for the first doomed owner's power starts Doom.");
        Equal(0, result.Actors["p2"].FinalHp, "The first eligible Doom callback kills all doomed allies.");
        Equal("Doom p1", result.Local.FirstLethalSource, "Doom resolves at the matching listener's place in hook order.");
    }

    private static void DoomDoesNotRunAfterCombatHasEnded()
    {
        var result = RunJson("""
        {
          "Actors": [
            { "Id": "p", "Name": "Player", "Hp": 3, "MaxHp": 10, "Block": 0,
              "IsPlayer": true, "DoomThreshold": 5 },
            { "Id": "e", "Name": "Enemy", "Hp": 0, "MaxHp": 10, "Block": 0 }
          ],
          "Events": [{ "Kind": "DoomCheck", "Target": "p", "Source": "Doom", "AffectedActors": ["p"],
            "EligibleOwners": ["p"], "StopWhenCombatOver": true }],
          "Warnings": [], "LocalPlayerId": "p", "EnemyIds": ["e"]
        }
        """);

        Equal(3, result.Local.FinalHp, "Native Doom callbacks stop once combat has ended.");
    }

    private static ForecastResult RunJson(string json)
    {
        return ForecastSimulator.Run(JsonSerializer.Deserialize<ForecastInput>(json, Json)
            ?? throw new Exception("Forecast JSON did not deserialize."));
    }

    private static void EnsureEventKind(string name)
    {
        if (!Enum.GetNames<ForecastEventKind>().Contains(name, StringComparer.Ordinal))
            throw new Exception("ForecastSimulator does not implement " + name + ".");
    }

    private static T Property<T>(object instance, string name)
    {
        var property = instance.GetType().GetProperty(name)
            ?? throw new Exception("Forecast result is missing " + name + ".");
        return (T)(property.GetValue(instance) ?? throw new Exception(name + " was null."));
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!Equals(expected, actual)) throw new Exception(label + $": expected {expected}, got {actual}");
    }
}
