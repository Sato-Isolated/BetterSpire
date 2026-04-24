#nullable enable
using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;

namespace BetterSpire2.Patches.Input;

/// <summary>
/// Wires BetterSpire hotkeys and overlay input into the game's global input loop.
/// </summary>
[HarmonyPatch(typeof(NGame), nameof(NGame._Input))]
internal static class NGame_Input_Patch
{
	[HarmonyPrepare]
	private static bool Prepare(MethodBase original)
	{
		if (original is not null)
		{
			return true;
		}

		ModLog.Info($"Target method not found for {nameof(NGame_Input_Patch)} - patch skipped.");
		return false;
	}

	[HarmonyPostfix]
	private static void Postfix(InputEvent inputEvent)
	{
		try
		{
			ClockDisplay.Update();
			ClockDisplay.HandleInput(inputEvent);
			ModConfigBridge.TryRegister();
			if (inputEvent is InputEventKey)
			{
				NGame? instance = NGame.Instance;
				if (instance != null && instance.FeedbackScreen?.Visible == true)
				{
					return;
				}
			}
			if (inputEvent is InputEventKey inputEventKey && inputEventKey.Keycode == Key.F1 && inputEventKey.Pressed && !inputEventKey.IsEcho())
			{
				SettingsMenu.Toggle();
				return;
			}
			if (inputEvent is InputEventKey inputEventKey2 && inputEventKey2.Keycode == Key.F3 && inputEventKey2.Pressed && !inputEventKey2.IsEcho())
			{
				DeckTracker.Toggle();
				return;
			}
			if (inputEvent is InputEventKey { Pressed: not false } inputEventKey3 && !inputEventKey3.IsEcho())
			{
				if (inputEventKey3.Keycode == Key.Pagedown)
				{
					DeckTracker.NextPage();
					return;
				}
				if (inputEventKey3.Keycode == Key.Pageup)
				{
					DeckTracker.PrevPage();
					return;
				}
			}
			if ((inputEvent is InputEventMouseButton || inputEvent is InputEventMouseMotion) ? true : false)
			{
				SettingsMenu.HandleMouseInput(inputEvent);
				DeckTracker.HandleMouseInput(inputEvent);
				TurnSummaryTracker.HandleMouseInput(inputEvent);
			}
		}
		catch (Exception ex)
		{
			ModLog.Error(nameof(NGame_Input_Patch), ex);
		}
	}
}
