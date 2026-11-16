#nullable enable
using System;
using BetterSpire2.Core;
using BetterSpire2.Guardian.UI;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

internal static class OverheadVisibilityTests
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string label)
        { checks++; if (!value) throw new Exception("FAIL overhead visibility: " + label); }
        var local = new Creature { IsPlayer = true };
        var teammate = new Creature { IsPlayer = true };
        var pet = new Creature { PetOwner = new Player { Creature = local }, Monster = new MonsterModel() };
        var teammatePet = new Creature { PetOwner = new Player { Creature = teammate }, Monster = new MonsterModel() };
        LocalContext.Me = local;
        for (int mask = 0; mask < 4; mask++)
        {
            bool teammates = (mask & 1) != 0, pets = (mask & 2) != 0;
            ModSettings.GuardianShowTeammates = teammates;
            ModSettings.GuardianShowPets = pets;
            Check(GuardianOverheadHud.ShouldTrack(local), "local player remains visible");
            Check(GuardianOverheadHud.ShouldTrack(teammate) == teammates, "teammate setting");
            Check(GuardianOverheadHud.ShouldTrack(pet) == pets, "own pet setting");
            Check(GuardianOverheadHud.ShouldTrack(teammatePet) == (pets && teammates), "teammate pet needs both settings");
            Check(!GuardianOverheadHud.ShouldTrack(new Creature()), "ownerless monster is not a pet");
        }
        ModSettings.GuardianShowPets = ModSettings.GuardianShowTeammates = true;
        pet.Monster!.IsInteractable = false;
        Check(!GuardianOverheadHud.ShouldTrack(pet), "v113 non-interactable pet has no HP readout");
        pet.Monster = null;
        Check(GuardianOverheadHud.ShouldTrack(pet), "missing monster keeps native fallback");
        pet.MaxHp = 0;
        Check(!GuardianOverheadHud.ShouldTrack(pet), "zero maximum HP pet hidden");
        pet.MaxHp = -1;
        Check(!GuardianOverheadHud.ShouldTrack(pet), "negative maximum HP pet hidden");
        pet.MaxHp = 10;
        pet.IsDead = true;
        Check(!GuardianOverheadHud.ShouldTrack(pet), "dead pet hidden");
        local.IsDead = teammate.IsDead = true;
        Check(!GuardianOverheadHud.ShouldTrack(local), "dead local player hidden");
        Check(!GuardianOverheadHud.ShouldTrack(teammate), "dead teammate hidden");
        Check(!GuardianOverheadHud.ShouldTrack(null!), "missing creature hidden");
        Console.WriteLine($"PASS {checks} source-linked overhead visibility checks (no Godot rendering).");
        return checks;
    }
}

namespace BetterSpire2.Core
{
    internal static class ModSettings
    {
        internal static bool GuardianShowTeammates, GuardianShowPets;
    }
}
namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    internal sealed class Creature
    {
        internal bool IsDead, IsPlayer;
        internal int MaxHp = 10;
        internal Player? PetOwner;
        internal MonsterModel? Monster;
    }
}
namespace MegaCrit.Sts2.Core.Entities.Players
{
    internal sealed class Player { internal Creature Creature = null!; }
}
namespace MegaCrit.Sts2.Core.Models
{
    internal sealed class MonsterModel { internal bool IsInteractable = true; }
}
namespace MegaCrit.Sts2.Core.Context
{
    internal static class LocalContext
    {
        internal static Creature? Me;
        internal static bool IsMe(Creature creature) => ReferenceEquals(Me, creature);
    }
}
