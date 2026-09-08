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
            internal CardView(CardVisual visual, Control control) { Visual = visual; Control = control; }
        }
        private static CardVisual ReadVisual(CardModel card)
        {
            string name = card.Title;
            if (card.IsUpgraded && !name.EndsWith("+", StringComparison.Ordinal)) name += "+";
            var energy = card.EnergyCost;
            int resolved = energy.GetResolved();
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
                if (!_cardViews.TryGetValue(card, out var view) || !GodotObject.IsInstanceValid(view.Control) || view.Visual != visual)
                {
                    if (view != null) FreeCard(view.Control);
                    var control = BuildCompactCard(visual.Portrait, visual.Cost, visual.TypeColor);
                    view = new CardView(visual, control);
                    _cardViews[card] = view;
                    _cardRow!.AddChild(control);
                }
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

        private static Control BuildCompactCard(Texture2D? portrait, string costText, Color typeColor)
        {
            var card = new Control
            {
                CustomMinimumSize = new Vector2(CardWidth, PortraitHeight),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
                MouseFilter = Control.MouseFilterEnum.Stop,
                ClipContents = true
            };
            if (portrait != null)
            {
                var image = new TextureRect
                {
                    Texture = portrait, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                image.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                card.AddChild(image);
            }
            else
            {
                var unavailable = UiHelpers.CreateLabel("?", HudTheme.Muted, 12, HorizontalAlignment.Center);
                unavailable.VerticalAlignment = VerticalAlignment.Center;
                unavailable.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                HudTheme.Outline(unavailable); card.AddChild(unavailable);
            }
            var edge = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
            edge.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            edge.AddThemeStyleboxOverride("panel", UiHelpers.CreatePanelStyle(Colors.Transparent, new Color(typeColor, .7f), 1, 2, 0));
            card.AddChild(edge);
            if (costText.Length > 0)
            {
                // The full cost (including stars and X) also stays in the tooltip.
                // Clip the badge within the thumbnail instead of making a long cost widen it.
                var cost = UiHelpers.CreateLabel(costText, Colors.White, 10);
                cost.Position = new Vector2(2, 0);
                cost.Size = new Vector2(Math.Max(1, CardWidth - 4), 14);
                cost.ClipText = true;
                cost.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                cost.AddThemeStyleboxOverride("normal", UiHelpers.CreatePanelStyle(new Color(0, 0, 0, .72f), Colors.Transparent, 0, 2, 0));
                HudTheme.Outline(cost); card.AddChild(cost);
            }
            return card;
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
