// Test doubles for the event boundary. The source-linked mod classes above are
// production code; these doubles do not claim to reproduce the game's engine.
global using BetterSpire2.Core;
global using BetterSpire2.Infrastructure;
global using BetterSpire2.Trackers;
global using BetterSpire2.HandViewer;
global using BetterSpire2.UI;
global using BetterSpire2.Journal.Game;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace MegaCrit.Sts2.Core.TestSupport { public static class TestMode { public static bool IsOn; } }
namespace MegaCrit.Sts2.Core.Rooms { public sealed class CombatRoom { } }
namespace MegaCrit.Sts2.Core.Runs
{
    public sealed class RunState { public object? CurrentRoom { get; set; } }
}
namespace MegaCrit.Sts2.Core.Combat.History
{
    public sealed class CombatHistory { public event Action? Changed; public void Change() => Changed?.Invoke(); }
}
namespace MegaCrit.Sts2.Core.Combat
{
    internal sealed class CombatTurnState { }
    public readonly record struct CombatId(int Value);
    public interface ICombatState { }
    public sealed class CombatState : ICombatState
    {
        public List<Creature> Creatures { get; } = new();
        public List<Player> Players { get; } = new();
        public Runs.RunState RunState { get; } = new();
        public event Action<ICombatState>? CreaturesChanged;
        public void ChangeCreatures() => CreaturesChanged?.Invoke(this);
    }
    public sealed class CombatStateTracker
    {
        public event Action<CombatState>? CombatStateChanged;
        public int SubscriberCount => CombatStateChanged?.GetInvocationList().Length ?? 0;
        public void Publish(CombatState state) => CombatStateChanged?.Invoke(state);
    }
    public sealed class CombatManager
    {
        internal Task EndCombatInternal() => EndCombatInternal(new CombatTurnState());
        private Task EndCombatInternal(CombatTurnState state) => Task.CompletedTask;
        public static CombatManager Instance { get; set; } = new();
        public CombatId? CurrentCombatId { get; set; }
        public bool IsStarting { get; set; }
        public bool IsInProgress { get; set; }
        public bool IsOverOrEnding { get; set; }
        public CombatState? State;
        public int DebugReads;
        public CombatStateTracker StateTracker { get; } = new();
        public History.CombatHistory History { get; } = new();
        public event Action<CombatState>? CombatSetUp;
        public event Action<CombatState>? CombatBegan;
        public event Action<Rooms.CombatRoom>? CombatEnded;
        public event Action<Player, bool>? PlayerEndedTurn;
        public event Action<Player>? PlayerUnendedTurn;
        public int SetupSubscribers => CombatSetUp?.GetInvocationList().Length ?? 0;
        public int ReadySubscribers => PlayerEndedTurn?.GetInvocationList().Length ?? 0;
        public int UndoSubscribers => PlayerUnendedTurn?.GetInvocationList().Length ?? 0;
        public bool IsCurrentLiveCombat(CombatId? id) => id != null && id == CurrentCombatId && IsInProgress;
        public CombatState? DebugOnlyGetState() { DebugReads++; return State; }
        public void SetUp(CombatState state, int id)
        { State = state; CurrentCombatId = new(id); IsStarting = true; IsInProgress = false; IsOverOrEnding = false; CombatSetUp?.Invoke(state); }
        public void Begin() { IsStarting = false; IsInProgress = true; CombatBegan?.Invoke(State!); }
        public void End(Rooms.CombatRoom room) { IsInProgress = false; IsOverOrEnding = true; CombatEnded?.Invoke(room); }
        public void PublishOldEnd(Rooms.CombatRoom room) => CombatEnded?.Invoke(room);
        public void Ready(Player player) => PlayerEndedTurn?.Invoke(player, true);
        public void Undo(Player player) => PlayerUnendedTurn?.Invoke(player);
    }
    public sealed class PlayerCombatState
    {
        public event Action? PlayerTurnPhaseChanged;
        public event Action<int, int>? EnergyChanged;
        public event Action<int, int>? StarsChanged;
        public Entities.Cards.CardPile Hand { get; } = new();
        public IEnumerable<Entities.Cards.CardPile> AllPiles => new[] { Hand };
    }
}
namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    public sealed class Creature
    {
        public MonsterModel? Monster { get; set; }
        public event Action<int, int>? CurrentHpChanged;
        public event Action<int, int>? MaxHpChanged;
        public event Action<int, int>? BlockChanged;
        public event Action<PowerModel>? PowerApplied;
        public event Action<PowerModel>? PowerRemoved;
        public event Action<PowerModel, int, bool>? PowerIncreased;
        public event Action<PowerModel, bool>? PowerDecreased;
        public void ChangeHp(int before, int after) => CurrentHpChanged?.Invoke(before, after);
    }
}
namespace MegaCrit.Sts2.Core.Entities.Players
{
    public sealed class Player
    {
        public Combat.PlayerCombatState? PlayerCombatState { get; set; } = new();
        public List<RelicModel> Relics { get; } = new();
        public event Action<RelicModel>? RelicObtained;
        public event Action<RelicModel>? RelicRemoved;
    }
}
namespace MegaCrit.Sts2.Core.Entities.Cards
{
    public sealed class CardPile
    {
        public List<CardModel> Cards { get; } = new();
        public event Action? ContentsChanged;
        public event Action<CardModel>? CardAdded;
        public event Action<CardModel>? CardRemoved;
    }
}
namespace MegaCrit.Sts2.Core.Models
{
    public sealed class MonsterModel { public object? NextMove { get; set; } }
    public sealed class PowerModel { }
    public sealed class RelicModel
    {
        public event Action? DisplayAmountChanged;
        public event Action? StatusChanged;
        public void ChangeStatus() => StatusChanged?.Invoke();
    }
    public sealed class CardModel
    {
        public event Action? Upgraded;
        public event Action? EnchantmentChanged;
        public event Action? AfflictionChanged;
        public event Action? KeywordsChanged;
        public event Action? EnergyCostChanged;
        public event Action? StarCostChanged;
        public event Action? ReplayCountChanged;
        public event Action? Forged;
        public void ChangeCost() => EnergyCostChanged?.Invoke();
        public void Forge() => Forged?.Invoke();
    }
}
namespace BetterSpire2.Core { internal static class ModLog { internal static int Errors; internal static void Error(string _, Exception ex) => Errors++; } }
namespace BetterSpire2.Infrastructure
{
    internal static class InstantSpeedHelper
    {
        internal static int Starts, Ends;
        internal static bool ThrowOnStart;
        internal static void OnCombatStart() { Starts++; if (ThrowOnStart) throw new Exception("test setup failure"); }
        internal static void OnCombatEnd() => Ends++;
    }
}
namespace BetterSpire2.Trackers
{
    internal static class DamageTracker
    {
        internal static int Setups, Hides;
        internal static void OnCombatSetUp() { Setups++; Runtime.CombatLifecycle.EnsureAttached(); }
        internal static void Hide() => Hides++;
    }
}
namespace BetterSpire2.HandViewer
{
    internal static class TeammateHandViewer { internal static void OnCombatSetUp() { } internal static void Hide() { } }
}
namespace BetterSpire2.UI { internal static class ClockDisplay { internal static void SyncVisibility() { } } }
namespace BetterSpire2.Journal.Game
{
    internal static class JournalController
    {
        internal static int Setups, Endings;
        internal static void OnCombatSetUp() => Setups++;
        internal static void OnCombatEnding(bool lost) => Endings++;
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyTargetMethod : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
    public static class AccessTools
    {
        public static MethodInfo? DeclaredMethod(Type type, string name, Type[] parameters) =>
            type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                null, parameters, null);
    }
}

namespace BetterSpire2.Runtime.Native
{
    internal static class NativeCombatHooks
    {
        internal static event Action<MegaCrit.Sts2.Core.Combat.CombatState>? StateChanged;
        internal static int Subscribers => StateChanged?.GetInvocationList().Length ?? 0;
        internal static void Publish(MegaCrit.Sts2.Core.Combat.CombatState state) => StateChanged?.Invoke(state);
        internal static void ForgetCombat() { }
    }
}
