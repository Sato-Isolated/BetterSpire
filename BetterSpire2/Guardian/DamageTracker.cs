#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BetterSpire2.Guardian.Core;
using BetterSpire2.Guardian.Game;
using BetterSpire2.Guardian.UI;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace BetterSpire2.Trackers;

/// <summary>Owns forecast lifetime, gameplay invalidation and presentation. All game access stays on the main thread.</summary>
public static class DamageTracker
{
    private static readonly GuardianHud Hud = new();
    private static readonly CombatLedger Ledger = new();
    private static readonly RefreshGate Gate = new();
    private static readonly ForecastEventObserver Observer = new(InvalidateForecast);
    private static ForecastResult? _lastResult;
    private static IReadOnlyDictionary<Creature, string>? _actorIds;
    private static CombatState? _state;
    private static bool _calculating, _renderPending = true, _statusRendered, _resolving;
    private static ulong _lastErrorLog, _retryAfter;
    public static bool DetailsVisible => Hud.DetailsVisible;

    // Called only for gameplay changes or explicit settings changes, never card-hover rendering.
    private static void InvalidateForecast()
    {
        Gate.Invalidate(); _lastResult = null; _renderPending = true;
        Hud.MarkStale();
    }
    public static void Recalculate() { InvalidateForecast(); ModRuntime.EnsureStarted(); }
    public static void OnCombatSetUp() { Hide(); ModRuntime.EnsureStarted(); }
    public static void Hide()
    {
        Observer.Dispose(); _lastResult = null; _actorIds = null; _state = null;
        Gate.Reset(); Hud.ResetCombat(); Ledger.Reset(); _retryAfter = 0;
        _renderPending = true; _statusRendered = _resolving = false;
    }
    public static void ToggleDetails()
    {
        if (!Hud.DetailsVisible && (!ModSettings.PlayerDamageTotal ||
            CombatManager.Instance is not { IsInProgress: true, IsOverOrEnding: false })) return;
        Hud.ToggleDetails(); _renderPending = true; _statusRendered = false; ModRuntime.EnsureStarted();
    }
    public static void ChangeDetailPage(int delta) { Hud.ChangePage(delta); _renderPending = true; }
    public static void ToggleVisibility()
    {
        ModSettings.PlayerDamageTotal = !ModSettings.PlayerDamageTotal;
        ModSettings.Save();
        if (!ModSettings.PlayerDamageTotal) Hide(); else Recalculate();
    }
    internal static void Tick(ulong now)
    {
        if (_calculating) return;
        if (!ModSettings.PlayerDamageTotal)
        { if (_state != null || _lastResult != null) Hide(); return; }
        if (now < _retryAfter) return;
        _calculating = true;
        try
        {
            var manager = CombatManager.Instance;
            var state = CombatLifecycle.CurrentState;
            var local = state == null ? null : LocalContext.GetMe(state);
            if (!ModSettings.PlayerDamageTotal || manager == null || !manager.IsInProgress ||
                manager.IsOverOrEnding || state == null || local == null || local.Creature.IsDead)
            { if (_state != null || _lastResult != null) Hide(); return; }
            if (!ReferenceEquals(_state, state)) { Hide(); _state = state; }
            Observer.Observe(state);
            Ledger.Update(state, local.Creature);
            bool resolving = manager.IsStarting || manager.IsEnemyTurnStarted || manager.EndingPlayerTurnPhaseOne ||
                manager.EndingPlayerTurnPhaseTwo || manager.PlayerActionsDisabled ||
                !manager.IsPartOfPlayerTurn(local) || state.Players.Any(manager.IsExecutingCardOrPotionEffect) ||
                local.PlayerCombatState?.Phase != PlayerTurnPhase.Play || manager.IsPlayerReadyToEndTurn(local);
            if (resolving != _resolving)
            { _resolving = resolving; InvalidateForecast(); _statusRendered = false; }
            if (NCapstoneContainer.Instance?.InUse == true || NOverlayStack.Instance?.ScreenCount > 0 ||
                NGame.Instance?.FeedbackScreen?.Visible == true)
            { Hud.Hide(); _renderPending = true; _statusRendered = false; return; }
            if (resolving)
            {
                if (!_statusRendered)
                {
                    Hud.RenderStatus(T("Resolving…", "Résolution…"),
                        T("The next projection appears on your turn.", "La prévision reprend pendant ton tour."), Ledger);
                    _statusRendered = true;
                }
                return;
            }
            if (Gate.IsDue(now))
            {
                using var measurement = PerformanceProbe.Measure(ProbeSection.Forecast);
                long version = Gate.Version;
                var adapter = new GameForecastAdapter(state, local);
                var result = adapter.Capture();
                Gate.Complete(now, version);
                // A callback during capture can invalidate it; don't erase that notification.
                if (Gate.IsDirty) { _lastResult = null; return; }
                _lastResult = result; _actorIds = adapter.ActorIds;
                _renderPending = true; _statusRendered = false;
            }
            if (_renderPending && _lastResult != null && _actorIds != null)
            {
                using var measurement = PerformanceProbe.Measure(ProbeSection.GuardianUi);
                Hud.Render(_lastResult, Ledger, _actorIds);
                _renderPending = false;
            }
        }
        catch (Exception exception)
        {
            _lastResult = null; Gate.Fail(now); _retryAfter = now + 1000; Hud.MarkStale();
            try
            {
                if (!_statusRendered)
                    Hud.RenderStatus(T("Forecast unavailable", "Prévision indisponible"),
                        T("No safety verdict. Check the mod log.", "Aucun verdict de sécurité. Consulte le journal."), Ledger, error: true);
                _statusRendered = true;
            }
            catch { Hud.Hide(); }
            if (_lastErrorLog == 0 || now - _lastErrorLog > 10000)
            { _lastErrorLog = now; ModLog.Error("Guardian.Tick", exception); }
        }
        finally { _calculating = false; }
    }
    public static void ExportLastForecast()
    {
        if (_lastResult == null || Gate.IsDirty) return;
        try
        {
            string path = Path.Combine(OS.GetUserDataDir(), "betterspire_guardian_forecast.json");
            var dump = new { Version = "3.6.1-v111", GameModule = typeof(CombatState).Module.ModuleVersionId,
                Forecast = _lastResult, Observed = new { Ledger.PlayerHpLost, Ledger.PlayerBlocked,
                    Ledger.PlayerBlockGained, Ledger.PetHpLost, Ledger.Recent } };
            File.WriteAllText(path, JsonSerializer.Serialize(dump, new JsonSerializerOptions { WriteIndented = true }));
            ModLog.Info("Guardian export: " + path);
        }
        catch (Exception ex) { ModLog.Error("Guardian.Export", ex); }
    }
    private static string T(string en, string fr) => ModText.IsFrench ? fr : en;
}
