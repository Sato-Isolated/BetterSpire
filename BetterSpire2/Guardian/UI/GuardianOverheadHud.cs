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
internal sealed partial class GuardianOverheadHud
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
    private int _hiddenForSpace;
    private ulong _lastErrorLog, _retryAfter;
    internal Action<int>? HiddenReadoutCountChanged { get; set; }
    internal int HiddenForSpace => _hiddenForSpace;

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
            if (!creature.IsPlayer && (actor == null ||
                (actor.MaximumHpLost == 0 && result.BranchEnumerationComplete))) continue;
            if (actor != null && !OverheadPresentation.ShouldShow(actor, result.IsPartial,
                ModSettings.GuardianOnlyDanger, result.BranchEnumerationComplete)) continue;
            Entry(node).Bind(node, actor, result.IsPartial, _generation,
                isLocal ? 0 : creature.IsPlayer ? 1 : 2, result.BranchEnumerationComplete);
        }
        FinishSync();
    }

    internal void MarkStale()
    {
        foreach (var label in _ordered) label.Display.Invalidate();
    }

    internal bool RenderStatus(bool error = false)
    {
        foreach (var label in _ordered) label.Display.SetStatus(error);
        if (Valid(_root) && Valid(_room)) FinishSync();
        return Valid(_tree);
    }

    internal void Hide()
    {
        SetHiddenForSpace(0);
        if (Valid(_root)) _root!.Visible = false;
        if (Valid(_tree)) _tree!.ProcessFrame -= UpdatePositions;
        _tree = null; _retryAfter = 0;
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
        ulong now = Time.GetTicksMsec();
        if (now < _retryAfter) return;
        _retryAfter = 0;
        try
        {
            using var measurement = PerformanceProbe.Measure(ProbeSection.OverheadPlacement);
            if (!Valid(_root) || !Valid(_room) || _room != NCombatRoom.Instance) { Clear(); return; }
            var manager = CombatManager.Instance;
            if (manager == null || !manager.IsInProgress || manager.IsOverOrEnding) { Clear(); return; }
            var player = _local?.Player;
            if (player == null || !GuardianTurnVisibility.ShouldShow(manager.IsStarting, manager.IsEnemyTurnStarted,
                manager.EndingPlayerTurnPhaseOne || manager.EndingPlayerTurnPhaseTwo,
                player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
                manager.IsPartOfPlayerTurn(player), manager.IsPlayerReadyToEndTurn(player)))
            { Clear(); return; }
            if (!_room!.IsVisibleInTree() || _room.Modulate.A <= .01f ||
                !ModSettings.PlayerDamageTotal ||
                NCapstoneContainer.Instance?.InUse == true || NOverlayStack.Instance?.ScreenCount > 0 ||
                NGame.Instance?.FeedbackScreen?.Visible == true)
            { SetHiddenForSpace(0); _root!.Visible = false; return; }
            _root!.Visible = true;
            var viewport = _root.GetViewport();
            var visible = viewport.GetVisibleRect();
            var bounds = new HudRect(visible.Position.X, visible.Position.Y, visible.Size.X, visible.Size.Y);
            _obstacles.Clear();
            foreach (var label in _ordered)
                if (Valid(label.CreatureNode) && OverheadAnchor.TryIntentRect(label.CreatureNode, out var intent))
                    _obstacles.Add(intent);
            int hiddenForSpace = 0;
            foreach (var label in _ordered)
            {
                if (!Valid(label.TextNode) || !Valid(label.CreatureNode)) continue;
                if (!ShouldTrack(label.CreatureNode.Entity)) { label.TextNode.Visible = false; continue; }
                var readout = label.Display.Read();
                if (!OverheadAnchor.TryHead(label.CreatureNode, viewport, out var head))
                { label.TextNode.Visible = false; continue; }
                label.Apply(readout.Text, readout.Severity);
                var placement = OverheadLayout.Place(head.X, head.Y, label.Size.X, label.Size.Y,
                    ModSettings.GuardianOverheadOffsetX, ModSettings.GuardianOverheadGap, bounds, _obstacles);
                label.TextNode.Visible = placement.HasValue;
                if (placement is not { } rect) { hiddenForSpace++; continue; }
                var position = new Vector2(rect.X, rect.Y);
                if (label.TextNode.Position != position) label.TextNode.Position = position;
                _obstacles.Add(rect);
            }
            SetHiddenForSpace(hiddenForSpace);
        }
        catch (Exception ex)
        {
            // Keep the frame callback attached so a transient layout error can recover without
            // waiting for another combat event to render the Guardian HUD.
            if (Valid(_root)) _root!.Visible = false;
            SetHiddenForSpace(0);
            _retryAfter = now + 1000;
            if (_lastErrorLog == 0 || now - _lastErrorLog > 10000)
            { _lastErrorLog = now; ModLog.Error("Guardian.Overhead", ex); }
        }
    }

    private void SetHiddenForSpace(int count)
    {
        if (_hiddenForSpace == count) return;
        _hiddenForSpace = count;
        HiddenReadoutCountChanged?.Invoke(count);
    }

    private static bool Valid(GodotObject? value) => OverheadAnchor.Valid(value);
}
