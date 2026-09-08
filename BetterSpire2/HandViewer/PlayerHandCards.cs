#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace BetterSpire2.HandViewer;

public static partial class TeammateHandViewer
{
    private sealed partial class PlayerHandSection
    {
        private readonly record struct CardVisual(string Name, string Cost, string Description,
            Texture2D? Portrait, Color TypeColor, int Scale);
        private sealed class CardView
        {
            internal CardVisual Visual;
            internal Control Control;
            internal int Generation;
            internal TextureRect Portrait;
            internal Label Missing, Cost;
            internal StyleBoxFlat Border;
            internal CardView(CardVisual visual, Control control, TextureRect portrait,
                Label missing, Label cost, StyleBoxFlat border)
            { Visual = visual; Control = control; Portrait = portrait; Missing = missing; Cost = cost; Border = border; }
        }
        private static CardVisual ReadVisual(CardModel card)
        {
            string name = card.Title;
            var energy = card.EnergyCost;
            // Display costs preserve the negative sentinel; GetResolved clamps it to zero.
            int resolved = energy.GetWithModifiers(CostModifiers.All);
            string cost = energy.CostsX ? "X" : resolved < 0 ? "—" : resolved.ToString();
            int stars = card.GetStarCostWithModifiers();
            if (card.HasStarCostX || stars > 0) cost += " · " + (card.HasStarCostX ? "X" : stars.ToString()) + " ★";
            Color color = card.Type switch
            {
                CardType.Attack => AttackColor, CardType.Skill => SkillColor,
                CardType.Power => PowerColor, CardType.Status => StatusColor, CardType.Curse => CurseColor,
                _ => DefaultCardColor
            };
            return new CardVisual(name, cost, ResolveDescription(card), card.Portrait, color, ModSettings.CardScalePercent);
        }
        private void SyncCards(IReadOnlyList<CardModel> cards)
        {
            _viewGeneration++;
            int count = cards.Count; // Do not silently truncate modded hands to twelve cards.
            for (int i = 0; i < count; i++)
            {
                var card = cards[i];
                CardVisual visual;
                try { visual = ReadVisual(card); }
                catch (Exception ex)
                {
                    LogError(ex);
                    visual = new CardVisual(ModText.T("Card unavailable"), "—",
                        ModText.T("Card preview unavailable. Actual combat statistics remain available."),
                        null, HudTheme.Muted, ModSettings.CardScalePercent);
                }
                if (!_cardViews.TryGetValue(card, out var view) || !GodotObject.IsInstanceValid(view.Control))
                {
                    if (view != null) FreeCard(view.Control);
                    view = BuildCompactCard(visual);
                    _cardViews[card] = view;
                    _cardRow!.AddChild(view.Control);
                }
                else if (view.Visual != visual) UpdateCard(view, visual);
                view.Generation = _viewGeneration;
                if (view.Control.GetIndex() != i) _cardRow!.MoveChild(view.Control, i);
            }
            _removedViews.Clear();
            foreach (var pair in _cardViews)
                if (pair.Value.Generation != _viewGeneration) _removedViews.Add(pair.Key);
            foreach (var card in _removedViews) { FreeCard(_cardViews[card].Control); _cardViews.Remove(card); }
        }
        private static void FreeCard(Control control)
        {
            if (!GodotObject.IsInstanceValid(control)) return;
            control.GetParent()?.RemoveChild(control);
            control.QueueFree();
        }

        private static CardView BuildCompactCard(CardVisual visual)
        {
            var card = new Control
            {
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
                MouseFilter = Control.MouseFilterEnum.Stop, ClipContents = true
            };
            var image = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            image.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); card.AddChild(image);
            var missing = UiHelpers.CreateLabel("?", HudTheme.Muted, 12, HorizontalAlignment.Center);
            missing.VerticalAlignment = VerticalAlignment.Center;
            missing.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            missing.MouseFilter = Control.MouseFilterEnum.Ignore;
            HudTheme.Outline(missing); card.AddChild(missing);
            var edge = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
            edge.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var border = UiHelpers.CreatePanelStyle(Colors.Transparent, new Color(visual.TypeColor, .7f), 1, 2, 0);
            edge.AddThemeStyleboxOverride("panel", border); card.AddChild(edge);
            var cost = UiHelpers.CreateLabel("", Colors.White, 10);
            cost.MouseFilter = Control.MouseFilterEnum.Ignore;
            cost.Position = new Vector2(2, 0); cost.ClipText = true;
            cost.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            cost.AddThemeStyleboxOverride("normal", UiHelpers.CreatePanelStyle(new Color(0, 0, 0, .72f), Colors.Transparent, 0, 2, 0));
            HudTheme.Outline(cost); card.AddChild(cost);
            var view = new CardView(visual, card, image, missing, cost, border);
            UpdateCard(view, visual, force: true);
            return view;
        }
        private static void UpdateCard(CardView view, CardVisual visual, bool force = false)
        {
            // Description-only changes touch no Godot properties or node structure.
            if (force || view.Visual.Scale != visual.Scale)
            {
                view.Control.CustomMinimumSize = new Vector2(CardWidth, PortraitHeight);
                view.Cost.Size = new Vector2(Math.Max(1, CardWidth - 4), 14);
            }
            if (force || view.Visual.Portrait != visual.Portrait)
            {
                view.Portrait.Texture = visual.Portrait;
                view.Portrait.Visible = visual.Portrait != null;
                view.Missing.Visible = visual.Portrait == null;
            }
            if (force || view.Visual.Cost != visual.Cost)
            { view.Cost.Text = visual.Cost; view.Cost.Visible = visual.Cost.Length > 0; }
            if (force || view.Visual.TypeColor != visual.TypeColor)
                view.Border.BorderColor = new Color(visual.TypeColor, .7f);
            view.Visual = visual;
        }

        private void ClearCards()
        {
            foreach (var view in _cardViews.Values) FreeCard(view.Control);
            _cardViews.Clear(); _removedViews.Clear();

        }

        internal bool TryGetCardTooltip(Vector2 point, out string text, out Rect2 rect)
        {
            foreach (var view in _cardViews.Values)
            {
                if (!Hits(view.Control, point)) continue;
                var visual = view.Visual;
                text = visual.Name + " · " + visual.Cost + "\n\n" + visual.Description;
                rect = view.Control.GetGlobalRect();
                return true;
            }
            text = "";
            rect = default;
            return false;
        }

    }
}
