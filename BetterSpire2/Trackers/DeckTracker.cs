#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace BetterSpire2.Trackers;

public static partial class DeckTracker
{
	[Flags]
	private enum ResizeEdge
	{
		None = 0,
		Right = 1,
		Bottom = 2
	}

	public static int DebugDuplicatePlayers = 0;

	private static CanvasLayer? _canvasLayer;

	private static HandPanel? _panel;

	private static bool _visible;

	private static Label? _gripLabel;

	private static readonly List<PlayerHandSection> _sections = new List<PlayerHandSection>();

	private static int _currentPage;

	private const int PlayersPerPage = 4;

	private static Vector2? _savedPosition;

	private static Vector2? _savedSize;

	private static bool _resizing;

	private static ResizeEdge _resizeEdge;

	private const float ResizeMargin = 8f;

	private const float DefaultPanelWidth = 420f;

	private const float MinPanelWidth = 230f;

	private const float MinPanelHeight = 180f;

	private static bool _dragging;

	private static Vector2 _dragOffset;

	private static void LoadSavedLayout()
	{
		_savedPosition = ModSettings.HandViewerPosition;
		_savedSize = ModSettings.HandViewerSize;
	}

	private static void PersistLayout()
	{
		ModSettings.HandViewerPosition = _savedPosition;
		ModSettings.HandViewerSize = _savedSize;
		ModSettings.Save();
	}

	public static void Toggle()
	{
		ModLog.Info($"DeckTracker.Toggle: _visible={_visible}");
		if (_visible)
		{
			Hide();
		}
		else
		{
			Show();
		}
	}

	public static bool ShowIfInCombat()
	{
		if (!_visible)
		{
			Show();
		}
		return _visible;
	}

	public static void RefreshIfVisible()
	{
		if (_visible)
		{
			Refresh();
		}
	}

	public static bool HasAnyNullHands()
	{
		if (!_visible)
		{
			return false;
		}
		foreach (PlayerHandSection section in _sections)
		{
			if (section.IsHandNull)
			{
				return true;
			}
		}
		return false;
	}

	public static void OnCombatSetUp()
	{
		CombatManager instance = CombatManager.Instance;
		if (instance != null)
		{
			instance.TurnStarted -= OnTurnStarted;
			instance.CombatEnded -= OnCombatEnded;
			instance.TurnStarted += OnTurnStarted;
			instance.CombatEnded += OnCombatEnded;
			RefreshIfVisible();
			if (ModSettings.AutoShowTeammateHand && ModSettings.ShowTeammateHand)
			{
				ShowIfInCombat();
			}
		}
	}

	private static void OnTurnStarted(CombatState _)
	{
		if (!_visible)
		{
			return;
		}
		foreach (PlayerHandSection section in _sections)
		{
			try
			{
				section.RefreshHandReference();
			}
			catch (Exception ex)
			{
				ModLog.Error("DeckTracker.OnTurnStarted", ex);
			}
		}
	}

	private static void OnCombatEnded(CombatRoom _)
	{
		try
		{
			Hide();
			CombatManager instance = CombatManager.Instance;
			if (instance != null)
			{
				instance.TurnStarted -= OnTurnStarted;
				instance.CombatEnded -= OnCombatEnded;
			}
		}
		catch (Exception ex)
		{
			ModLog.Error("DeckTracker.OnCombatEnded", ex);
		}
	}

	public static void NextPage()
	{
		if (_visible)
		{
			int playerCount = GetPlayerCount();
			int num = Math.Max(0, (playerCount - 1) / 4);
			if (_currentPage < num)
			{
				_currentPage++;
				Refresh();
			}
		}
	}

	public static void PrevPage()
	{
		if (_visible && _currentPage > 0)
		{
			_currentPage--;
			Refresh();
		}
	}

	private static int GetPlayerCount()
	{
		RunState runState = GetRunState();
		if (runState?.Players == null)
		{
			return 0;
		}
		return (DebugDuplicatePlayers > 0) ? DebugDuplicatePlayers : runState.Players.Count;
	}

	private static RunState? GetRunState()
	{
		try
		{
			return RunManager.Instance?.DebugOnlyGetState();
		}
		catch
		{
			return null;
		}
	}

	private static void Show()
	{
		if (!ModSettings.ShowTeammateHand)
		{
			ModLog.Info("DeckTracker.Show: blocked — ShowTeammateHand is off");
			return;
		}
		LoadSavedLayout();
		CombatManager instance = CombatManager.Instance;
		if (instance == null)
		{
			ModLog.Info("DeckTracker.Show: blocked — CombatManager.Instance is null");
			return;
		}
		CombatState combatState = instance.DebugOnlyGetState();
		if (combatState == null)
		{
			ModLog.Info("DeckTracker.Show: blocked — combatState is null");
			return;
		}
		RunState runState = GetRunState();
		if (runState?.Players == null || runState.Players.Count == 0)
		{
			ModLog.Info("DeckTracker.Show: blocked — runState.Players is " + ((runState?.Players == null) ? "null" : "empty"));
			return;
		}
		ModLog.Info($"DeckTracker.Show: building UI for {runState.Players.Count} players");
		_currentPage = 0;
		BuildUI(runState);
	}

	private static void Refresh()
	{
		if (UiHelpers.IsValid(_panel))
		{
			_savedPosition = _panel!.Position;
			_savedSize = _panel.Size;
		}
		RunState runState = GetRunState();
		if (runState?.Players != null)
		{
			CleanupUI();
			BuildUI(runState);
		}
	}

	private static void BuildUI(RunState runState)
	{
		if (_visible)
		{
			CleanupUI();
		}
		NGame instance = NGame.Instance;
		if (instance == null)
		{
			return;
		}
		_canvasLayer = UiHelpers.CreateCanvasLayer(10);
		instance.AddChild(_canvasLayer, forceReadableName: false, Node.InternalMode.Disabled);
		ulong localNetId = 0uL;
		try
		{
			localNetId = (RunManager.Instance?.NetService?.NetId).GetValueOrDefault();
		}
		catch
		{
		}
		List<Player> list = new List<Player>();
		if (DebugDuplicatePlayers > 0)
		{
			Player item = runState.Players[0];
			for (int i = 0; i < DebugDuplicatePlayers; i++)
			{
				list.Add(item);
			}
		}
		else
		{
			list.AddRange(runState.Players);
		}
		if (ModSettings.HideOwnHand && localNetId != 0)
		{
			list.RemoveAll((Player p) => p.NetId == localNetId);
		}
		int count = list.Count;
		int num = Math.Max(0, (count - 1) / 4);
		_currentPage = Math.Min(_currentPage, num);
		int num2 = _currentPage * 4;
		int num3 = Math.Min(num2 + 4, count);
		_panel = new HandPanel();
		_panel.ZIndex = 200;
		_panel.ClipContents = true;
		Vector2 size = _savedSize ?? new Vector2(420f, 0f);
		if (size.X < 230f)
		{
			size.X = 230f;
		}
		_panel.CustomMinimumSize = new Vector2(230f, 180f);
		if (_savedSize.HasValue)
		{
			_panel.Size = size;
		}
		StyleBoxFlat styleBoxFlat = new StyleBoxFlat();
		styleBoxFlat.BgColor = new Color(0f, 0f, 0f, 0f);
		styleBoxFlat.BorderColor = new Color(0f, 0f, 0f, 0f);
		styleBoxFlat.SetBorderWidthAll(0);
		styleBoxFlat.SetCornerRadiusAll(0);
		styleBoxFlat.SetContentMarginAll(14f);
		_panel.AddThemeStyleboxOverride("panel", styleBoxFlat);
		ScrollContainer scrollContainer = new ScrollContainer();
		scrollContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		scrollContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scrollContainer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_panel.AddChild(scrollContainer, forceReadableName: false, Node.InternalMode.Disabled);
		VBoxContainer vBoxContainer = new VBoxContainer();
		vBoxContainer.AddThemeConstantOverride("separation", 6);
		vBoxContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scrollContainer.AddChild(vBoxContainer, forceReadableName: false, Node.InternalMode.Disabled);
		HBoxContainer hBoxContainer = new HBoxContainer();
		hBoxContainer.AddThemeConstantOverride("separation", 8);
		hBoxContainer.Alignment = BoxContainer.AlignmentMode.Center;
		vBoxContainer.AddChild(hBoxContainer, forceReadableName: false, Node.InternalMode.Disabled);
		if (num > 0)
		{
			Button button = CreateArrowButton("<", _currentPage > 0);
			button.Pressed += delegate
			{
				PrevPage();
			};
			hBoxContainer.AddChild(button, forceReadableName: false, Node.InternalMode.Disabled);
		}
		Label label = new Label();
		string text = "Party Hands";
		if (num > 0)
		{
			text += $"  [{_currentPage + 1}/{num + 1}]";
		}
		label.Text = text;
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		label.MouseFilter = Control.MouseFilterEnum.Ignore;
		label.AddThemeColorOverride("font_color", new Color(0.9f, 0.7f, 0.2f));
		label.AddThemeFontSizeOverride("font_size", 18);
		hBoxContainer.AddChild(label, forceReadableName: false, Node.InternalMode.Disabled);
		if (num > 0)
		{
			Button button2 = CreateArrowButton(">", _currentPage < num);
			button2.Pressed += delegate
			{
				NextPage();
			};
			hBoxContainer.AddChild(button2, forceReadableName: false, Node.InternalMode.Disabled);
		}
		Button button3 = new Button();
		button3.Text = "X";
		button3.CustomMinimumSize = new Vector2(28f, 28f);
		button3.Pressed += delegate
		{
			Hide();
		};
		StyleBoxFlat styleBoxFlat2 = new StyleBoxFlat();
		styleBoxFlat2.BgColor = new Color(0.3f, 0.1f, 0.1f);
		styleBoxFlat2.BorderColor = new Color(0.6f, 0.2f, 0.2f);
		styleBoxFlat2.SetBorderWidthAll(1);
		styleBoxFlat2.SetCornerRadiusAll(4);
		styleBoxFlat2.SetContentMarginAll(2f);
		button3.AddThemeStyleboxOverride("normal", styleBoxFlat2);
		StyleBoxFlat styleBoxFlat3 = new StyleBoxFlat();
		styleBoxFlat3.BgColor = new Color(0.5f, 0.15f, 0.15f);
		styleBoxFlat3.BorderColor = new Color(0.8f, 0.3f, 0.3f);
		styleBoxFlat3.SetBorderWidthAll(1);
		styleBoxFlat3.SetCornerRadiusAll(4);
		styleBoxFlat3.SetContentMarginAll(2f);
		button3.AddThemeStyleboxOverride("hover", styleBoxFlat3);
		button3.AddThemeStyleboxOverride("pressed", styleBoxFlat3);
		button3.AddThemeColorOverride("font_color", new Color(0.9f, 0.4f, 0.4f));
		button3.AddThemeFontSizeOverride("font_size", 14);
		hBoxContainer.AddChild(button3, forceReadableName: false, Node.InternalMode.Disabled);
		if (ModSettings.CompactHandViewer)
		{
			GridContainer gridContainer = new GridContainer();
			gridContainer.Columns = 2;
			gridContainer.AddThemeConstantOverride("h_separation", 16);
			gridContainer.AddThemeConstantOverride("v_separation", 8);
			vBoxContainer.AddChild(gridContainer, forceReadableName: false, Node.InternalMode.Disabled);
			for (int num4 = num2; num4 < num3; num4++)
			{
				Player player = list[num4];
				string debugSuffix = ((DebugDuplicatePlayers > 0) ? $" #{num4 + 1}" : null);
				PlayerHandSection playerHandSection = new PlayerHandSection(player, localNetId, debugSuffix);
				VBoxContainer vBoxContainer2 = new VBoxContainer();
				vBoxContainer2.AddThemeConstantOverride("separation", 2);
				vBoxContainer2.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				playerHandSection.AddTo(vBoxContainer2);
				gridContainer.AddChild(vBoxContainer2, forceReadableName: false, Node.InternalMode.Disabled);
				_sections.Add(playerHandSection);
			}
		}
		else
		{
			for (int num5 = num2; num5 < num3; num5++)
			{
				Player player2 = list[num5];
				string debugSuffix2 = ((DebugDuplicatePlayers > 0) ? $" #{num5 + 1}" : null);
				PlayerHandSection playerHandSection2 = new PlayerHandSection(player2, localNetId, debugSuffix2);
				playerHandSection2.AddTo(vBoxContainer);
				_sections.Add(playerHandSection2);
			}
		}
		Label label2 = new Label();
		label2.Text = (ModSettings.AlwaysShowTeammateHand ? "Drag to move | Drag edges to resize | F3 or X to close" : "Drag to move | Drag edges to resize | Click outside to close");
		label2.HorizontalAlignment = HorizontalAlignment.Center;
		label2.MouseFilter = Control.MouseFilterEnum.Ignore;
		label2.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
		label2.AddThemeFontSizeOverride("font_size", 11);
		vBoxContainer.AddChild(label2, forceReadableName: false, Node.InternalMode.Disabled);
		_canvasLayer.AddChild(_panel, forceReadableName: false, Node.InternalMode.Disabled);
		_gripLabel = new Label();
		_gripLabel.Text = "◢";
		_gripLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.3f));
		_gripLabel.AddThemeFontSizeOverride("font_size", 14);
		_gripLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
		_canvasLayer.AddChild(_gripLabel, forceReadableName: false, Node.InternalMode.Disabled);
		if (_savedPosition.HasValue)
		{
			_panel.Position = _savedPosition.Value;
		}
		else
		{
			_panel.CallDeferred("set_position", GetCenteredPosition());
		}
		_visible = true;
	}

	private static void UpdateGripPosition()
	{
		if (_gripLabel != null && GodotObject.IsInstanceValid(_gripLabel) && _panel != null && GodotObject.IsInstanceValid(_panel))
		{
			Vector2 position = _panel.Position;
			Vector2 size = _panel.Size;
			_gripLabel.Position = new Vector2(position.X + size.X - 18f, position.Y + size.Y - 20f);
		}
	}

	private static Vector2 GetCenteredPosition()
	{
		float num = (NGame.Instance?.GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920f, 1080f)).X / 2f - 500f;
		if (num < 0f)
		{
			num = 0f;
		}
		return new Vector2(num, 20f);
	}

	private static Button CreateArrowButton(string text, bool enabled)
	{
		Button button = new Button();
		button.Text = text;
		button.CustomMinimumSize = new Vector2(32f, 28f);
		button.Disabled = !enabled;
		StyleBoxFlat styleBoxFlat = new StyleBoxFlat();
		styleBoxFlat.BgColor = (enabled ? new Color(0.2f, 0.18f, 0.12f) : new Color(0.15f, 0.15f, 0.15f));
		styleBoxFlat.BorderColor = (enabled ? new Color(0.8f, 0.6f, 0.2f) : new Color(0.4f, 0.4f, 0.4f));
		styleBoxFlat.SetBorderWidthAll(1);
		styleBoxFlat.SetCornerRadiusAll(4);
		styleBoxFlat.SetContentMarginAll(4f);
		button.AddThemeStyleboxOverride("normal", styleBoxFlat);
		StyleBoxFlat styleBoxFlat2 = new StyleBoxFlat();
		styleBoxFlat2.BgColor = new Color(0.3f, 0.25f, 0.15f);
		styleBoxFlat2.BorderColor = new Color(0.9f, 0.7f, 0.2f);
		styleBoxFlat2.SetBorderWidthAll(1);
		styleBoxFlat2.SetCornerRadiusAll(4);
		styleBoxFlat2.SetContentMarginAll(4f);
		button.AddThemeStyleboxOverride("hover", styleBoxFlat2);
		StyleBoxFlat styleBoxFlat3 = new StyleBoxFlat();
		styleBoxFlat3.BgColor = new Color(0.35f, 0.3f, 0.15f);
		styleBoxFlat3.BorderColor = new Color(1f, 0.8f, 0.3f);
		styleBoxFlat3.SetBorderWidthAll(1);
		styleBoxFlat3.SetCornerRadiusAll(4);
		styleBoxFlat3.SetContentMarginAll(4f);
		button.AddThemeStyleboxOverride("pressed", styleBoxFlat3);
		button.AddThemeStyleboxOverride("disabled", styleBoxFlat);
		button.AddThemeColorOverride("font_color", enabled ? new Color(0.9f, 0.7f, 0.2f) : new Color(0.4f, 0.4f, 0.4f));
		button.AddThemeColorOverride("font_disabled_color", new Color(0.4f, 0.4f, 0.4f));
		button.AddThemeFontSizeOverride("font_size", 16);
		return button;
	}

	public static bool IsPointInPanel(Vector2 point)
	{
		if (!_visible || _panel == null || !GodotObject.IsInstanceValid(_panel))
		{
			return false;
		}
		return _panel.GetGlobalRect().HasPoint(point);
	}

	public static void Hide()
	{
		if (UiHelpers.IsValid(_panel))
		{
			_savedPosition = _panel!.Position;
			_savedSize = _panel.Size;
			PersistLayout();
		}
		CleanupUI();
		_visible = false;
		_resizing = false;
	}

	private static void CleanupUI()
	{
		foreach (PlayerHandSection section in _sections)
		{
			section.Cleanup();
		}
		_sections.Clear();
		if (UiHelpers.IsValid(_canvasLayer))
		{
			_canvasLayer!.QueueFree();
		}
		_canvasLayer = null;
		_panel = null;
		_gripLabel = null;
	}

	internal static string ResolveDescription(CardModel card)
	{
		try
		{
			try
			{
				CardPile pile = card.Pile;
				if (pile != null)
				{
					object pileType = pile.GetType().GetProperty("PileType")?.GetValue(pile);
					object descriptionForPile = card.GetType().GetMethod("GetDescriptionForPile")?.Invoke(card, new object[2] { pileType, null });
					if (descriptionForPile is string text && !string.IsNullOrEmpty(text))
					{
						return CleanDescriptionText(text);
					}
				}
			}
			catch
			{
			}
			object description = card.Description;
			if (description == null)
			{
				return "";
			}
			string value3 = description.GetType().GetMethod("GetFormattedText")?.Invoke(description, null) as string;
			if (string.IsNullOrEmpty(value3))
			{
				return "";
			}
			string text2 = ResolveVarPlaceholders(card, value3);
			return CleanDescriptionText(text2);
		}
		catch
		{
			return "";
		}
	}

	private static string CleanDescriptionText(string text)
	{
		text = Regex.Replace(text, "(\\[img\\]res://\\S+?/([^/]+?)(?:_icon)?\\.png\\[/img\\])+", delegate(Match match)
		{
			int count = Regex.Matches(match.Value, "\\[img\\]").Count;
			Match match2 = Regex.Match(match.Value, "res://\\S+?/([^/]+?)(?:_icon)?\\.png");
			string filename = (match2.Success ? match2.Groups[1].Value : "?");
			filename = MapIconLabel(filename);
			return (count > 1) ? $"{count} {filename}" : filename;
		}, RegexOptions.IgnoreCase);
		text = Regex.Replace(text, "\\[/?[^\\]]+\\]", "");
		text = Regex.Replace(text, "res://\\S+", "");
		return text.Trim();
	}

	private static string MapIconLabel(string filename)
	{
		filename = filename.ToLowerInvariant();
		if (filename.Contains("energy"))
		{
			return "Energy";
		}
		if (filename.Contains("block"))
		{
			return "Block";
		}
		if (filename.Contains("star"))
		{
			return "Star";
		}
		if (filename.Contains("orb"))
		{
			return "Orb";
		}
		return char.ToUpper(filename[0]) + filename.Substring(1);
	}

	private static string ResolveVarPlaceholders(CardModel card, string text)
	{
		object dynamicVars = card.DynamicVars;
		if (dynamicVars == null)
		{
			return text;
		}
		return Regex.Replace(text, "\\{(\\w+)(?::[^}]*)?\\}", delegate(Match match)
		{
			string value = match.Groups[1].Value;
			if (TryGetDynamicVarBaseValueReflective(dynamicVars, value, out int value2))
			{
				return value2.ToString();
			}
			return match.Value;
		});
	}

	private static bool TryGetDynamicVarBaseValueReflective(object dynamicVars, string key, out int value)
	{
		value = 0;
		try
		{
			object[] args = new object[2] { key, null };
			object tryGetResult = dynamicVars.GetType().GetMethod("TryGetValue")?.Invoke(dynamicVars, args);
			if (!(tryGetResult is bool flag) || !flag || args[1] == null)
			{
				return false;
			}
			object dynamicVar = args[1];
			object intValue = dynamicVar.GetType().GetProperty("IntValue")?.GetValue(dynamicVar);
			if (intValue is int intValueInt)
			{
				value = intValueInt;
				return true;
			}
			object baseValue = dynamicVar.GetType().GetProperty("BaseValue")?.GetValue(dynamicVar);
			if (baseValue is float floatValue)
			{
				value = (int)floatValue;
				return true;
			}
			if (baseValue is decimal decimalValue)
			{
				value = (int)decimalValue;
				return true;
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	public static void HandleMouseInput(InputEvent @event)
	{
		if (!_visible || !UiHelpers.IsValid(_panel))
		{
			return;
		}
		UpdateGripPosition();
		if (@event is InputEventMouseButton inputEventMouseButton && inputEventMouseButton.ButtonIndex == MouseButton.Left)
		{
			Vector2 position = inputEventMouseButton.Position;
			Rect2 globalRect = _panel!.GetGlobalRect();
			if (inputEventMouseButton.Pressed)
			{
				if (globalRect.HasPoint(position))
				{
					ResizeEdge resizeEdge = GetResizeEdge(position, globalRect);
					if (resizeEdge != ResizeEdge.None)
					{
						_resizing = true;
						_resizeEdge = resizeEdge;
					}
					else
					{
						_dragging = true;
						_dragOffset = position - _panel.Position;
					}
				}
				else if (!SettingsMenu.IsPointInPanel(position) && !ModSettings.AlwaysShowTeammateHand)
				{
					_dragging = false;
					_resizing = false;
					Hide();
				}
			}
			else
			{
				if (_dragging || _resizing)
				{
					_savedPosition = _panel.Position;
					_savedSize = _panel.Size;
					PersistLayout();
				}
				_dragging = false;
				_resizing = false;
			}
		}
		else
		{
			if (!(@event is InputEventMouseMotion inputEventMouseMotion))
			{
				return;
			}
			if (_resizing)
			{
				Vector2 position2 = _panel.Position;
				Vector2 size = _panel.Size;
				if (_resizeEdge.HasFlag(ResizeEdge.Right))
				{
					size.X = Math.Max(230f, inputEventMouseMotion.Position.X - position2.X);
				}
				if (_resizeEdge.HasFlag(ResizeEdge.Bottom))
				{
					size.Y = Math.Max(180f, inputEventMouseMotion.Position.Y - position2.Y);
				}
				_panel.CustomMinimumSize = size;
				_panel.Size = size;
			}
			else if (_dragging)
			{
				_panel.Position = inputEventMouseMotion.Position - _dragOffset;
			}
			else
			{
				UpdateResizeCursor(inputEventMouseMotion.Position, _panel.GetGlobalRect());
			}
		}
	}

	private static ResizeEdge GetResizeEdge(Vector2 mousePos, Rect2 rect)
	{
		ResizeEdge resizeEdge = ResizeEdge.None;
		if (mousePos.X >= rect.End.X - 8f)
		{
			resizeEdge |= ResizeEdge.Right;
		}
		if (mousePos.Y >= rect.End.Y - 8f)
		{
			resizeEdge |= ResizeEdge.Bottom;
		}
		return resizeEdge;
	}

	private static void UpdateResizeCursor(Vector2 mousePos, Rect2 rect)
	{
		if (!rect.HasPoint(mousePos))
		{
			_panel.MouseDefaultCursorShape = Control.CursorShape.Arrow;
			return;
		}
		ResizeEdge resizeEdge = GetResizeEdge(mousePos, rect);
		HandPanel panel = _panel;
		if (1 == 0)
		{
		}
		Control.CursorShape mouseDefaultCursorShape = resizeEdge switch
		{
			ResizeEdge.Right | ResizeEdge.Bottom => Control.CursorShape.Fdiagsize, 
			ResizeEdge.Right => Control.CursorShape.Hsize, 
			ResizeEdge.Bottom => Control.CursorShape.Vsize, 
			_ => Control.CursorShape.Arrow, 
		};
		if (1 == 0)
		{
		}
		panel.MouseDefaultCursorShape = mouseDefaultCursorShape;
	}

	private static IHoverTip CreateHoverTip(string title, string description)
	{
		object obj = default(HoverTip);
		typeof(HoverTip).GetProperty("Title")?.SetValue(obj, title);
		typeof(HoverTip).GetProperty("Description")?.SetValue(obj, description);
		typeof(HoverTip).GetProperty("Id")?.SetValue(obj, "BetterSpire2_" + title);
		return (IHoverTip)obj;
	}
}
