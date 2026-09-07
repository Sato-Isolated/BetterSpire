#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace BetterSpire2.HandViewer;

/// <summary>Combat lifetime and selection. Layout and card rendering live in separate partials.</summary>
public static partial class TeammateHandViewer
{
    private static bool _visible, _french;
    private static CombatManager? _manager;
    private static readonly List<Player> _players = new();
    private static readonly List<PlayerHandSection> _sections = new();
    private static int _currentPage;
    private static ulong? _localNetId;
    private static ulong _nextRosterCheck;
    internal static bool IsVisible => _visible && UiHelpers.IsValid(_panel) && _panel!.IsVisibleInTree();

    public static void Toggle()
    {
        if (_visible) { Hide(); return; }
        if (!ModSettings.ShowTeammateHand) return;
        JournalController.Hide();
        Show();
    }
    public static bool ShowIfInCombat() { if (!_visible) Show(); return _visible; }

    public static void OnCombatSetUp()
    {
        DetachManager();
        _manager = CombatManager.Instance;
        if (_manager == null) return;
        _manager.TurnStarted += OnTurnStarted;
        _manager.CombatEnded += OnCombatEnded;
        RefreshIfVisible();
        if (ModSettings.AutoShowTeammateHand && ModSettings.ShowTeammateHand) ShowIfInCombat();
    }
    private static void OnTurnStarted(CombatState _)
    {
        foreach (var section in _sections) section.RefreshHandReference();
    }
    private static void OnCombatEnded(CombatRoom _) { Hide(); DetachManager(); }
    private static void DetachManager()
    {
        if (_manager == null) return;
        _manager.TurnStarted -= OnTurnStarted;
        _manager.CombatEnded -= OnCombatEnded;
        _manager = null;
    }
    private static RunState? GetRunState() => RunManager.Instance?.DebugOnlyGetState();

    private static void Show()
    {
        var combat = CombatManager.Instance;
        if (!ModSettings.ShowTeammateHand || combat?.DebugOnlyGetState() == null || combat.IsOverOrEnding) return;
        var run = GetRunState();
        if (run == null || run.Players.Count == 0) return;
        CleanupUI();
        _currentPage = 0;
        RefreshRoster(run);
        BuildUI();
        _visible = UiHelpers.IsValid(_panel);
        SyncContextVisibility();
    }

    internal static void FlushPendingChanges()
    {
        if (!_visible) return;
        var combat = CombatManager.Instance;
        var run = GetRunState();
        if (!ModSettings.ShowTeammateHand || combat?.DebugOnlyGetState() == null || combat.IsOverOrEnding || run == null)
        { Hide(); return; }
        if (!UiHelpers.IsValid(_panel) || _canvasLayer?.GetParent() != NGame.Instance)
        { Hide(); return; }
        ulong now = Time.GetTicksMsec();
        if (now >= _nextRosterCheck)
        {
            _nextRosterCheck = now + 500;
            if (RefreshRoster(run) || _french != ModText.IsFrench) RebuildContent();
        }
        SyncContextVisibility();
        if (!IsVisible) return;
        foreach (var section in _sections) section.FlushPendingChanges();
    }
    private static void SyncContextVisibility()
    {
        if (!UiHelpers.IsValid(_panel)) return;
        bool shown = _players.Count > 0 && HudVisibilityPolicy.ShowHand(
            HudVisibilityPolicy.IsTransientBlocker(JournalService.GameUiBlocking, JournalController.IsJournalVisible),
            NMapScreen.Instance?.IsVisibleInTree() == true);
        if (!shown && _layoutController?.IsInteracting == true)
        { _layoutController.Persist(); _layoutController.CancelInteraction(); }
        _panel!.Visible = shown;
    }

    // Selection is tied to NetId, not to an index that changes after filtering/reconnection.
    private static bool RefreshRoster(RunState run)
    {
        ulong? selected = _players.Count > 0 ? _players[Math.Clamp(_currentPage, 0, _players.Count - 1)].NetId : null;
        ulong? local = RunManager.Instance?.NetService?.NetId;
        if (run.Players.Count == 1) local = run.Players[0].NetId; // NetId 0 is a valid local id.
        var candidates = new List<Player>();
        foreach (var player in run.Players)
            if (!ModSettings.HideOwnHand || !local.HasValue || player.NetId != local.Value) candidates.Add(player);
        bool changed = _localNetId != local || candidates.Count != _players.Count;
        for (int i = 0; !changed && i < candidates.Count; i++) changed = !ReferenceEquals(candidates[i], _players[i]);
        if (!changed) return false;
        _localNetId = local;
        _players.Clear(); _players.AddRange(candidates);
        var ids = new List<ulong>();
        foreach (var player in _players) ids.Add(player.NetId);
        _currentPage = HandViewerSelection.Preserve(ids, selected, _currentPage);
        return true;
    }

    public static void RefreshIfVisible()
    {
        if (!_visible) return;
        var run = GetRunState();
        if (run == null) { Hide(); return; }
        RefreshRoster(run);
        RebuildContent();
    }
    public static void NextPage() => ChangePage(1);
    public static void PrevPage() => ChangePage(-1);
    private static void ChangePage(int direction)
    {
        if (!IsVisible || _players.Count < 2) return;
        _currentPage = HandViewerSelection.Move(_currentPage, _players.Count, direction);
        RebuildContent(); // Never rebuild the CanvasLayer or save a layout when paging.
    }
    internal static void SetCompactMode(bool compact)
    {
        if (ModSettings.CompactHandViewer == compact) return;
        if (_visible) _layoutController?.Persist(); // Store the OLD mode's size before switching.
        ModSettings.CompactHandViewer = compact;
        if (!_visible) return;
        RebuildContent();
        _autoFit?.Request();
    }
    public static void Hide()
    {
        _layoutController?.Persist();
        CleanupUI();
        _visible = false;
    }
    internal static void Stop() { Hide(); DetachManager(); _players.Clear(); _currentPage = 0; _localNetId = null; }
    private static void ClearSections()
    {
        foreach (var section in _sections) section.Cleanup();
        _sections.Clear();
    }
    private static void CleanupUI()
    {
        _autoFit?.Dispose(); _autoFit = null;
        _layoutController?.Dispose(); _layoutController = null;
        ClearSections();
        _interactiveControls.Clear();
        if (UiHelpers.IsValid(_panel)) _panel!.Visible = false;
        if (UiHelpers.IsValid(_canvasLayer)) _canvasLayer!.QueueFree();
        _canvasLayer = null; _panel = null; _body = null; _scroll = null;
        _handRoot = null;
        _title = null; _previous = _next = null;
    }
}
