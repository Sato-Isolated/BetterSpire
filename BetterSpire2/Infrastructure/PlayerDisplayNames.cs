#nullable enable
using System.Globalization;
using MegaCrit.Sts2.Core.Entities.Players;

namespace BetterSpire2.Infrastructure;

/// <summary>Reuse the journal's bounded Steam-name cache. Never resolve platform IDs from UI rendering.</summary>
internal static class PlayerDisplayNames
{
    internal static string Nickname(Player player)
    {
        var run = JournalService.Session.Run;
        string id = player.NetId.ToString(CultureInfo.InvariantCulture);
        return run != null && run.Players.TryGetValue(id, out var registered) ? registered.Name : "";
    }
}
