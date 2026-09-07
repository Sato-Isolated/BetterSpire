#nullable enable
using System;
using System.Linq;
using BetterSpire2.Journal.Core;
using BetterSpire2.Journal.Game;
using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace BetterSpire2.Journal.UI;

/// <summary>A reusable, on-demand journal. No portraits, nested drawers, dragging or permanent panel.</summary>
internal sealed class JournalWindow
{
    private const float Width = 552, Height = 518;
    private CanvasLayer? _layer;
    private Panel? _panel;
    private Label? _title, _context, _section, _note, _footer, _pageLabel, _compact;
    private Button? _player, _sourceButton, _previous, _next, _live, _pagePrevious, _pageNext;
    private readonly Button[] _tabs = new Button[3];
    private readonly Label[] _hero = new Label[4];
    private readonly Label[] _heroLabels = new Label[4];
    private readonly Button[] _rowButtons = new Button[JournalReportBuilder.RowsPerPage];
    private readonly Label[] _rowValues = new Label[JournalReportBuilder.RowsPerPage];
    private readonly string?[] _rowKeys = new string?[JournalReportBuilder.RowsPerPage];
    private bool _open, _sources, _runDetails, _invalid = true;
    private int _page, _pages = 1, _styledScope = -1;
    private long _revision = -1;
    private (JournalScope Scope, string? Combat, int Round, string? Player, bool Live, bool French, string Warning) _selectionStamp;
    private string _notice = "";
    private long _compactRevision = -1;
    private bool _compactFrench;
    private Vector2? _layoutView;
    private readonly Color _text = HudTheme.Text, _quiet = HudTheme.Muted;
    private readonly Color _accent = HudTheme.Accent, _loss = HudTheme.Danger;
    internal bool IsOpen => _open;
    internal bool IsVisible => Valid(_panel) && _panel!.IsVisibleInTree();
    internal void Toggle() { _open = !_open; _invalid = true; if (!_open) HideVisuals(); }
    internal void Close() { _open = false; _invalid = true; HideVisuals(); }
    internal void Invalidate() => _invalid = true;
    internal void SetNotice(string text) { _notice = text; _invalid = true; }
    internal void Move(int delta) { JournalService.Selection.Move(JournalService.Session.Run, delta); _page = 0; Invalidate(); }
    internal bool Contains(Vector2 point) => IsVisible && _panel!.GetGlobalRect().HasPoint(point);
    private void HideVisuals() { if (Valid(_panel)) _panel!.Visible = false; }

    internal void Render(JournalSession session, JournalSelection selection, bool nativeUiBlocking, string storageWarning)
    {
        if (!_open && !ModSettings.ShowTurnSummary) { HideVisuals(); if (Valid(_compact)) _compact!.Visible = false; return; }
        if (nativeUiBlocking) { HideVisuals(); if (Valid(_compact)) _compact!.Visible = false; return; }
        if (!EnsureCreated()) return;
        Layout();
        _panel!.Visible = _open && !nativeUiBlocking;
        _compact!.Visible = !_open && !nativeUiBlocking && ModSettings.ShowTurnSummary && session.Active?.Outcome == CombatOutcome.InProgress;
        if (_compact.Visible && (_compactRevision != session.Revision || _compactFrench != ModText.IsFrench))
        {
            _compactRevision = session.Revision; _compactFrench = ModText.IsFrench;
            var totals = session.QueryTotals(JournalScope.Round, session.Active!.Key, session.Active.LatestRound,
                (session.Run?.Players.Values.FirstOrDefault(p => p.IsLocal) ?? session.Run?.Players.Values.FirstOrDefault())?.Id);
            _compact.Text = (session.Active.LatestRound == 0 ? T("Setup", "Préparation") : T("Round ", "Tour ") + session.Active.LatestRound) + "   ·   " + totals[Stat.DamageDealtHp] +
                T(" dealt", " infligés") + "   ·   −" + totals[Stat.HpLost] + T(" HP", " PV") + "   ·   F5";
        }
        if (!_panel.Visible) return;
        var stamp = (selection.Scope, selection.CombatKey, selection.Round, selection.PlayerId, selection.FollowLive, ModText.IsFrench, storageWarning);
        if (!_invalid && _revision == session.Revision && _selectionStamp == stamp) return;
        _invalid = false; _revision = session.Revision; _selectionStamp = stamp;
        var report = JournalReportBuilder.Build(session, selection, _sources, _page, ModText.IsFrench, _runDetails);
        _page = report.Page; _pages = report.Pages;
        _title!.Text = T("COMBAT JOURNAL", "JOURNAL DE COMBAT");
        _context!.Text = report.Context; _context.TooltipText = report.Context;
        _player!.Text = report.Player; _player.TooltipText = T("Click to switch player. Team is a separate view.", "Cliquer pour changer de joueur. L’équipe est une vue distincte.");
        _sourceButton!.Text = selection.Scope == JournalScope.Run
            ? (_sources ? T("Combats", "Combats") : _runDetails ? T("Sources", "Sources") : T("Details", "Détails"))
            : _sources ? T("Overview", "Bilan") : T("Sources", "Sources");
        _sourceButton.TooltipText = selection.Scope == JournalScope.Run
            ? T("Cycle run views: combat history, all totals, attributed sources.", "Vues de partie : historique des combats, tous les totaux, sources attribuées.")
            : T("Switch between observed details and attributed sources.", "Alterner entre le détail observé et les sources attribuées.");
        _live!.Text = T("Live", "En cours");
        _live.Disabled = selection.FollowLive;
        string[] names = { T("Round", "Tour"), T("Combat", "Combat"), T("Run", "Partie") };
        string[] metrics = { T("ENEMY HP REMOVED", "PV INFLIGÉS AUX ENNEMIS"), T("HP LOST", "PV PERDUS"),
            T("DAMAGE BLOCKED", "DÉGÂTS BLOQUÉS"), T("CARDS PLAYED", "CARTES JOUÉES") };
        for (int i = 0; i < 3; i++)
        {
            _tabs[i].Text = names[i];
            if (_styledScope != (int)selection.Scope)
                HudTheme.StyleButton(_tabs[i], selected: (int)selection.Scope == i, padding: 3);
        }
        _styledScope = (int)selection.Scope;
        for (int i = 0; i < 4; i++) { _hero[i].Text = report.HeroValues[i]; _heroLabels[i].Text = metrics[i]; }
        var selected = selection.SelectedCombat(session.Run);
        int combatIndex = selected == null ? -1 : session.Run!.Combats.IndexOf(selected);
        _previous!.Disabled = selection.Scope == JournalScope.Run || (selection.Scope == JournalScope.Round ? selection.Round <= 0 : combatIndex <= 0);
        _next!.Disabled = selection.Scope == JournalScope.Run || (selection.Scope == JournalScope.Round ? selection.Round >= (selected?.LatestRound ?? 0) : combatIndex >= (session.Run?.Combats.Count ?? 0) - 1);
        _section!.Text = report.Section;
        for (int i = 0; i < _rowButtons.Length; i++)
        {
            bool visible = i < report.Rows.Count;
            _rowButtons[i].Visible = _rowValues[i].Visible = visible; _rowKeys[i] = null;
            if (!visible) continue;
            var row = report.Rows[i]; _rowKeys[i] = row.CombatKey;
            _rowButtons[i].Size = new Vector2(_sources ? 232 : 338, 23);
            _rowValues[i].Position = new Vector2(_sources ? 256 : 360, 309 + i * 24);
            _rowValues[i].Size = new Vector2(_sources ? 278 : 174, 23);
            _rowButtons[i].Text = row.Label; _rowButtons[i].TooltipText = row.Label + "  " + row.Value;
            _rowButtons[i].Disabled = row.CombatKey == null;
            _rowValues[i].Text = row.Value; _rowValues[i].TooltipText = row.Value;
        }
        _pageLabel!.Text = $"{_page + 1} / {_pages}";
        _pagePrevious!.Disabled = _page == 0; _pageNext!.Disabled = _page >= _pages - 1;
        _note!.Text = storageWarning.Length > 0 ? T("Local save unavailable. This session remains in memory.", "Sauvegarde locale indisponible. La session reste en mémoire.") : report.Note;
        _note.TooltipText = _note.Text;
        _footer!.Text = _notice.Length > 0 ? _notice : T("F5 close   ·   Ctrl+F5 export   ·   Observed, not predicted", "F5 fermer   ·   Ctrl+F5 exporter   ·   Réel, pas prévisionnel");
    }
    private bool EnsureCreated()
    {
        if (Valid(_layer) && Valid(_panel) && _layer!.GetParent() == NGame.Instance) return true;
        if (Valid(_layer)) _layer!.QueueFree();
        var game = NGame.Instance;
        if (game == null || !GodotObject.IsInstanceValid(game)) return false;
        _styledScope = -1;
        _layer = new CanvasLayer { Name = "BetterSpireJournal", Layer = HudLayers.Journal };
        _panel = new Panel { Name = "JournalWindow", Size = new Vector2(Width, Height), ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Stop, FocusMode = Control.FocusModeEnum.None, Visible = false };
        _panel.AddThemeStyleboxOverride("panel", HudTheme.Panel(0));
        _layer.AddChild(_panel); game.AddChild(_layer);
        _title = LabelAt("", 18, 12, 452, 24, 15, _accent);
        MakeButton("×", 498, 10, 36, 28, Close);
        for (int i = 0; i < 3; i++)
        {
            int tab = i;
            _tabs[i] = MakeButton("", 18 + i * 172, 46, 164, 30, () =>
            { JournalService.Selection.SelectScope((JournalScope)tab); _page = 0; _sources = false; _runDetails = false; Invalidate(); });
        }
        _context = LabelAt("", 18, 84, 360, 25, 13, _text);
        _previous = MakeButton("‹", 380, 82, 30, 28, () => Move(-1));
        _next = MakeButton("›", 414, 82, 30, 28, () => Move(1));
        _live = MakeButton("", 448, 82, 86, 28, () => { JournalService.Selection.Live(JournalService.Session.Run); _page = 0; Invalidate(); });
        _player = MakeButton("", 18, 120, 396, 27, () => { JournalService.Selection.CyclePlayer(JournalService.Session.Run); _page = 0; Invalidate(); });
        _sourceButton = MakeButton("", 430, 120, 104, 27, () =>
        {
            if (JournalService.Selection.Scope != JournalScope.Run) _sources = !_sources;
            else if (_sources) { _sources = false; _runDetails = false; }
            else if (_runDetails) { _sources = true; _runDetails = false; }
            else _runDetails = true;
            _page = 0; Invalidate();
        });
        for (int i = 0; i < 4; i++)
        {
            float x = 18 + (i % 2) * 264, y = 162 + (i / 2) * 56;
            _heroLabels[i] = LabelAt("", x, y, 248, 16, 11, _quiet);
            _hero[i] = LabelAt("", x, y + 16, 248, 31, 26, i == 1 ? _loss : _text);
        }
        _section = LabelAt("", 18, 280, 378, 20, 12, _accent);
        _pagePrevious = MakeButton("‹", 422, 277, 26, 24, () => { _page = Math.Max(0, _page - 1); Invalidate(); });
        _pageLabel = LabelAt("", 451, 278, 50, 24, 11, _quiet); _pageLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _pageNext = MakeButton("›", 508, 277, 26, 24, () => { _page = Math.Min(_pages - 1, _page + 1); Invalidate(); });
        for (int i = 0; i < _rowButtons.Length; i++)
        {
            int row = i;
            _rowButtons[i] = MakeButton("", 18, 309 + i * 24, 338, 23, () =>
            {
                if (_rowKeys[row] is { } key && JournalService.Session.Run is { } run)
                { JournalService.Selection.SelectCombat(run, key); _page = 0; _sources = false; _runDetails = false; Invalidate(); }
            }, flat: true);
            _rowValues[i] = LabelAt("", 360, 309 + i * 24, 174, 23, 12, _text);
            _rowValues[i].HorizontalAlignment = HorizontalAlignment.Right;
        }
        _note = LabelAt("", 18, 466, 516, 18, 11, _quiet);
        _footer = LabelAt("", 18, 488, 516, 18, 11, _quiet);
        _compact = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, FocusMode = Control.FocusModeEnum.None,
            ClipText = true, HorizontalAlignment = HorizontalAlignment.Right, Visible = false };
        _compact.AddThemeFontSizeOverride("font_size", 13); _compact.AddThemeColorOverride("font_color", _quiet);
        HudTheme.Outline(_compact);
        _layer.AddChild(_compact); _invalid = true; _compactRevision = -1; _layoutView = null;
        return true;
    }
    private Label LabelAt(string text, float x, float y, float width, float height, int fontSize, Color color)
    {
        var label = UiHelpers.CreateLabel(text, color, fontSize);
        label.Position = new Vector2(x,y); label.Size = new Vector2(width,height); label.ClipText = true;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.VerticalAlignment = VerticalAlignment.Center; label.MouseFilter = Control.MouseFilterEnum.Pass;
        _panel!.AddChild(label); return label;
    }
    private Button MakeButton(string text, float x, float y, float width, float height, Action action, bool flat = false)
    {
        var button = new Button { Text = text, Position = new Vector2(x,y), Size = new Vector2(width,height),
            ClipText = true, FocusMode = Control.FocusModeEnum.None, MouseFilter = Control.MouseFilterEnum.Stop };
        HudTheme.StyleButton(button, fontSize: flat ? 12 : 13, padding: 3);
        if (flat)
        {
            var transparent = UiHelpers.CreatePanelStyle(Colors.Transparent, Colors.Transparent, 0, 5, 3);
            button.AddThemeStyleboxOverride("normal", transparent);
            button.AddThemeStyleboxOverride("disabled", transparent);
            button.AddThemeColorOverride("font_disabled_color", HudTheme.Muted);
        }
        button.Pressed += action;
        if (flat) button.Alignment = HorizontalAlignment.Left;
        _panel!.AddChild(button); return button;
    }
    private void Layout()
    {
        var game = NGame.Instance;
        if (game == null || !Valid(_panel)) return;
        Vector2 view = game.GetViewport().GetVisibleRect().Size;
        if (_layoutView == view) return;
        _layoutView = view;
        float scale = Math.Clamp(Math.Min((view.X - 24) / Width, (view.Y - 32) / Height), .1f, 1f);
        _panel!.Scale = new Vector2(scale, scale);
        _panel.Position = new Vector2(Math.Max(12, view.X - Width * scale - 24), Math.Clamp(100f, 16, Math.Max(16, view.Y - Height * scale - 16)));
        _compact!.Position = new Vector2(Math.Max(12, view.X - 450), 92);
        _compact.Size = new Vector2(Math.Max(1, Math.Min(426, view.X - 24)), 24);
    }
    private static bool Valid(GodotObject? obj) => obj != null && GodotObject.IsInstanceValid(obj);
    private static string T(string en, string fr) => ModText.IsFrench ? fr : en;
}
