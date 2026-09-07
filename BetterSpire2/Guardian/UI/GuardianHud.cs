#nullable enable
using System.Collections.Generic;
using BetterSpire2.Guardian.Core;
using BetterSpire2.Guardian.Game;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace BetterSpire2.Guardian.UI;

/// <summary>The normal HUD is text above creatures. Diagnostics exist only on explicit F2.</summary>
internal sealed class GuardianHud
{
    private readonly GuardianOverheadHud _overhead = new();
    private readonly GuardianDetailsPanel _details = new();

    internal bool DetailsVisible => _details.DetailsVisible;
    internal void ToggleDetails() => _details.ToggleDetails();
    internal void ChangePage(int delta) => _details.ChangePage(delta);
    internal void MarkStale() => _overhead.MarkStale();
    internal void Hide() { _overhead.Hide(); _details.Hide(); }
    internal void ResetCombat() { _overhead.Clear(); _details.Close(); }

    internal void Render(ForecastResult result, CombatLedger ledger,
        IReadOnlyDictionary<Creature, string> actorIds)
    {
        _overhead.Render(result, actorIds);
        _details.Render(result, ledger);
    }

    internal void RenderStatus(string message, string explanation, CombatLedger? ledger = null, bool error = false)
    {
        _overhead.RenderStatus();
        _details.RenderStatus(message, explanation, ledger, error);
    }
}
