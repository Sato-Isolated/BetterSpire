using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterSpire2.Guardian.Core;

internal static class Program
{
    private sealed record Fixture(string Name, ForecastInput Input,
        Dictionary<string, Dictionary<string, JsonElement>> Expected);
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static int Main()
    {
        int passed = 0, failed = 0;
        string file = Path.Combine(AppContext.BaseDirectory, "fixtures.json");
        var fixtures = JsonSerializer.Deserialize<List<Fixture>>(File.ReadAllText(file), Json)
            ?? throw new InvalidDataException("Empty fixtures.");
        foreach (var fixture in fixtures)
        {
            try
            {
                string snapshot = JsonSerializer.Serialize(fixture.Input, Json);
                var result = ForecastSimulator.Run(fixture.Input);
                foreach (var actor in fixture.Expected)
                foreach (var expectation in actor.Value)
                {
                    var property = typeof(ActorForecast).GetProperty(expectation.Key)
                        ?? throw new InvalidDataException("Unknown expectation: " + expectation.Key);
                    object? observed = property.GetValue(result.Actors[actor.Key]);
                    object? expected = expectation.Value.Deserialize(property.PropertyType, Json);
                    Equal(expected, observed, actor.Key + "." + expectation.Key);
                }
                Equal(snapshot, JsonSerializer.Serialize(fixture.Input, Json), "Input immutability");
                Equal(JsonSerializer.Serialize(result, Json),
                    JsonSerializer.Serialize(ForecastSimulator.Run(fixture.Input), Json), "Determinism");
                Equal(fixture.Input.Warnings.Count > 0, result.IsPartial, "Uncertainty preserved");
                foreach (var actor in result.Actors.Values)
                {
                    Equal(actor.StartHp - actor.HpLost + actor.Healed, actor.FinalHp, "HP conservation");
                    if (actor.FinalHp < 0 || actor.FinalBlock < 0) throw new Exception("Negative state.");
                    if (!fixture.Input.Events.Any(e => e.Kind == ForecastEventKind.ClearBlock))
                        Equal(actor.StartBlock + actor.BlockGained - actor.BlockSpent, actor.FinalBlock, "Block conservation");
                }
                Console.WriteLine("PASS " + fixture.Name);
                passed++;
            }
            catch (Exception ex) { Console.WriteLine("FAIL " + fixture.Name + ": " + ex.Message); failed++; }
        }
        try { IntegerDamagePropertyTests(); passed++; Console.WriteLine("PASS 4096 integer-model comparisons"); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL integer properties: " + ex.Message); }
        try { InvalidInputTests(); passed++; Console.WriteLine("PASS invalid-input guards"); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL guards: " + ex.Message); }
        try { OverheadTests.Run(); passed++; Console.WriteLine("PASS overhead formatting, per-actor values and geometry"); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL overhead: " + ex.Message); }
        try { OstyRegressionTests.Run(); passed++; Console.WriteLine("PASS Osty receiver-scoped reactions and forecast phase policy"); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL Osty regression: " + ex.Message); }
        Console.WriteLine($"{passed} groups passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void IntegerDamagePropertyTests()
    {
        for (int initialBlock = 0; initialBlock < 16; initialBlock++)
        for (int a = 0; a < 16; a++)
        for (int b = 0; b < 16; b++)
        {
            int block = initialBlock, loss = 0;
            foreach (int hit in new[] { a, b })
            {
                int absorbed = Math.Min(block, hit);
                block -= absorbed;
                loss += Math.Max(0, hit - absorbed - 1);
            }
            var input = new ForecastInput(
                new[] { new ActorSnapshot("p", "P", 100, 100, initialBlock,
                    HpRules: new[] { new HpRule(HpRuleKind.Reduction, 1) }) },
                new[] { new ForecastEvent(ForecastEventKind.Damage, "p", "A", a),
                    new ForecastEvent(ForecastEventKind.Damage, "p", "B", b) },
                Array.Empty<string>(), "p");
            var result = ForecastSimulator.Run(input).Local;
            Equal(loss, result.HpLost, "Independent integer arithmetic");
            Equal(block, result.FinalBlock, "Independent block arithmetic");
        }
    }

    private static void InvalidInputTests()
    {
        var p = new ActorSnapshot("p", "P", 10, 10, 0);
        void MustThrow(ForecastInput input)
        {
            try { ForecastSimulator.Run(input); }
            catch (ArgumentException) { return; }
            throw new Exception("Expected an input validation exception.");
        }
        MustThrow(new(Array.Empty<ActorSnapshot>(), Array.Empty<ForecastEvent>(), Array.Empty<string>(), "p"));
        MustThrow(new(new[] { p, p }, Array.Empty<ForecastEvent>(), Array.Empty<string>(), "p"));
        MustThrow(new(new[] { p }, new[] { new ForecastEvent(ForecastEventKind.Damage, "missing", "A", 1) }, Array.Empty<string>(), "p"));
        MustThrow(new(new[] { p }, Enumerable.Repeat(new ForecastEvent(ForecastEventKind.Damage, "p", "A", 1), 20001).ToArray(), Array.Empty<string>(), "p"));
    }
    private static void Equal(object? expected, object? actual, string label)
    { if (!Equals(expected, actual)) throw new Exception($"{label}: expected {expected}, got {actual}"); }
}
