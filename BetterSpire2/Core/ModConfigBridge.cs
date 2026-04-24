#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes;

namespace BetterSpire2.Core;

internal static class ModConfigBridge
{
	private static bool _detected;

	private static bool _available;

	private static Type? _apiType;

	private static Type? _entryType;

	private static Type? _configType;

	private static bool _registered;

	internal static bool IsAvailable
	{
		get
		{
			if (!_detected)
			{
				_detected = true;
				_apiType = Type.GetType("ModConfig.ModConfigApi, ModConfig");
				_entryType = Type.GetType("ModConfig.ConfigEntry, ModConfig");
				_configType = Type.GetType("ModConfig.ConfigType, ModConfig");
				_available = _apiType != null && _entryType != null && _configType != null;
			}
			return _available;
		}
	}

	internal static void TryRegister()
	{
		if (_registered)
		{
			return;
		}
		if (!IsAvailable)
		{
			_registered = true;
			return;
		}
		try
		{
			SceneTree? tree = NGame.Instance?.GetTree();
			if (tree == null)
			{
				return;
			}

			int frames = 0;
			void Handler()
			{
				if (++frames >= 2)
				{
					tree.ProcessFrame -= Handler;
					Register();
				}
			}

			tree.ProcessFrame += Handler;
		}
		catch (Exception ex)
		{
			ModLog.Error("ModConfigBridge.TryRegister", ex);
		}
	}

	private static void Register()
	{
		if (_registered)
		{
			return;
		}
		_registered = true;
		try
		{
			List<object> list = new List<object>();
			list.Add(MakeHeader("Combat HUD"));
			list.Add(MakeToggle("MultiHitTotals", "Multi-Hit Totals (per enemy)", ModSettings.MultiHitTotals, delegate(object v)
			{
				ModSettings.MultiHitTotals = (bool)v;
				ModSettings.Save();
			}));
			list.Add(MakeToggle("PlayerDamageTotal", "Total Incoming Damage (above player)", ModSettings.PlayerDamageTotal, delegate(object v)
			{
				ModSettings.PlayerDamageTotal = (bool)v;
				ModSettings.Save();
				if ((bool)v)
				{
					DamageTracker.Recalculate();
				}
				else
				{
					DamageTracker.Hide();
				}
			}));
			list.Add(MakeToggle("ShowExpectedHp", "Show Expected HP After Damage", ModSettings.ShowExpectedHp, delegate(object v)
			{
				ModSettings.ShowExpectedHp = (bool)v;
				ModSettings.Save();
				DamageTracker.Recalculate();
			}));
			list.Add(MakeToggle("ShowTurnSummary", "Show Turn Summary Tracker", ModSettings.ShowTurnSummary, delegate(object v)
			{
				ModSettings.ShowTurnSummary = (bool)v;
				ModSettings.Save();
				TurnSummaryTracker.SyncVisibility();
			}));
			list.Add(MakeHeader("Multiplayer"));
			list.Add(MakeToggle("ShowTeammateHand", "Show Teammate Hand in Combat (F3)", ModSettings.ShowTeammateHand, delegate(object v)
			{
				ModSettings.ShowTeammateHand = (bool)v;
				ModSettings.Save();
				if (!(bool)v)
				{
					DeckTracker.Hide();
				}
			}));
			list.Add(MakeToggle("AlwaysShowTeammateHand", "Keep Open (no click-outside close)", ModSettings.AlwaysShowTeammateHand, delegate(object v)
			{
				ModSettings.AlwaysShowTeammateHand = (bool)v;
				ModSettings.Save();
			}));
			list.Add(MakeToggle("AutoShowTeammateHand", "Auto-Show When Entering Combat", ModSettings.AutoShowTeammateHand, delegate(object v)
			{
				ModSettings.AutoShowTeammateHand = (bool)v;
				ModSettings.Save();
			}));
			list.Add(MakeToggle("HideOwnHand", "Hide Own Hand (teammates only)", ModSettings.HideOwnHand, delegate(object v)
			{
				ModSettings.HideOwnHand = (bool)v;
				ModSettings.Save();
				DeckTracker.RefreshIfVisible();
			}));
			list.Add(MakeToggle("CompactHandViewer", "Compact View (stats only, no cards)", ModSettings.CompactHandViewer, delegate(object v)
			{
				ModSettings.CompactHandViewer = (bool)v;
				ModSettings.Save();
				DeckTracker.RefreshIfVisible();
			}));
			list.Add(MakeEntry("CardScalePercent", "Card Size (%)", ConfigTypeValue("Slider"), (float)ModSettings.CardScalePercent, 50f, 200f, 10f, "F0", null, null, null, delegate(object v)
			{
				ModSettings.CardScalePercent = (int)(float)v;
				ModSettings.Save();
				DeckTracker.RefreshIfVisible();
			}));
			list.Add(MakeHeader("Gameplay"));
			list.Add(MakeToggle("InstantFastMode", "Instant Fast Mode (combat only)", ModSettings.InstantFastMode, delegate(object v)
			{
				ModSettings.InstantFastMode = (bool)v;
				ModSettings.Save();
				if (!(bool)v)
				{
					InstantSpeedHelper.OnCombatEnd();
				}
				else if (CombatManager.Instance?.DebugOnlyGetState() != null)
				{
					InstantSpeedHelper.OnCombatStart();
				}
			}));
			list.Add(MakeToggle("SkipSplash", "Skip Splash Screen", ModSettings.SkipSplash, delegate(object v)
			{
				ModSettings.SkipSplash = (bool)v;
				ModSettings.Save();
			}));
			list.Add(MakeToggle("ShowClock", "Show Clock", ModSettings.ShowClock, delegate(object v)
			{
				ModSettings.ShowClock = (bool)v;
				ModSettings.Save();
				ClockDisplay.Toggle((bool)v);
			}));
			list.Add(MakeToggle("Clock24Hour", "24-Hour Clock Format", ModSettings.Clock24Hour, delegate(object v)
			{
				ModSettings.Clock24Hour = (bool)v;
				ModSettings.Save();
			}));
			Array array = Array.CreateInstance(_entryType!, list.Count);
			for (int num = 0; num < list.Count; num++)
			{
				array.SetValue(list[num], num);
			}

			string text = "BetterSpire2Lite";
			string text2 = "BetterSpire2 Lite";
			_apiType!.GetMethod("Register", new Type[3]
			{
				typeof(string),
				typeof(string),
				array.GetType()
			})?.Invoke(null, new object[3] { text, text2, array });
			ModLog.Info($"ModConfig integration registered ({list.Count} entries)");
		}
		catch (Exception ex)
		{
			ModLog.Error("ModConfigBridge.Register", ex);
		}
	}

	private static object ConfigTypeValue(string name)
	{
		return Enum.Parse(_configType!, name);
	}

	private static object MakeToggle(string key, string label, bool defaultValue, Action<object> onChanged)
	{
		return MakeEntry(key, label, ConfigTypeValue("Toggle"), defaultValue, 0f, 100f, 1f, "F0", null, null, null, onChanged);
	}

	private static object MakeHeader(string label)
	{
		return MakeEntry("", label, ConfigTypeValue("Header"));
	}

	private static object MakeEntry(string key, string label, object type, object? defaultValue = null, float min = 0f, float max = 100f, float step = 1f, string format = "F0", string[]? options = null, string? buttonText = null, Func<object, bool>? validator = null, Action<object>? onChanged = null)
	{
		object obj = Activator.CreateInstance(_entryType!)!;
		SetProp(obj, "Key", key);
		SetProp(obj, "Label", label);
		SetProp(obj, "Type", type);
		if (defaultValue != null)
		{
			SetProp(obj, "DefaultValue", defaultValue);
		}
		SetProp(obj, "Min", min);
		SetProp(obj, "Max", max);
		SetProp(obj, "Step", step);
		SetProp(obj, "Format", format);
		if (options != null)
		{
			SetProp(obj, "Options", options);
		}
		if (buttonText != null)
		{
			SetProp(obj, "ButtonText", buttonText);
		}
		if (validator != null)
		{
			SetProp(obj, "Validator", validator);
		}
		if (onChanged != null)
		{
			SetProp(obj, "OnChanged", onChanged);
		}
		return obj;
	}

	private static void SetProp(object obj, string name, object value)
	{
		obj.GetType().GetProperty(name)?.SetValue(obj, value);
	}
}
