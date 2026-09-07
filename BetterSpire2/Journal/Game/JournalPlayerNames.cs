#nullable enable
using System.Globalization;
using BetterSpire2.Journal.Core;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace BetterSpire2.Journal.Game;

/// <summary>Game-thread platform lookup, decoupled from frame rendering and damage events.</summary>
internal sealed class JournalPlayerNames
{
    private readonly PlayerNameCache _cache = new();
    private ulong _nextPoll;
    private int _playerCount = -1;
    internal void Reset() { _cache.Clear(); _nextPoll = 0; _playerCount = -1; }

    internal void Refresh(RunState state, JournalSession session, ulong now, bool force = false)
    {
        if (session.Run == null || (!force && now < _nextPoll && _playerCount == state.Players.Count)) return;
        _nextPoll = now + 1000;
        _playerCount = state.Players.Count;
        foreach (var player in state.Players)
        {
            string id = player.NetId.ToString(CultureInfo.InvariantCulture);
            bool local = LocalContext.IsMe(player) || state.Players.Count == 1;
            string? nickname = _cache.Resolve(id, now, () => ReadNickname(player, local));
            string name = nickname ?? (ModText.IsFrench ? "Joueur " : "Player ") + (state.GetPlayerSlotIndex(player) + 1);
            if (session.Run.Players.TryGetValue(id, out var old) && old.Name == name && old.IsLocal == local) continue;
            session.RegisterPlayer(new JournalPlayer { Id = id, Name = name, IsLocal = local });
        }
    }
    private static string? ReadNickname(Player player, bool local)
    {
        // PrimaryPlatform checks Steam initialization in the supplied DLL. Do not call Steam when unavailable.
        var primary = PlatformUtil.PrimaryPlatform;
        if (primary != PlatformType.Steam) return null;
        var manager = RunManager.Instance;
        var network = manager?.NetService?.Platform;
        if (!local && network != PlatformType.Steam && manager?.History?.PlatformType != PlatformType.Steam) return null;
        // Solo's NetId can be 1. It is NOT the local Steam ID; remote Steam peers keep their own NetId.
        ulong steamId = local ? PlatformUtil.GetLocalPlayerId(PlatformType.Steam) : player.NetId;
        if (steamId == 0) return null;
        // Plain Godot Labels do not parse BBCode. GetPlayerName would add visible escape markup.
        return PlayerNameText.Resolved(PlatformUtil.GetPlayerNameRaw(PlatformType.Steam, steamId),
            steamId.ToString(CultureInfo.InvariantCulture));
    }
}
