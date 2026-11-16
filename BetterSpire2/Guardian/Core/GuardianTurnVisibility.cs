namespace BetterSpire2.Guardian.Core;

public static class GuardianTurnVisibility
{
    // Card/potion animations and PlayerActionsDisabled affect calculation, not turn visibility.
    public static bool ShouldShow(bool starting, bool enemyTurn, bool endingTurn,
        bool playPhase, bool participant, bool readyToEndTurn) =>
        !starting && !enemyTurn && !endingTurn && playPhase && participant && !readyToEndTurn;
}
