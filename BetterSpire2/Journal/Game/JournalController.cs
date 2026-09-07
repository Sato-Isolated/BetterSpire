#nullable enable
using System;
using BetterSpire2.Journal.Core;
using BetterSpire2.Journal.Game;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace BetterSpire2.Journal.Game;

/// <summary>UI commands and patch error boundary. Closing the journal never clears observations.</summary>
internal static class JournalController
{
    public static void OnCombatSetUp() => Safe(JournalService.OnCombatSetUp);
    public static void Refresh() => Safe(JournalService.Refresh);
    public static void ToggleJournal() => Safe(JournalService.Toggle);
    public static void ExportJournal() => Safe(JournalService.Export);
    public static void Hide() => JournalService.Window.Close();
    public static void BeforeCombatReset() => Safe(JournalService.BeforeReset);
    public static void OnCombatEnding(bool lost) => Safe(() => JournalService.OnEnding(lost));
    public static bool IsJournalVisible => JournalService.Window.IsVisible && !JournalService.NativeUiBlocking;
    public static bool ShouldTrackBlockChanges(Creature? creature) => JournalService.CanTrack(creature);
    public static void RecordEnergyGained(Creature creature, int amount) => Safe(() => JournalService.Record(creature, Stat.EnergyGained, amount));
    public static void RecordBlockLost(Creature creature, int amount) => Safe(() => JournalService.Record(creature, Stat.BlockRemoved, amount));
    public static void RecordBlockCleared(Creature creature, int amount) => Safe(() => JournalService.Record(creature, Stat.BlockExpired, amount));
    private static void Safe(Action action)
    { try { action(); } catch (Exception ex) { ModLog.Error("Journal.Command", ex); } }
    internal static bool IsPointInWindow(Godot.Vector2 point) => JournalService.Window.Contains(point);

}
