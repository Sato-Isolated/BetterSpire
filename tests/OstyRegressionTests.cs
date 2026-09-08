using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BetterSpire2.Guardian.Core;

internal static class OstyRegressionTests
{
    private static int _checks;
    private static void Equal<T>(T expected, T actual, string label)
    { _checks++; if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{label}: expected {expected}, got {actual}"); }
    private static ForecastResult Check(string name, int petHp, HpRule[] rules, decimal[] hits,
        int playerLoss, int petLoss, int block = 0, HpRule[]? petRules = null, decimal previous = 0,
        int playerHp = 50, int revive = 0, bool powered = true)
    {
        var input = new ForecastInput(
            new[] { new ActorSnapshot("p", "Player", playerHp, 100, block,
                RedirectPoweredAttacksTo: "pet", HpRules: rules, DamageAlreadyReceived: previous,
                ReviveHp: revive, IsPlayer: true),
                new ActorSnapshot("pet", "Osty", petHp, 100, 0, SharedBlockOwner: "p", HpRules: petRules) },
            hits.Select((hit, i) => new ForecastEvent(ForecastEventKind.Damage, "p", "hit " + i, hit, PoweredAttack: powered)).ToArray(),
            Array.Empty<string>(), "p");
        string before = JsonSerializer.Serialize(input);
        var result = ForecastSimulator.Run(input);
        Equal(playerLoss, result.Local.HpLost, name + ": player loss");
        Equal(petLoss, result.Actors["pet"].HpLost, name + ": pet loss");
        Equal(playerHp - playerLoss + result.Local.Healed, result.Local.FinalHp, name + ": player HP conservation");
        Equal(petHp - petLoss, result.Actors["pet"].FinalHp, name + ": pet HP conservation");
        Equal(before, JsonSerializer.Serialize(input), name + ": immutable input");
        Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(ForecastSimulator.Run(input)), name + ": deterministic");
        foreach (var actor in result.Actors.Values)
            Equal(actor.HpLost, result.Steps.Where(s => s.Target == actor.Id).Sum(s => s.HpLost), name + ": per-receiver steps");
        return result;
    }
    internal static void Run()
    {
        HpRule Limit(int value) => new(HpRuleKind.TurnLimit, value);
        HpRule Slip(int charges = 1) => new(HpRuleKind.Slippery, 1, charges);
        HpRule Buffer() => new(HpRuleKind.Buffer, 0, 1);
        // Expected values derive from v111 CreatureCmd.Damage's per-result reaction loop
        // and the inspected BeatingRemnant/Slippery hooks, NOT the old simulator comment.
        Check("Osty does not consume Beating Remnant", 50, [Limit(20)], [20, 40], 10, 50);
        Check("Osty does not consume player's Slippery", 10, [Slip()], [5, 10], 1, 10);
        Check("overflow uses personal turn budget", 4, [Limit(10)], [10, 10], 10, 4, playerHp: 30);
        Check("already-spent personal budget", 4, [Limit(20)], [10, 10], 5, 4, previous: 15);
        Check("fully intercepted attacks", 50, [Limit(20), Slip()], [10, 10], 0, 20);
        Check("fully blocked then redirected", 4, [], [10, 10], 6, 4, block: 10);
        Equal(10, Check("shared block consumed once", 5, [], [10, 10], 5, 5, block: 10).Local.BlockSpent, "shared block owner");
        Check("pet Buffer keeps player Slippery", 4, [Slip()], [5, 10], 1, 4, petRules: [Buffer()]);
        Check("pet Slippery is decremented on pet", 10, [], [5, 10], 1, 10, petRules: [Slip()]);
        Check("pet turn limit is updated on pet", 10, [], [5, 5], 0, 3, petRules: [Limit(3)]);
        Equal(1, Check("player Buffer consumed only by overflow", 5, [Buffer()], [3, 5, 5], 5, 5).Local.BuffersUsed, "one player Buffer");
        Equal(0, Check("zero overflow keeps player Buffer", 10, [Buffer()], [0, 5, 5], 0, 10).Local.BuffersUsed, "zero result keeps Buffer");
        Check("next hit after pet death", 5, [Slip()], [5, 5, 5], 6, 5);
        Check("two charges apply to two player losses", 5, [Slip(2)], [3, 5, 5, 5], 7, 5);
        Check("unpowered damage bypasses redirection", 10, [Slip()], [5, 10], 11, 0, powered: false);
        Check("dead pet cannot receive redirected hit", 0, [Slip()], [5, 10], 11, 0);
        Check("dead player receives no later damage", 1, [], [10, 10], 2, 1, playerHp: 2);
        Equal(9, Check("revived receiver keeps correct personal budget", 1, [Limit(3)], [10, 10], 3, 1,
            playerHp: 2, revive: 10).Local.FinalHp, "revival then remaining budget");
        Check("fractional loss does not consume a charge", 2, [Slip()], [0.5m, 2.5m, 4], 1, 2);

        // Independent integer oracle, no calls to production helper methods.
        // Only player's one Slippery charge or 20-loss limit; pet has no modifiers.
        int cases = 0;
        for (int pet = 0; pet <= 16; pet++)
        for (int block = 0; block <= 6; block += 3)
        for (int a = 0; a <= 20; a += 2)
        for (int b = 0; b <= 20; b += 2)
        foreach (bool slippery in new[] { false, true })
        {
            int shield = block, petRemaining = pet, total = 0, charges = 1;
            foreach (int hit in new[] { a, b })
            {
                int absorbed = Math.Min(shield, hit); shield -= absorbed;
                int remainder = hit - absorbed;
                int petLost = Math.Min(petRemaining, remainder);
                petRemaining -= petLost; remainder -= petLost;
                int lost = slippery ? (charges > 0 ? Math.Min(1, remainder) : remainder)
                    : Math.Min(remainder, Math.Max(0, 20 - total));
                total += lost;
                if (lost > 0 && slippery && charges > 0) charges--;
            }
            var input = new ForecastInput(
                [new("p", "P", 100, 100, block, RedirectPoweredAttacksTo: "pet", HpRules: [slippery ? Slip() : Limit(20)]),
                 new("pet", "Pet", pet, 100, 0, SharedBlockOwner: "p")],
                [new(ForecastEventKind.Damage, "p", "a", a, PoweredAttack: true),
                 new(ForecastEventKind.Damage, "p", "b", b, PoweredAttack: true)], [], "p");
            var actual = ForecastSimulator.Run(input);
            Equal(total, actual.Local.HpLost, $"oracle pet={pet} block={block} a={a} b={b} slippery={slippery}");
            Equal(petRemaining, actual.Actors["pet"].FinalHp, "oracle pet result");
            Equal(shield, actual.Local.FinalBlock, "oracle shared block");
            cases++;
        }
        Equal(true, ForecastHookPolicy.IsOutsideForecastWindow("BurningBlood", "AfterCombatVictory"), "audited post-victory heal");
        Equal(false, ForecastHookPolicy.IsOutsideForecastWindow("BurningBlood", "AfterDamageReceived"), "no model-wide exemption");
        Equal(false, ForecastHookPolicy.IsOutsideForecastWindow("UnknownRelic", "AfterCombatVictory"), "unknown victory callback still uncertain");
        Equal(true, ForecastHookPolicy.IsReaction("AfterNewFutureEffect"), "future effects remain uncertain");
        Console.WriteLine($"PASS {_checks} Osty/phase checks, including {cases} independent integer scenarios (not an in-game run).");
    }
}
