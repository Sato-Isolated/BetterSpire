#nullable enable
using System;
using System.Collections.Generic;
using BetterSpire2.Guardian.Core;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace BetterSpire2.Guardian.UI;

/// <summary>One lightweight label per actor, positioned every frame; no per-frame simulation.</summary>
internal sealed class GuardianOverheadHud
{
    private CanvasLayer? _layer;
    private Control? _root;
    private SceneTree? _tree;
    private NCombatRoom? _room;
    private Creature? _local;
    private readonly Dictionary<Creature, OverheadLabel> _labels = new();
    private readonly List<Creature> _remove = new();
    private readonly List<OverheadLabel> _ordered = new();
    private readonly List<HudRect> _obstacles = new();
    private int _generation;
    private ulong _lastErrorLog;

    internal void Render(ForecastResult result, IReadOnlyDictionary<Creature, string> actorIds)
    {
        if (!EnsureCreated()) { Clear(); return; }
        _generation++;
        foreach (var node in _room!.CreatureNodes)
        {
            if (!Valid(node) || !ShouldTrack(node.Entity)) continue;
            var creature = node.Entity;
            bool isLocal = LocalContext.IsMe(creature);
            if (isLocal) _local = creature;
            ActorForecast? actor = null;
            if (actorIds.TryGetValue(creature, out var id)) result.Actors.TryGetValue(id, out actor);
            // Only show a pet when it is actually projected to lose HP; never pretend its HP is the player's.
            if (!creature.IsPlayer && (actor == null || actor.HpLost == 0)) continue;
            if (actor != null && !OverheadPresentation.ShouldShow(actor, result.IsPartial, ModSettings.GuardianOnlyDanger)) continue;
            Entry(node).Bind(node, actor, result.IsPartial, _generation, isLocal ? 0 : creature.IsPlayer ? 1 : 2);
        }
        FinishSync();
    }

    internal void MarkStale()
    {
        foreach (var label in _ordered) label.Invalidated = true;
    }

    internal void RenderStatus()
    {
        // Unavailable/resolving readouts intentionally contain no placeholder. Do not build hidden labels.
        MarkStale();
        Hide();
    }

    internal void Hide()
    {
        if (Valid(_root)) _root!.Visible = false;
        if (Valid(_tree)) _tree!.ProcessFrame -= UpdatePositions;
        _tree = null;
    }

    internal void Clear()
    {
        Hide();
        foreach (var label in _labels.Values) label.Free();
        _labels.Clear(); _ordered.Clear(); _remove.Clear(); _obstacles.Clear();
        if (Valid(_layer)) _layer!.QueueFree();
        _layer = null; _root = null; _room = null; _local = null;
    }

    private bool EnsureCreated()
    {
        var game = NGame.Instance;
        var room = NCombatRoom.Instance;
        if (!Valid(game) || !game!.IsInsideTree() || !Valid(room) || !room!.IsInsideTree()) return false;
        if (Valid(_root) && Valid(_layer) && _layer!.GetParent() == game && _room == room) return true;
        Clear();
        _room = room;
        _layer = new CanvasLayer { Name = "BetterSpireGuardianOverhead", Layer = 14 };
        // Keep the layer/root transforms at identity: all anchors are expressed in viewport coordinates.
        _root = new Control { Name = "GuardianTextOnly", Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore, FocusMode = Control.FocusModeEnum.None };
        game.AddChild(_layer);
        _layer.AddChild(_root);
        return true;
    }

    private OverheadLabel Entry(NCreature node)
    {
        if (_labels.TryGetValue(node.Entity, out var label) && Valid(label.TextNode)) return label;
        label = new OverheadLabel(node, _root!);
        _labels[node.Entity] = label;
        return label;
    }

    private void FinishSync()
    {
        _remove.Clear(); _ordered.Clear();
        foreach (var pair in _labels)
        {
            if (pair.Value.Generation != _generation) _remove.Add(pair.Key);
            else _ordered.Add(pair.Value);
        }
        foreach (var creature in _remove) { _labels[creature].Free(); _labels.Remove(creature); }
        // Give the local player's readout first choice when players crowd together.
        _ordered.Sort((a, b) => a.Order.CompareTo(b.Order));
        if (_tree == null && _ordered.Count > 0)
        {
            _tree = _root!.GetTree();
            _tree.ProcessFrame += UpdatePositions;
        }
        UpdatePositions();
        if (_ordered.Count == 0) Hide();
    }

    private void UpdatePositions()
    {
        try
        {
            using var measurement = PerformanceProbe.Measure(ProbeSection.OverheadPlacement);
            if (!Valid(_root) || !Valid(_room) || _room != NCombatRoom.Instance) { Clear(); return; }
            var manager = CombatManager.Instance;
            if (manager == null || !manager.IsInProgress || manager.IsOverOrEnding) { Clear(); return; }
            if (!_room!.IsVisibleInTree() || _room.Modulate.A <= .01f ||
                !ModSettings.PlayerDamageTotal || SettingsMenu.IsVisible ||
                NCapstoneContainer.Instance?.InUse == true || NOverlayStack.Instance?.ScreenCount > 0 ||
                NGame.Instance?.FeedbackScreen?.Visible == true)
            { _root!.Visible = false; return; }
            _root!.Visible = true;
            bool changed = IsResolving(manager);
            foreach (var label in _ordered)
            {
                if (!Valid(label.CreatureNode) || !Valid(label.TextNode)) continue;
                if (!label.MatchesSnapshot()) changed = true;
            }
            // Once invalidated, never revive old numbers when an animation ends; await a fresh capture.
            if (changed) foreach (var label in _ordered) label.Invalidated = true;
            var viewport = _root.GetViewport();
            var visible = viewport.GetVisibleRect();
            var bounds = new HudRect(visible.Position.X, visible.Position.Y, visible.Size.X, visible.Size.Y);
            _obstacles.Clear();
            foreach (var label in _ordered)
                if (Valid(label.CreatureNode) && OverheadAnchor.TryIntentRect(label.CreatureNode, out var intent))
                    _obstacles.Add(intent);
            foreach (var label in _ordered)
            {
                if (!Valid(label.TextNode)) continue;
                var readout = label.Invalidated
                    ? new OverheadReadout(OverheadPresentation.Resolving, OverheadSeverity.Normal)
                    : label.Readout;
                // No placeholder for unavailable data and no invisible collision rectangle.
                // A later valid capture restores the readout through the normal render path.
                if (string.IsNullOrEmpty(readout.Text))
                { label.TextNode.Visible = false; continue; }
                if (!OverheadAnchor.TryHead(label.CreatureNode, viewport, out var head))
                { label.TextNode.Visible = false; continue; }
                label.Apply(readout.Text, readout.Severity);
                var placement = OverheadLayout.Place(head.X, head.Y, label.Size.X, label.Size.Y,
                    ModSettings.GuardianOverheadOffsetX, ModSettings.GuardianOverheadGap, bounds, _obstacles);
                label.TextNode.Visible = placement.HasValue;
                if (placement is not { } rect) continue;
                var position = new Vector2(rect.X, rect.Y);
                if (label.TextNode.Position != position) label.TextNode.Position = position;
                _obstacles.Add(rect);
            }
        }
        catch (Exception ex)
        {
            Hide(); // No frame callback may escape into the game's update loop.
            ulong now = Time.GetTicksMsec();
            if (_lastErrorLog == 0 || now - _lastErrorLog > 10000)
            { _lastErrorLog = now; ModLog.Error("Guardian.Overhead", ex); }
        }
    }

    private bool IsResolving(CombatManager manager)
    {
        if (manager.IsStarting || manager.IsEnemyTurnStarted || manager.EndingPlayerTurnPhaseOne ||
            manager.EndingPlayerTurnPhaseTwo || manager.PlayerActionsDisabled) return true;
        var player = _local?.Player;
        return player != null && (player.PlayerCombatState?.Phase != PlayerTurnPhase.Play ||
            manager.IsPlayerReadyToEndTurn(player));
    }

    private static bool ShouldTrack(Creature creature)
    {
        if (creature == null || creature.IsDead) return false;
        if (creature.IsPlayer) return ModSettings.GuardianShowTeammates || LocalContext.IsMe(creature);
        return ModSettings.GuardianShowPets && creature.PetOwner is { } owner && creature.MaxHp > 0 &&
            creature.Monster?.IsHealthBarVisible != false &&
            (ModSettings.GuardianShowTeammates || LocalContext.IsMe(owner.Creature));
    }

    private static bool Valid(GodotObject? value) => OverheadAnchor.Valid(value);
}
