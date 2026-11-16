#nullable enable

namespace BetterSpire2.Guardian.Core;

/// <summary>Keeps the last projection visible while player actions recalculate it.</summary>
public sealed class OverheadDisplayState
{
    public ActorForecast? Actor { get; private set; }
    public bool IsPending { get; private set; } = true;
    private OverheadReadout _forecast;

    public void Bind(ActorForecast? actor, bool partial, bool branchEnumerationComplete)
    {
        if (actor == null) return;
        Actor = actor;
        IsPending = false;
        _forecast = OverheadPresentation.Format(actor, partial, branchEnumerationComplete);
    }

    public void Invalidate() => SetStatus(false);

    public void SetStatus(bool error)
    {
        IsPending = true;
    }

    public OverheadReadout Read()
    {
        return Actor == null ? new("", OverheadSeverity.Uncertain) : _forecast;
    }
}
