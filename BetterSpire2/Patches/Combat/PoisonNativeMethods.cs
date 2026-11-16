#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace BetterSpire2.Patches.Combat;

internal static class PoisonNativeMethods
{
    internal static MethodInfo? SingleDamage() => AccessTools.Method(typeof(CreatureCmd),
        nameof(CreatureCmd.Damage), new[] { typeof(PlayerChoiceContext), typeof(Creature), typeof(decimal),
            typeof(ValueProp), typeof(CardModel), typeof(CardPlay) });
    internal static MethodInfo? ManyDamage() => AccessTools.Method(typeof(CreatureCmd),
        nameof(CreatureCmd.Damage), new[] { typeof(PlayerChoiceContext), typeof(IEnumerable<DamageTargetData>), typeof(decimal),
            typeof(ValueProp), typeof(Creature), typeof(CardModel), typeof(CardPlay), typeof(ModifyDamageHookType) });
    internal static MethodInfo? MoveNext(MethodInfo? method) => method?
        .GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType.GetMethod("MoveNext",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    internal static MethodInfo? PoisonMoveNext() => MoveNext(AccessTools.Method(typeof(PoisonPower),
        nameof(PoisonPower.Trigger)));
}
