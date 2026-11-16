#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using BetterSpire2.Guardian.Core;

internal static class WuppyRegressionTests
{
    private static int _checks;
    private static void Equal<T>(T expected, T actual, string label)
    {
        _checks++;
        if (!Equals(expected, actual)) throw new Exception(label + $": expected {expected}, got {actual}");
    }

    private static ActorSnapshot[] Actors(int wuppyHp, int block, int ostyHp = 0) =>
    [
        new("p", "Player", 100, 100, block, RedirectPoweredAttacksTo: ostyHp > 0 ? "o" : null,
            IsPlayer: true) { RedirectPoweredAttacksBeforeBlockTo = "w" },
        new("w", "Wuppy", wuppyHp, 60, 0, SharedBlockOwner: "p"),
        new("o", "Osty", ostyHp, 60, 0, SharedBlockOwner: "p"),
        new("e", "Enemy", 100, 100, 0)
    ];

    private static ForecastEvent Hit(decimal amount, string target = "p", bool powered = true, bool unblockable = false) =>
        new(ForecastEventKind.Damage, target, "Hit", amount, BypassesBlock: unblockable,
            PoweredAttack: powered, Attacker: "e", RequireLivingAttacker: true, TriggersThorns: powered);

    private static ForecastResult Run(ActorSnapshot[] actors, params ForecastEvent[] events) =>
        ForecastSimulator.Run(new(actors, events, [], "p", ["e"]));

    internal static void Run()
    {
        var result = Run(Actors(8, 9), Hit(5));
        Equal(3, result.Actors["w"].FinalHp, "Wuppy absorbs damage before block");
        Equal(9, result.Local.FinalBlock, "a surviving Wuppy preserves all owner block");
        Equal(0, result.Local.HpLost, "a surviving Wuppy protects owner HP");
        Equal(false, result.IsPartial, "supported Wuppy damage needs no uncertainty warning");

        result = Run(Actors(3, 5), Hit(6));
        Equal(0, result.Actors["w"].FinalHp, "lethal Wuppy damage consumes its remaining HP");
        Equal(2, result.Local.FinalBlock, "only Wuppy overkill consumes block");
        Equal(0, result.Local.HpLost, "block absorbs the Wuppy overkill");

        result = Run(Actors(3, 2), Hit(8));
        Equal(3, result.Local.HpLost, "overkill passes through remaining block to player HP");
        result = Run(Actors(3, 2, 4), Hit(11));
        Equal(3, result.Actors["w"].HpLost, "Wuppy is first in the redirect chain");
        Equal(4, result.Actors["o"].HpLost, "Osty receives damage after Wuppy and block");
        Equal(2, result.Local.HpLost, "player receives only final Osty overkill");

        result = Run(Actors(7, 3), Hit(5), Hit(5), Hit(5));
        Equal(7, result.Actors["w"].HpLost, "multi-hit attacks cannot spend Wuppy HP twice");
        Equal(5, result.Local.HpLost, "later hits skip a dead Wuppy");
        Equal(0, result.Local.FinalBlock, "block continues across hits after Wuppy dies");

        result = Run(Actors(7, 3, 8), Hit(5, powered: false));
        Equal(7, result.Actors["w"].FinalHp, "unpowered damage bypasses Wuppy");
        Equal(8, result.Actors["o"].FinalHp, "unpowered damage bypasses Osty");
        Equal(2, result.Local.HpLost, "unpowered blockable damage still consumes block");
        result = Run(Actors(3, 9), Hit(5, unblockable: true));
        Equal(0, result.Actors["w"].FinalHp, "powered unblockable damage still reaches Wuppy");
        Equal(9, result.Local.FinalBlock, "unblockable overkill leaves block intact");
        Equal(2, result.Local.HpLost, "unblockable overkill reaches player HP");

        result = Run(Actors(3, 5), Hit(7, "w"));
        Equal(1, result.Actors["w"].FinalHp, "direct Wuppy hit uses owner block before pet HP");
        Equal(0, result.Local.FinalBlock, "direct Wuppy hit spends owner block");
        Equal(0, result.Local.HpLost, "direct Wuppy hit does not overflow to owner");
        result = Run(Actors(3, 1, 4), Hit(10, "w"));
        Equal(0, result.Local.HpLost, "lethal direct Wuppy hit does not overflow to owner");
        Equal(4, result.Actors["o"].FinalHp, "direct Wuppy hit does not redirect to owner's Osty");
        result = Run(Actors(3, 5), Hit(.8m));
        Equal(3, result.Actors["w"].FinalHp, "native Wuppy HP loss truncates fractional damage");
        Equal(5, result.Local.FinalBlock, "fractional damage absorbed by Wuppy never reaches block");
        result = Run(Actors(3, 5), Hit(4.8m));
        Equal(4, result.Local.FinalBlock, "only integer Wuppy overkill reaches block");
        result = Run(Actors(8, 5), Hit(10) with { DamageCap = 2 });
        Equal(6, result.Actors["w"].FinalHp, "native damage caps apply before Wuppy redirection");

        var actors = Actors(8, 0);
        actors[0] = actors[0] with { HpRules = [new(HpRuleKind.Reduction, 2), new(HpRuleKind.Buffer, 0, 1)] };
        actors[1] = actors[1] with { HpRules = [new(HpRuleKind.Cap, 1), new(HpRuleKind.Buffer, 0, 1)] };
        result = Run(actors, Hit(5));
        Equal(3, result.Actors["w"].FinalHp, "pre-block Wuppy damage bypasses AfterOsty HP modifiers");
        Equal(0, result.Actors["w"].BuffersUsed, "redirected Wuppy damage does not consume its Buffer");
        Equal(0, result.Local.BuffersUsed, "Wuppy absorption preserves owner Buffer");
        result = Run(actors, Hit(5, "w"));
        Equal(8, result.Actors["w"].FinalHp, "direct Wuppy damage applies its normal HP modifiers");
        Equal(1, result.Actors["w"].BuffersUsed, "direct Wuppy damage consumes its own Buffer");

        actors = Actors(3, 0);
        actors[0] = actors[0] with { HpRules = [new(HpRuleKind.Reduction, 1), new(HpRuleKind.Buffer, 0, 1)] };
        result = Run(actors, Hit(8), Hit(8));
        Equal(7, result.Local.HpLost, "owner HP rules apply to Wuppy overflow and subsequent hits");
        Equal(1, result.Local.BuffersUsed, "owner Buffer is consumed once by positive overflow");
        actors = Actors(3, 0, 3);
        actors[0] = actors[0] with { HpRules = [new(HpRuleKind.TurnLimit, 3)] };
        result = Run(actors, Hit(10), Hit(10));
        Equal(3, result.Local.HpLost, "Wuppy and Osty HP loss do not spend owner's turn limit");
        actors = Actors(20, 0);
        actors[1] = actors[1] with { HpRules = [new(HpRuleKind.Slippery, 1, 1)] };
        result = Run(actors, Hit(5), Hit(5, "w"));
        Equal(10, result.Actors["w"].FinalHp, "redirected Wuppy loss updates its own Slippery charge before a direct hit");

        actors = Actors(20, 0);
        actors[0] = actors[0] with { DamageReactions = [new(DamageReactionKind.FlameBarrier, 3, "Owner Flame")] };
        actors[1] = actors[1] with { DamageReactions = [new(DamageReactionKind.Thorns, 9, "Pet Thorns"),
            new(DamageReactionKind.FlameBarrier, 4, "Pet Flame")] };
        result = Run(actors, Hit(5));
        Equal(93, result.Actors["e"].FinalHp, "both living receiver Flame Barriers react, redirected pet Thorns does not");
        actors[1] = actors[1] with { Hp = 5 };
        result = Run(actors, Hit(5));
        Equal(97, result.Actors["e"].FinalHp, "dead Wuppy cannot retaliate but its surviving owner can");

        actors = Actors(20, 0);
        actors[0] = actors[0] with { DamageReactions = [new(DamageReactionKind.Thorns, 4, "Owner Thorns")] };
        actors[3] = actors[3] with { Hp = 3 };
        result = Run(actors, Hit(5), Hit(5));
        Equal(15, result.Actors["w"].FinalHp, "engaged hit lands on Wuppy after lethal owner Thorns; next hit stops");

        actors = Actors(4, 1).Append(new ActorSnapshot("ally", "Ally", 50, 50, 0, IsPlayer: true)).ToArray();
        result = Run(actors, new ForecastEvent(ForecastEventKind.DamageCommand, "p", "AOE", 6,
            PoweredAttack: true, Attacker: "e", TargetOptions: [new("p", 6), new("ally", 6)]));
        Equal(1, result.Local.HpLost, "AOE respects owner's Wuppy then block");
        Equal(6, result.Actors["ally"].HpLost, "Wuppy does not protect another player");

        result = Run(Actors(3, 3), Hit(4, "w", powered: false, unblockable: true), Hit(5));
        Equal(2, result.Local.HpLost, "Wuppy killed by an earlier indirect effect cannot redirect later attacks");
        actors = Actors(20, 0);
        actors[0] = actors[0] with { DoomThreshold = 95 };
        result = Run(actors, Hit(6), new ForecastEvent(ForecastEventKind.DoomCheck, "p", "Doom", 0,
            AffectedActors: ["p"], EligibleOwners: ["p"], StopWhenCombatOver: true));
        Equal(100, result.Local.FinalHp, "Doom uses protected player HP after Wuppy absorbs the attack");

        var exported = new ForecastInput(Actors(8, 9), [Hit(5)], [], "p", ["e"]);
        var restored = JsonSerializer.Deserialize<ForecastInput>(JsonSerializer.Serialize(exported))!;
        Equal("w", restored.Actors[0].RedirectPoweredAttacksBeforeBlockTo, "forecast JSON retains Wuppy redirect");
        Equal(0, ForecastSimulator.Run(restored).Local.HpLost, "exported Wuppy scenario reproduces the forecast");

        int cases = 0;
        for (int wuppy = 0; wuppy <= 10; wuppy += 2)
        for (int block = 0; block <= 8; block += 2)
        for (int osty = 0; osty <= 6; osty += 3)
        for (int a = 0; a <= 14; a += 2)
        for (int b = 0; b <= 14; b += 2)
        {
            // Independent closed-form oracle: integer hits exhaust the three shields in order.
            int total = a + b;
            result = Run(Actors(wuppy, block, osty), Hit(a), Hit(b));
            Equal(Math.Max(0, wuppy - total), result.Actors["w"].FinalHp, "oracle Wuppy HP");
            Equal(Math.Max(0, block - Math.Max(0, total - wuppy)), result.Local.FinalBlock, "oracle owner block");
            Equal(Math.Max(0, osty - Math.Max(0, total - wuppy - block)), result.Actors["o"].FinalHp, "oracle Osty HP");
            Equal(Math.Max(0, total - wuppy - block - osty), result.Local.HpLost, "oracle owner HP loss");
            cases++;
        }
        Console.WriteLine($"PASS {_checks} Wuppy forecast checks, including {cases} independent shield-chain scenarios.");
    }
}
