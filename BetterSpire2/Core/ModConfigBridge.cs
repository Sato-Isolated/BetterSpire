#nullable enable
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using System;
using System.Collections.Generic;

namespace BetterSpire2.Core;

internal static class ModConfigBridge
{
    private static bool _detected;

    private static bool _available;

    private static Type? _apiType;

    private static Type? _entryType;

    private static Type? _configType;

    private static bool _registered;
    private static SceneTree? _pendingTree;
    private static Action? _pendingHandler;

    internal static void CancelPending()
    {
        if (_pendingTree != null && GodotObject.IsInstanceValid(_pendingTree) && _pendingHandler != null)
            _pendingTree.ProcessFrame -= _pendingHandler;
        _pendingTree = null; _pendingHandler = null;
    }

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
        if (_registered || _pendingHandler != null)
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
                    CancelPending();
                    Register();
                }
            }

            _pendingTree = tree;
            _pendingHandler = Handler;
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
        try
        {
            List<object> list = new List<object>();
            foreach (ModSettingSectionDefinition section in ModSettingsCatalog.Sections)
            {
                list.Add(MakeHeader(ModText.T(section.Title)));
                foreach (ModSettingDefinition definition in section.Settings)
                {
                    if (definition.Kind == ModSettingKind.Toggle)
                    {
                        list.Add(MakeToggle(definition.Key, ModText.T(definition.Label), definition.ToggleValue, value => definition.Apply((bool)value)));
                        continue;
                    }

                    list.Add(MakeEntry(
                        definition.Key,
                        ModText.T(definition.Label) + " (" + definition.Unit + ")",
                        ConfigTypeValue("Slider"),
                        (float)definition.SliderValue,
                        definition.Min,
                        definition.Max,
                        definition.Step,
                        "F0",
                        onChanged: value => definition.Apply(Convert.ToInt32(value))));
                }
            }
            Array array = Array.CreateInstance(_entryType!, list.Count);
            for (int num = 0; num < list.Count; num++)
            {
                array.SetValue(list[num], num);
            }

            string text = "BetterSpire2Lite";
            string text2 = "BetterSpire Guardian";
            var register = _apiType!.GetMethod("Register", new Type[3]
            {
                typeof(string),
                typeof(string),
                array.GetType()
            }) ?? throw new MissingMethodException("ModConfigApi.Register(string,string,ConfigEntry[])");
            register.Invoke(null, new object[3] { text, text2, array });
            _registered = true;
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
