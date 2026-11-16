#nullable enable
using System.Reflection;

internal static partial class GameApiCompatibilityTests
{
    private static void VerifyRemainingPatchTargets(MetadataAssembly game)
    {
        const string ns = "MegaCrit.Sts2.Core.";
        const string creature = ns + "Entities.Creatures.Creature";
        const string manager = ns + "Combat.CombatManager";
        const string gameNode = ns + "Nodes.NGame";
        const string intentNode = ns + "Nodes.Combat.NIntent";
        const string drawings = ns + "Nodes.Screens.Map.NMapDrawings";
        const string playerState = ns + "Entities.Players.PlayerCombatState";

        game.RequireContract(manager, "LoseCombat", "System.Void", MethodAttributes.Public, false);
        game.RequireContract(manager, "Reset", "System.Void", MethodAttributes.Public, false, "System.Boolean");
        game.RequireContract(manager, "EndCombatInternal", "System.Threading.Tasks.Task", MethodAttributes.Private,
            false, ns + "Combat.CombatTurnState");
        game.RequireContract(ns + "Combat.History.CombatHistory", "Clear", "System.Void", MethodAttributes.Public, false);
        game.RequireContract(ns + "Runs.RunManager", "CleanUp", "System.Void", MethodAttributes.Public, false, "System.Boolean");
        game.RequireContract(creature, "HealInternal", "System.Void", MethodAttributes.Public, false, "System.Decimal");
        game.RequireContract(creature, "LoseBlockInternal", "System.Void", MethodAttributes.Public, false, "System.Decimal");
        game.RequireContract(creature, "ClearBlock", "System.Threading.Tasks.Task", MethodAttributes.Private, false);
        game.RequireContract(ns + "Hooks.Hook", "ShouldClearBlock", "System.Boolean", MethodAttributes.Public, true,
            ns + "Combat.ICombatState", creature, ns + "Models.AbstractModel&");
        game.RequireContract(playerState, "GainEnergy", "System.Void", MethodAttributes.Public, false, "System.Decimal");
        Check(game.HasField(playerState, "_player"), "energy patch can inject the native player field");

        game.RequireContract(gameNode, "LaunchMainMenu", "System.Threading.Tasks.Task", MethodAttributes.Private, false,
            "System.Boolean");
        game.RequireContract(gameNode, "_Input", "System.Void", MethodAttributes.Public, false, "Godot.InputEvent");
        game.RequireContract(gameNode, "_Ready", "System.Void", MethodAttributes.Public, false);
        game.RequireContract(gameNode, "_ExitTree", "System.Void", MethodAttributes.Public, false);
        game.RequireContract(intentNode, "UpdateVisuals", "System.Void", MethodAttributes.Private, false);
        foreach (string field in new[] { "_intent", "_owner", "_targets", "_valueLabel" })
            Check(game.HasField(intentNode, field), "intent patch can inject native field " + field);

        game.RequireContract(drawings, "HandleDrawingMessage", "System.Void", MethodAttributes.Private, false,
            ns + "Multiplayer.Messages.Game.Flavor.MapDrawingMessage", "System.UInt64");
        game.RequireContract(drawings, "HandleClearMapDrawingsMessage", "System.Void", MethodAttributes.Private, false,
            ns + "Multiplayer.Messages.Game.Flavor.ClearMapDrawingsMessage", "System.UInt64");
        game.RequireContract(drawings, "GetDrawingStateForPlayer", "DrawingState", MethodAttributes.Private, false,
            "System.UInt64");
        game.RequireContract(drawings, "ClearAllLinesForPlayer", "System.Void", MethodAttributes.Private, false,
            "DrawingState");
        game.RequireContract(drawings, "ClearAllLines", "System.Void", MethodAttributes.Public, false);

        foreach (string prefix in new[] { "add_", "remove_" })
        {
            foreach (string signal in new[] { "CombatSetUp", "CombatBegan", "TurnStarted" })
                game.RequireContract(manager, prefix + signal, "System.Void", MethodAttributes.Public, false,
                    "System.Action<" + ns + "Combat.CombatState>");
            game.RequireContract(manager, prefix + "CombatEnded", "System.Void", MethodAttributes.Public, false,
                "System.Action<" + ns + "Rooms.CombatRoom>");
        }
        Check(game.Calls(creature, "ClearBlock", ns + "Hooks.Hook", "ShouldClearBlock", 3),
            "block expiration still passes through the observed native decision");
    }
}
