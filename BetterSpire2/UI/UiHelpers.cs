#nullable enable
using Godot;

namespace BetterSpire2.UI;

public static class UiHelpers
{
	public static bool IsValid(GodotObject? instance)
	{
		return instance != null && GodotObject.IsInstanceValid(instance);
	}

	public static CanvasLayer CreateCanvasLayer(int layer)
	{
		return new CanvasLayer
		{
			Layer = layer
		};
	}

	public static StyleBoxFlat CreatePanelStyle(Color backgroundColor, Color borderColor, int borderWidth, int cornerRadius, float contentMargin)
	{
		StyleBoxFlat style = new StyleBoxFlat();
		style.BgColor = backgroundColor;
		style.BorderColor = borderColor;
		style.SetBorderWidthAll(borderWidth);
		style.SetCornerRadiusAll(cornerRadius);
		style.SetContentMarginAll(contentMargin);
		return style;
	}

	public static StyleBoxFlat CreateSeparatorStyle(Color color, float topMargin = 1f, float bottomMargin = 1f)
	{
		StyleBoxFlat style = new StyleBoxFlat();
		style.BgColor = color;
		style.ContentMarginTop = topMargin;
		style.ContentMarginBottom = bottomMargin;
		return style;
	}

	public static Label CreateLabel(string text, Color color, int fontSize, HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left)
	{
		Label label = new Label();
		label.Text = text;
		label.HorizontalAlignment = horizontalAlignment;
		label.MouseFilter = Control.MouseFilterEnum.Ignore;
		label.AddThemeColorOverride("font_color", color);
		label.AddThemeFontSizeOverride("font_size", fontSize);
		return label;
	}
}