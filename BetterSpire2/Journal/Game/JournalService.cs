#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterSpire2.DamageMeter;
using BetterSpire2.Journal.Core;
using BetterSpire2.Journal.UI;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace BetterSpire2.Journal.Game;

/// <summary>Collection lifetime is independent from window visibility and all Guardian predictions.</summary>
internal static class JournalService
{
    internal static readonly JournalSession Session = new();
    internal static readonly JournalSelection Selection = new();
    internal static readonly JournalWindow Window = new();
    private static readonly HistoryReader Reader = new();
    private static readonly JournalPlayerNames PlayerNames = new();
    private static JournalArchive? _archive;
    private static readonly JournalCheckpoint Checkpoint = new();
    private static Task<Exception?>? _pendingSave;
    private static long _pendingRevision;
    private static RunState? _runState;
    private static CombatState? _state;
    private static CombatManager? _manager;
    private static CombatHistory? _history;
    private static bool _booted, _dirty, _sealed, _inTick;
    private static CombatOutcome _pendingOutcome = CombatOutcome.Interrupted;
    private static long _savedRevision = -1;
    private static ulong _lastSave, _lastError;
    internal static string StorageWarning { get; private set; } = "";

    private static void Boot()
    {
        if (_booted) return;
        _booted = true;
        _archive = new JournalArchive(Path.Combine(OS.GetUserDataDir(), "betterspire", "journal.json"));
        try
        {
            var saved = _archive.Load();
            if (saved != null) { Session.Attach(saved); Selection.Reset(saved); }
        }
        catch (Exception ex) { StorageWarning = "load"; ModLog.Error("Journal.Load", ex); }
    }
    internal static void BeginRun(RunState state)
    {
        Boot();
        if (ReferenceEquals(_runState, state)) return;
        BeforeReset();
        _runState = state;
        var manager = RunManager.Instance;
        long start = manager?.History?.StartTime ?? 0;
        string seed = state.Rng.StringSeed;
        // StartTime separates a new run from another run played with the same seed.
        // A missing start time deliberately prevents an unsafe cross-session merge.
        string key = start > 0 ? $"{start}:{seed}:{state.GameMode}:{manager?.NetService?.NetId}" : Guid.NewGuid().ToString("N");
        var saved = Session.Run;
        if (saved?.Key != key)
            Session.Attach(new RunJournal { Key = key, Seed = seed, GameStartTime = start, Partial = state.TotalFloor > 1, DamageMeterCoverageVersion = 1 });
        Session.SetRunStatus("ongoing");
        PlayerNames.Reset();
        PlayerNames.Refresh(state, Session, Time.GetTicksMsec(), force: true);
        Selection.Reset(Session.Run);
        Window.Close();
        Save(true);
    }
    internal static void OnCombatSetUp()
    {
        ModRuntime.EnsureStarted();
        // Capture the singleton once: checking a second read does not protect the first.
        var manager = CombatManager.Instance;
        if (manager == null) return;
        var state = manager.DebugOnlyGetState();
        if (state == null || ReferenceEquals(_state, state)) return;
        if (state.RunState is RunState run) BeginRun(run);
        if (Session.Run == null) return;
        Detach();
        _state = state; _manager = manager; _history = manager.History;
        _pendingOutcome = CombatOutcome.Interrupted; _sealed = false; _dirty = true; Reader.Reset();
        var room = state.RunState.CurrentRoom;
        string key = $"{state.RunState.CurrentActIndex}:{state.RunState.TotalFloor}:" +
            (room?.Id?.ToString(CultureInfo.InvariantCulture) ?? Guid.NewGuid().ToString("N"));
        string name = string.Join(", ", state.Enemies.Select(c => c.Name).Distinct());
        if (string.IsNullOrWhiteSpace(name))
            name = state.Encounter?.Id.Entry ?? T("Unknown encounter", "Rencontre inconnue");
        Session.BeginCombat(key, name, state.RunState.CurrentActIndex + 1, state.RunState.TotalFloor,
            partial: state.RoundNumber > 1 || manager.IsStarting == false);
        Session.ObserveRound(state.RoundNumber);
        _history.Changed += OnHistoryChanged;
        _manager.TurnStarted += OnTurnStarted;
        _manager.CombatWon += OnWon;
        _manager.CombatEnded += OnEnded;
        Selection.Live(Session.Run); Window.Close();
        Drain(); Save(true);
    }
    private static void OnHistoryChanged() => _dirty = true;
    private static void OnTurnStarted(CombatState state) { Session.ObserveRound(state.RoundNumber); _dirty = true; }
    private static void OnWon(CombatRoom _) { _pendingOutcome = CombatOutcome.Won; Seal(); }
    private static void OnEnded(CombatRoom _) => Seal();
    internal static void OnEnding(bool lost)
    {
        _pendingOutcome = lost ? CombatOutcome.Lost : CombatOutcome.Won;
        Drain(); Window.Close();
    }
    internal static void BeforeHistoryClear(CombatHistory history)
    {
        if (!ReferenceEquals(_history, history)) return;
        // In the supplied DLL History.Clear happens BEFORE CombatWon/CombatEnded.
        Seal();
    }
    private static void Seal()
    {
        Drain();
        _sealed = true;
        Session.Complete(_pendingOutcome);
        Save(true);
    }
    internal static void BeforeReset()
    {
        if (_state == null) return;
        Seal(); Detach(); Window.Close();
    }
    internal static void OnRunCleanup()
    {
        BeforeReset();
        var manager = RunManager.Instance;
        string status = manager?.IsAbandoned == true ? "abandoned" : manager?.IsGameOver == true
            ? (manager.History?.Win == true ? "won" : "lost") : "suspended";
        Session.SetRunStatus(status); Save(true); PlayerNames.Reset(); _runState = null; DamageMeterController.Hide();
    }
    private static void Detach()
    {
        if (_history != null) _history.Changed -= OnHistoryChanged;
        if (_manager != null)
        { _manager.TurnStarted -= OnTurnStarted; _manager.CombatWon -= OnWon; _manager.CombatEnded -= OnEnded; }
        _history = null; _manager = null; _state = null; _dirty = false;
        Reader.Reset(); Session.DetachCombat();
    }
    private static void Drain()
    {
        if (_sealed || _state == null || _history == null) return;
        Session.ObserveRound(_state.RoundNumber);
        Reader.Drain(_history, Session); _dirty = false;
    }
    internal static bool Observes(CombatHistory history) => !_sealed && _state != null &&
        ReferenceEquals(_history, history) && Session.Active?.Outcome == CombatOutcome.InProgress;
    internal static PoisonDamageCredit? CapturePoison(PoisonPower poison)
    {
        if (_sealed || _state == null || !ReferenceEquals(poison.Owner.CombatState, _state) ||
            Session.Active?.Outcome != CombatOutcome.InProgress) return null;
        // Consume applications/decrements in native history order BEFORE the tick.
        Drain();
        return Reader.CapturePoison(poison);
    }
    internal static void ForgetPoison(PoisonPower poison)
    {
        if (_state == null || !ReferenceEquals(poison.Owner.CombatState, _state)) return;
        Drain(); Reader.ForgetPoison(poison);
    }
    internal static bool CanTrack(Creature? creature) => !_sealed && _state != null &&
        ReferenceEquals(_state, CombatManager.Instance?.DebugOnlyGetState()) && HistoryReader.PlayerId(creature) != null;
    internal static void Record(Creature creature, Stat stat, int amount)
    {
        if (amount <= 0 || !CanTrack(creature)) return;
        int round = Math.Max(0, _state!.RoundNumber);
        var phase = round == 0 ? JournalPhase.Setup : _state.CurrentSide == CombatSide.Player ? JournalPhase.Player : JournalPhase.Enemy;
        HistoryReader.Emit(Session, creature, round, phase, stat, amount);
    }
    internal static bool GameUiBlocking => NGame.Instance?.FeedbackScreen?.Visible == true ||
        NOverlayStack.Instance?.ScreenCount > 0 ||
        (NCapstoneContainer.Instance?.InUse == true && NMapScreen.Instance?.IsVisibleInTree() != true);
    internal static bool NativeUiBlocking => SettingsMenu.IsVisible || GameUiBlocking;
    internal static void Tick()
    {
        if (_inTick) return;
        _inTick = true;
        try
        {
            using var measurement = PerformanceProbe.Measure(ProbeSection.Journal);
            Boot();
            var manager = RunManager.Instance;
            if (manager?.IsInProgress == true && !manager.IsCleaningUp && manager.DebugOnlyGetState() is { } run)
                BeginRun(run);
            var state = CombatManager.Instance?.DebugOnlyGetState();
            if (manager?.IsInProgress == true && !manager.IsCleaningUp && state != null && !ReferenceEquals(_state, state) &&
                (CombatManager.Instance?.IsInProgress == true || CombatManager.Instance?.IsStarting == true) &&
                CombatManager.Instance?.IsOverOrEnding != true)
                OnCombatSetUp();
            if (_dirty) Drain();
            if (_runState != null && manager?.IsInProgress == true && !manager.IsCleaningUp)
                PlayerNames.Refresh(_runState, Session, Time.GetTicksMsec());
            Selection.Sync(Session.Run);
            bool uiBlocking = NativeUiBlocking;
            Window.Render(Session, Selection, uiBlocking, StorageWarning);
            DamageMeterController.Tick(Session,
                _runState != null && manager?.IsInProgress == true && !manager.IsCleaningUp,
                _runState?.CurrentRoom is CombatRoom,
                NMapScreen.Instance?.IsVisibleInTree() == true,
                // HandViewer, clock and Guardian details are non-modal: never hide the meter for them.
                HudVisibilityPolicy.IsTransientBlocker(GameUiBlocking, Window.IsOpen));
            Save(false);
        }
        catch (Exception ex)
        {
            DamageMeterController.Hide();
            Session.MarkPartial();
            ulong now = Time.GetTicksMsec();
            if (_lastError == 0 || now - _lastError >= 10000)
            { _lastError = now; ModLog.Error("Journal.Tick", ex); }
        }
        finally { _inTick = false; }
    }
    internal static void FlushPersistence() => Save(true);
    private static void FinishPendingSave(bool wait)
    {
        if (_pendingSave == null || (!wait && !_pendingSave.IsCompleted)) return;
        try
        {
            var error = _pendingSave.GetAwaiter().GetResult();
            if (error != null) throw error;
            _savedRevision = _pendingRevision; StorageWarning = "";
        }
        catch (Exception ex) { StorageWarning = "save"; ModLog.Error("Journal.Save", ex); }
        finally { _pendingSave = null; }
    }
    private static void Save(bool force)
    {
        FinishPendingSave(force);
        if (_archive == null || Session.Run == null || Session.Revision == _savedRevision || _pendingSave != null) return;
        ulong now = Time.GetTicksMsec();
        if (!force && now - _lastSave < 5000) return;
        _lastSave = now;
        try
        {
            using var measurement = PerformanceProbe.Measure(ProbeSection.JournalCheckpoint);
            var snapshot = Checkpoint.Capture(Session.Run);
            long revision = Session.Revision;
            if (force)
            {
                // Durability at combat/run transitions takes priority over background throughput.
                _archive.Save(snapshot); _savedRevision = revision; StorageWarning = "";
                return;
            }
            var archive = _archive;
            _pendingRevision = revision;
            _pendingSave = Task.Run(() =>
            {
                try { archive.Save(snapshot); return (Exception?)null; }
                catch (Exception ex) { return ex; }
            });
        }
        catch (Exception ex) { StorageWarning = "save"; ModLog.Error("Journal.Save", ex); }
    }
    internal static void Toggle()
    {
        ModRuntime.EnsureStarted();
        if (NativeUiBlocking) return;
        Drain(); Window.Toggle(); Tick();
    }
    internal static void Refresh() { Window.Invalidate(); DamageMeterController.Refresh(); ModRuntime.EnsureStarted(); }
    internal static void Export()
    {
        Boot(); Drain();
        if (Session.Run == null) { Window.SetNotice(T("No run recorded yet.", "Aucune partie enregistrée.")); return; }
        try
        {
            string path = Path.Combine(OS.GetUserDataDir(), "betterspire_run_journal_export.json");
            new JournalArchive(path).Save(Session.Run);
            Window.SetNotice(T("JSON export saved. Path in the mod log.", "Export JSON enregistré. Chemin dans le journal du mod."));
            ModLog.Info("Run journal exported: " + path);
        }
        catch (Exception ex) { Window.SetNotice(T("Export failed. Check the mod log.", "Échec de l’export. Consulte le journal du mod.")); ModLog.Error("Journal.Export", ex); }
    }
    private static string T(string en, string fr) => ModText.IsFrench ? fr : en;
}
