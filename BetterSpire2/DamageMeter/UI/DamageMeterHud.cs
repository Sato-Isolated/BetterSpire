#nullable enable
using System;
using System.Collections.Generic;
using BetterSpire2.DamageMeter.Core;
using BetterSpire2.Journal.Core;
using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace BetterSpire2.DamageMeter.UI;

/// <summary>Text and hairline bars. Only the unlocked title captures a drag; all damage rows are click-through.</summary>
internal sealed class DamageMeterHud
{
    private CanvasLayer? _layer;
    private Control? _root, _content;
    private DockableOverlayController? _layout;
    private Vector2 _physicalSize;
    private bool _locked;
    internal bool IsVisible => Valid(_root) && _root!.IsVisibleInTree();
    internal bool IsInteracting => _layout?.IsInteracting == true;
    // Includes click-through rows and a locked title. This does NOT consume input.
    internal bool IsPointInVisibleArea(Vector2 point) => IsVisible && _root!.GetGlobalRect().HasPoint(point);
    internal bool IsPointInInteractionArea(Vector2 point) => IsVisible && !ModSettings.DamageMeterLocked
        && _layout?.IsPointInInteractionArea(point) == true;
    internal void CancelPointerInteraction()
    {
        if (IsInteracting) _layout?.Persist();
        _layout?.CancelInteraction();
    }
    internal void ResetPointerCursor() => _layout?.ResetPointerCursor();
    internal bool HandleInput(InputEvent input)
    {
        if (!IsVisible || ModSettings.DamageMeterLocked) return false;
        bool active = IsInteracting;
        _layout?.HandleInput(input);
        return active || IsInteracting;
    }
    internal void ResetLayout()
    {
        _layout?.CancelInteraction();
        _layout?.SetNormalizedPosition(new Vector2(1, .12f));
        _layoutStamp = null;
        Invalidate();
    }
    internal void Stop()
    {
        Hide(); _layout?.Dispose(); _layout = null;
        if (Valid(_layer)) _layer!.QueueFree();
        _layer = null; _root = _content = null;
        _layoutStamp = null; _invalid = true;
    }
    private Label? _heading, _footer;
    private readonly Label[] _rank = new Label[5], _name = new Label[5], _value = new Label[5], _share = new Label[5];
    private readonly ColorRect[] _track = new ColorRect[5], _bar = new ColorRect[5];
    private long _revision = -1;
    private (string Run, string? Combat, DamageMeterScope Scope, bool French, bool Block, bool Bars) _stamp;
    private IReadOnlyList<DamageMeterRow> _rows = Array.Empty<DamageMeterRow>();
    private (Vector2 View, int Rows, bool Multi, bool Footer, int Scale, int X, int Y)? _layoutStamp;
    private bool _invalid = true;
    private DamageMeterSnapshot? _snapshot;
    private readonly Color _text = HudTheme.Text, _quiet = HudTheme.Muted;
    private readonly Color _local = HudTheme.Accent, _line = HudTheme.Info;

    internal void Invalidate() => _invalid = true;
    internal void Hide()
    {
        if (IsInteracting) _layout?.Persist();
        _layout?.CancelInteraction();
        if (Valid(_root)) _root!.Visible = false;
    }
    internal void Render(JournalSession session, DamageMeterScope scope, bool visible)
    {
        if (!visible || session.Run == null || session.Run.Players.Count == 0) { Hide(); return; }
        if (!EnsureCreated()) return;
        bool french = ModText.IsFrench;
        var stamp = (session.Run.Key, session.Active?.Key, scope, french, ModSettings.DamageMeterIncludeBlock, ModSettings.DamageMeterShowBars);
        if (_invalid || _revision != session.DamageRevision || _stamp != stamp)
        {
            using var measurement = PerformanceProbe.Measure(ProbeSection.DamageMeter);
            _snapshot = DamageMeterBuilder.BuildLive(session, ModSettings.DamageMeterIncludeBlock, scope);
            _rows = DamageMeterBuilder.VisibleRows(_snapshot, french);
            _revision = session.DamageRevision; _stamp = stamp; _invalid = false;
            UpdateText(_snapshot, french);
        }
        var data = _snapshot!;
        var rows = _rows;
        var view = NGame.Instance!.GetViewport().GetVisibleRect().Size;
        var layoutStamp = (view, rows.Count, data.IsMultiplayer, data.UnattributedDamage > 0,
            ModSettings.DamageMeterScalePercent, ModSettings.DamageMeterXPercent, ModSettings.DamageMeterYPercent);
        UpdateInteractionState();
        _root!.Visible = true;
        if (_layoutStamp == layoutStamp) return;
        // Do not cancel a captured drag when a damage event changes the number of rows.
        if (IsInteracting) return;
        var placement = DamageMeterLayout.Place(view.X, view.Y, rows.Count, data.IsMultiplayer,
            data.UnattributedDamage > 0, ModSettings.DamageMeterScalePercent,
            ModSettings.DamageMeterXPercent, ModSettings.DamageMeterYPercent);
        if (placement.Scale <= 0) { Hide(); return; }
        bool positionChanged = !_layoutStamp.HasValue || _layoutStamp.Value.X != layoutStamp.Item6 ||
            _layoutStamp.Value.Y != layoutStamp.Item7;
        _layoutStamp = layoutStamp;
        _content!.Scale = new Vector2(placement.Scale, placement.Scale);
        _content.Size = new Vector2(placement.Width, placement.Height);
        _physicalSize = _content.Size * placement.Scale;
        if (_layout == null)
        {
            var game = NGame.Instance!;
            // Geometry is physical viewport space on an unscaled wrapper, just like the clock.
            _layout = new DockableOverlayController(_root, _heading!, game.GetViewport(), game,
                _physicalSize, _physicalSize, false, ModSettings.GetDamageMeterLayout,
                ModSettings.SaveDamageMeterLayout, handleOnly: true);
            // Preserve the old 3.4.x on-screen position before migrating to the clock's safe-area coordinates.
            bool migrate = ModSettings.NeedsDamageMeterMigration;
            _layout.Initialize(_physicalSize, (safe, size) => migrate
                ? new Vector2(placement.X, placement.Y)
                : safe.Position + (safe.Size - size) *
                    new Vector2(ModSettings.DamageMeterXPercent / 100f, ModSettings.DamageMeterYPercent / 100f));
            if (migrate) _layout.Persist();
        }
        else
        {
            _layout.ApplyGeometry(_physicalSize, _physicalSize, _physicalSize);
            if (positionChanged)
            {
                var saved = ModSettings.GetDamageMeterLayout();
                _layout.SetNormalizedPosition(saved.HasPosition ? saved.Position :
                    new Vector2(ModSettings.DamageMeterXPercent / 100f, ModSettings.DamageMeterYPercent / 100f));
            }
        }
    }
    private void UpdateInteractionState()
    {
        bool locked = ModSettings.DamageMeterLocked;
        if (_locked != locked && IsInteracting) { _layout?.Persist(); _layout?.CancelInteraction(); }
        _locked = locked;
        _heading!.MouseFilter = locked ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop;
        _heading.MouseDefaultCursorShape = locked ? Control.CursorShape.Arrow : Control.CursorShape.Move;
        _heading.TooltipText = ModText.T(locked ? "Meter position locked" : "Drag the title to move") +
            (_snapshot?.IsPartial == true ? "\n" + ModText.T("Partial data: some damage could not be observed.") : "") +
            "\n" + T("Percentages use attributed damage only. Poison is shared by observed stack contribution.",
                "Pourcentages sur les dégâts attribués uniquement. Poison réparti selon les contributions observées.");
    }
    private void UpdateText(DamageMeterSnapshot data, bool french)
    {
        var rows = _rows;
        bool multi = data.IsMultiplayer;
        float width = multi ? 276 : 216;
        _heading!.Text = data.Scope == DamageMeterScope.Combat
            ? T("DAMAGE · COMBAT", "DÉGÂTS · COMBAT") : T("DAMAGE · RUN", "DÉGÂTS · RUN");
        if (data.IncludesBlock) _heading.Text += T(" · HP + BLOCK", " · PV + BLOC");
        _heading.Size = new Vector2(width, 18);
        _heading.AddThemeColorOverride("font_color", data.IsPartial ? _local : _quiet);
        for (int i = 0; i < _name.Length; i++)
        {
            bool used = i < rows.Count;
            _rank[i].Visible = _name[i].Visible = _value[i].Visible = _share[i].Visible =
                _track[i].Visible = _bar[i].Visible = used;
            if (!used) continue;
            var row = rows[i];
            float y = 20 + i * (multi ? 29 : 24);
            bool bars = multi && ModSettings.DamageMeterShowBars && !row.IsGroup;
            _rank[i].Visible = multi && !row.IsGroup;
            _share[i].Visible = multi;
            _track[i].Visible = _bar[i].Visible = bars;
            SetRect(_rank[i], 0, y, 20, 22);
            SetRect(_name[i], multi ? 23 : 0, y, multi ? 108 : 121, 22);
            SetRect(_value[i], multi ? 132 : 124, y, multi ? 88 : 92, 22);
            SetRect(_share[i], 223, y, 53, 22);
            _rank[i].Text = row.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _name[i].Text = DamageMeterText.Name(row.Name, french);
            _value[i].Text = DamageMeterText.Number(row.Damage, french);
            _share[i].Text = DamageMeterText.Share(row.ShareTenths, french);
            Color color = row.IsLocal ? _local : row.IsGroup ? _quiet : _text;
            _name[i].AddThemeColorOverride("font_color", color);
            _value[i].AddThemeColorOverride("font_color", color);
            SetRect(_track[i], 23, y + 24, 253, 1.5f);
            SetRect(_bar[i], 23, y + 24, Math.Clamp((float)row.RelativeToLeader, 0, 1) * 253, 1.5f);
            _bar[i].Visible = bars && row.Damage > 0;
            _bar[i].Color = row.IsLocal ? _local : _line;
        }
        _footer!.Visible = data.UnattributedDamage > 0;
        _footer.Text = T("Unattributed · ", "Sans attribution · ") + DamageMeterText.Number(data.UnattributedDamage, french);
        SetRect(_footer, 0, 20 + rows.Count * (multi ? 29 : 24), width, 20);
    }
    private bool EnsureCreated()
    {
        var game = NGame.Instance;
        if (Valid(_layer) && Valid(_root) && _layer!.GetParent() == game) return true;
        _layout?.Dispose(); _layout = null;
        if (Valid(_layer)) _layer!.QueueFree();
        if (game == null || !GodotObject.IsInstanceValid(game) || !game.IsInsideTree()) return false;
        _layer = new CanvasLayer { Name = "BetterSpireRunDamageMeter", Layer = HudLayers.DamageMeter };
        _root = new Control { Name = "RunDamageMeter", MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None, Visible = false };
        _content = new Control { Name = "MeterContent", MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None };
        _root.AddChild(_content);
        _layer.AddChild(_root); game.AddChild(_layer);
        _heading = MakeLabel(11, _quiet); _footer = MakeLabel(11, _quiet);
        for (int i = 0; i < _name.Length; i++)
        {
            _rank[i] = MakeLabel(12, _quiet); _name[i] = MakeLabel(14, _text);
            _value[i] = MakeLabel(14, _text, HorizontalAlignment.Right);
            _share[i] = MakeLabel(12, _quiet, HorizontalAlignment.Right);
            _track[i] = MakeBar(new Color(.45f, .47f, .45f, .20f)); _bar[i] = MakeBar(_line);
        }
        _invalid = true; _layoutStamp = null;
        return true;
    }
    private Label MakeLabel(int size, Color color, HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, FocusMode = Control.FocusModeEnum.None,
            ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        HudTheme.Outline(label);
        label.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        _content!.AddChild(label); return label;
    }
    private ColorRect MakeBar(Color color)
    {
        var rect = new ColorRect { Color = color, MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None };
        _content!.AddChild(rect); return rect;
    }
    private static void SetRect(Control control, float x, float y, float width, float height)
    { control.Position = new Vector2(x, y); control.Size = new Vector2(width, height); }
    private static bool Valid(GodotObject? obj) => obj != null && GodotObject.IsInstanceValid(obj);
    private static string T(string en, string fr) => ModText.IsFrench ? fr : en;
}
