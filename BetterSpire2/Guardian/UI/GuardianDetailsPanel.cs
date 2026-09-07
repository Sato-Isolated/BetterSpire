#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BetterSpire2.Guardian.Core;
using BetterSpire2.Guardian.Game;
using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace BetterSpire2.Guardian.UI;

/// <summary>Optional diagnostics, never the permanent HUD. Created only after F2.</summary>
internal sealed class GuardianDetailsPanel
{
    private CanvasLayer? _layer;
    private Panel? _panel;
    private Label? _heading, _main, _block, _recovery, _footer;
    private readonly List<(Label Left, Label Right)> _rows = new();
    private bool _details;
    private int _page;
    private const int RowsPerPage = 8;
    private readonly Color _quiet = HudTheme.Muted;
    private readonly Color _text = HudTheme.Text;
    private readonly Color _danger = HudTheme.Danger;
    private readonly Color _warning = HudTheme.Accent;
    private readonly Color _blockColor = HudTheme.Info;
    internal bool DetailsVisible => _details;

    internal void ToggleDetails() { _details = !_details; _page = 0; if (!_details) Hide(); }
    internal void Close() { _details = false; _page = 0; Hide(); }
    internal void ChangePage(int delta) { _page = Math.Max(0, _page + delta); }
    internal void Hide() { if (Valid(_panel)) _panel!.Visible = false; }

    internal void Render(ForecastResult result, CombatLedger ledger)
    {
        if (!_details) { Hide(); return; }
        if (!EnsureCreated()) return;
        var local = result.Local;
        _panel!.Visible = true;
        Color severity = result.KnownLethal ? _danger : result.IsPartial ? _warning : _text;
        string verdict = result.KnownLethal
            ? (result.IsPartial ? T("POSSIBLE LETHAL", "MORTEL POSSIBLE") : T("LETHAL PROJECTED", "MORTEL PRÉVU"))
            : result.IsPartial ? T("PARTIAL PROJECTION", "PROJECTION PARTIELLE") : T("PROJECTION", "PROJECTION");
        Set(_heading!, "GUARDIAN  /  " + verdict, severity);
        Set(_main!, $"−{local.HpLost} {T("HP", "PV")}  →  {local.FinalHp} {T("remaining", "restants")}", severity);
        Set(_block!, T("Block", "Blocage") + $"  {local.StartBlock}  +{local.BlockGained} " +
            T("projected", "prévus") + $"  →  {local.FinalBlock}", _blockColor);
        int petLoss = result.Actors.Values.Where(a => a.IsLocalPet).Sum(a => a.HpLost);
        string extras = T("Healing", "Soins") + $" +{local.Healed}  ·  " +
            T("prevented", "évités") + $" {local.Prevented}";
        if (petLoss > 0) extras += $"  ·  Osty −{petLoss}";
        if (local.BuffersUsed > 0) extras += $"  ·  Buffer −{local.BuffersUsed}";
        if (local.Revivals > 0) extras = T("Lizard Tail consumed", "Queue de lézard consommée") + "  ·  " + extras;
        Set(_recovery!, extras, local.Revivals > 0 ? _warning : _quiet);
        var lines = BuildDetails(result, ledger);
        RenderDetails(lines);
        Layout();
    }

    internal void RenderStatus(string message, string explanation, CombatLedger? ledger = null, bool error = false)
    {
        if (!_details) { Hide(); return; }
        if (!EnsureCreated()) return;
        _panel!.Visible = true;
        Set(_heading!, "GUARDIAN  /  " + (error ? T("UNAVAILABLE", "INDISPONIBLE") : T("RESOLVING", "RÉSOLUTION")), error ? _warning : _quiet);
        Set(_main!, message, _text);
        Set(_block!, explanation, _quiet);
        Set(_recovery!, ledger == null ? "" : T("Actual combat loss", "PV réellement perdus au combat") + $" : {ledger.PlayerHpLost}", _quiet);
        foreach (var row in _rows) { row.Left.Visible = false; row.Right.Visible = false; }
        Set(_footer!, "F1 " + T("settings", "paramètres") + "  ·  F4 " + T("hide", "masquer"), _quiet);
        Layout(forceCompact: true);
    }

    private List<(string Left, string Right)> BuildDetails(ForecastResult result, CombatLedger ledger)
    {
        var lines = new List<(string, string)>();
        lines.Add((T("If you end the turn now", "Si tu termines le tour maintenant"), ""));
        lines.Add((T("Through the displayed enemy attacks", "Jusqu'aux attaques ennemies affichées"), ""));
        foreach (string warning in result.Warnings) lines.Add(("! " + warning, "?"));
        if (result.Local.FirstLethalSource != null)
            lines.Add((T("First lethal hit: ", "Premier coup mortel : ") + result.Local.FirstLethalSource,
                result.Local.Revivals > 0 ? T("revived", "réanimé") : "!"));
        foreach (var step in result.Steps)
        {
            if (!result.Actors.TryGetValue(step.Target, out var actor)) continue;
            // Enemy poison is included so the reason for a cancelled attack stays inspectable.
            string name = step.Target == result.LocalPlayerId ? step.Source : actor.Name + " / " + step.Source;
            string values = step.Kind switch
            {
                ForecastEventKind.Block => $"+{step.BlockGained} " + T("block", "bloc"),
                ForecastEventKind.Heal => $"+{step.Healed} " + T("HP", "PV"),
                ForecastEventKind.Damage => $"−{step.HpLost} " + T("HP", "PV") + $" / {step.BlockSpent} " + T("blk", "bloc"),
                _ => ""
            };
            if (step.BuffersUsed > 0) values += " / B−1";
            if (step.Revived) values += " / +" + step.Healed;
            lines.Add((name, values));
        }
        lines.Add((T("OBSERVED — current combat, not forecast", "RÉEL — combat en cours, pas la prévision"), ""));
        lines.Add((T("HP actually lost / blocked", "PV effectivement perdus / bloqués"), $"{ledger.PlayerHpLost} / {ledger.PlayerBlocked}"));
        lines.Add((T("Block actually gained / pet HP lost", "Bloc réellement gagné / PV familier perdus"), $"{ledger.PlayerBlockGained} / {ledger.PetHpLost}"));
        foreach (var hit in ledger.Recent.Reverse())
            lines.Add((hit.Receiver + " / " + hit.Source, $"−{hit.HpLost} " + T("HP", "PV") + $" / {hit.Blocked} " + T("blk", "bloc")));
        return lines;
    }

    private void RenderDetails(List<(string Left, string Right)> lines)
    {
        int pages = Math.Max(1, (lines.Count + RowsPerPage - 1) / RowsPerPage);
        _page = Math.Clamp(_page, 0, pages - 1);
        for (int i = 0; i < RowsPerPage; i++)
        {
            int index = _page * RowsPerPage + i;
            bool visible = _details && index < lines.Count;
            _rows[i].Left.Visible = _rows[i].Right.Visible = visible;
            if (!visible) continue;
            Set(_rows[i].Left, lines[index].Left, lines[index].Left.StartsWith("!") ? _warning : _quiet);
            Set(_rows[i].Right, lines[index].Right, _text);
        }
        Set(_footer!, _details ? $"F2 ×  ·  Alt+PgUp/PgDn  {_page + 1}/{pages}  ·  Ctrl+F2 " + T("export", "exporter")
            : "F2 " + T("details", "détails") + "  ·  F4 " + T("hide", "masquer") + (lines.Any(l => l.Left.StartsWith("!")) ? "  ·  ! " + T("uncertain effects", "effets incertains") : ""), _quiet);
    }

    private bool EnsureCreated()
    {
        var game = NGame.Instance;
        if (game == null || !GodotObject.IsInstanceValid(game) || !game.IsInsideTree()) return false;
        if (Valid(_panel) && Valid(_layer) && _layer!.GetParent() == game) return true;
        if (Valid(_layer)) _layer!.QueueFree();
        _rows.Clear();
        _layer = new CanvasLayer { Name = "BetterSpireGuardianDetails", Layer = HudLayers.Guardian };
        game.AddChild(_layer);
        _panel = new Panel { Name = "GuardianOptionalDetails", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None, ClipContents = true };
        var style = HudTheme.Panel(0);
        _panel.AddThemeStyleboxOverride("panel", style);
        _layer.AddChild(_panel);
        _heading = MakeLabel(12); _main = MakeLabel(23); _block = MakeLabel(14);
        _recovery = MakeLabel(12); _footer = MakeLabel(11);
        for (int i = 0; i < RowsPerPage; i++)
        {
            var left = MakeLabel(13); var right = MakeLabel(13);
            right.HorizontalAlignment = HorizontalAlignment.Right;
            _rows.Add((left, right));
        }
        return true;
    }

    private Label MakeLabel(int fontSize)
    {
        var label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            ClipText = true, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        _panel!.AddChild(label);
        return label;
    }

    private void Layout(bool forceCompact = false)
    {
        if (!Valid(_panel)) return;
        var viewport = _panel!.GetViewport().GetVisibleRect();
        bool expanded = _details && !forceCompact;
        float width = expanded ? 520 : 370;
        float height = expanded ? 322 : 118;
        float scale = ModSettings.GuardianScalePercent / 100f;
        scale = Math.Max(.4f, Math.Min(scale, Math.Min((viewport.Size.X - 24) / width, (viewport.Size.Y - 24) / height)));
        _panel.Scale = new Vector2(scale, scale);
        _panel.Size = new Vector2(width, height);
        _panel.Position = viewport.Position + new Vector2(12, 12) + new Vector2(
            Math.Max(0, viewport.Size.X - width * scale - 24) * ModSettings.GuardianXPercent / 100f,
            Math.Max(0, viewport.Size.Y - height * scale - 24) * ModSettings.GuardianYPercent / 100f);
        Place(_heading!, 12, 8, width - 24, 17);
        Place(_main!, 12, 26, width - 24, 29);
        Place(_block!, 12, 57, width - 24, 19);
        Place(_recovery!, 12, 79, width - 24, 17);
        for (int i = 0; i < RowsPerPage; i++)
        {
            Place(_rows[i].Left, 12, 113 + i * 23, width - 188, 22);
            Place(_rows[i].Right, width - 174, 113 + i * 23, 162, 22);
        }
        Place(_footer!, 12, height - 20, width - 24, 17);
    }

    private static void Place(Control control, float x, float y, float w, float h)
    { control.Position = new Vector2(x, y); control.Size = new Vector2(w, h); }
    private static void Set(Label label, string text, Color color)
    { if (label.Text != text) label.Text = text; label.AddThemeColorOverride("font_color", color); }
    private static bool Valid(GodotObject? instance) => instance != null && GodotObject.IsInstanceValid(instance);
    private static string T(string en, string fr) => ModText.IsFrench ? fr : en;
}
