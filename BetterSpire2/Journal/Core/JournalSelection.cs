#nullable enable
using System;
using System.Linq;

namespace BetterSpire2.Journal.Core;

/// <summary>Browsing old rounds never silently snaps back. "Live" is an explicit selection.</summary>
public sealed class JournalSelection
{
    public JournalScope Scope { get; private set; } = JournalScope.Combat;
    public string? CombatKey { get; private set; }
    public int Round { get; private set; }
    public string? PlayerId { get; private set; }
    public bool FollowLive { get; private set; } = true;
    private bool _playerChosen;
    public void Sync(RunJournal? run)
    {
        if (run == null) return;
        if (!_playerChosen || (PlayerId != null && !run.Players.ContainsKey(PlayerId)))
        {
            PlayerId = run.Players.Values.FirstOrDefault(p => p.IsLocal)?.Id ?? run.Players.Keys.FirstOrDefault();
            _playerChosen = true;
        }
        if (FollowLive || !run.Combats.Any(c => c.Key == CombatKey))
        {
            var last = run.Combats.LastOrDefault();
            CombatKey = last?.Key;
            Round = last?.LatestRound ?? 0;
        }
    }
    public CombatRecord? SelectedCombat(RunJournal? run) => run?.Combats.FirstOrDefault(c => c.Key == CombatKey);
    public void SelectScope(JournalScope scope) => Scope = scope;
    public void Live(RunJournal? run) { FollowLive = true; Sync(run); }
    public void Reset(RunJournal? run)
    {
        CombatKey = null; Round = 0; FollowLive = true; Scope = JournalScope.Combat;
        _playerChosen = false; PlayerId = null; Sync(run);
    }
    public void SelectCombat(RunJournal run, string key)
    {
        var selected = run.Combats.FirstOrDefault(c => c.Key == key);
        if (selected == null) return;
        CombatKey = selected.Key; Round = selected.LatestRound; FollowLive = false; Scope = JournalScope.Combat;
    }
    public void Move(RunJournal? run, int delta)
    {
        if (run == null || Scope == JournalScope.Run || run.Combats.Count == 0) return;
        Sync(run);
        FollowLive = false;
        if (Scope == JournalScope.Round)
        {
            int max = SelectedCombat(run)?.LatestRound ?? 0;
            Round = Math.Clamp(Round + Math.Sign(delta), 0, max);
        }
        else
        {
            int index = run.Combats.FindIndex(c => c.Key == CombatKey);
            var next = run.Combats[Math.Clamp(index + Math.Sign(delta), 0, run.Combats.Count - 1)];
            CombatKey = next.Key; Round = next.LatestRound;
        }
    }
    public void CyclePlayer(RunJournal? run)
    {
        if (run == null || run.Players.Count <= 1) return;
        var ids = run.Players.Values.OrderByDescending(p => p.IsLocal).ThenBy(p => p.Name, StringComparer.Ordinal).Select(p => p.Id).ToList();
        ids.Add(null!); // null is explicitly labelled Team, never the silent default.
        PlayerId = ids[(ids.IndexOf(PlayerId!) + 1) % ids.Count];
        _playerChosen = true;
    }
}
