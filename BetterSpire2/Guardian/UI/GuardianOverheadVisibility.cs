#nullable enable
using BetterSpire2.Core;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace BetterSpire2.Guardian.UI;

internal sealed partial class GuardianOverheadHud
{
    internal static bool ShouldTrack(Creature creature)
    {
        if (creature == null || creature.IsDead) return false;
        if (creature.IsPlayer) return ModSettings.GuardianShowTeammates || LocalContext.IsMe(creature);
        return ModSettings.GuardianShowPets && creature.PetOwner is { } owner && creature.MaxHp > 0 &&
            creature.Monster?.IsInteractable != false &&
            (ModSettings.GuardianShowTeammates || LocalContext.IsMe(owner.Creature));
    }
}
