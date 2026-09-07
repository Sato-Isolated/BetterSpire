#nullable enable
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace BetterSpire2.HandViewer;

public static partial class TeammateHandViewer
{
    internal static string ResolveDescription(CardModel card)
    {
        try
        {
            // The bundled DLL exposes this overload. No reflection or dynamic dispatch on hover.
            string description = card.GetDescriptionForPile(card.Pile?.Type ?? PileType.Hand, null);
            return CleanDescriptionText(description);
        }
        catch
        {
            return CleanDescriptionText(card.Description?.GetFormattedText() ?? "");
        }
    }

    private static string CleanDescriptionText(string text)
    {
        text = Regex.Replace(text, "(\\[img\\]res://\\S+?/([^/]+?)(?:_icon)?\\.png\\[/img\\])+", delegate (Match match)
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
            return ModText.T("Energy");
        }
        if (filename.Contains("block"))
        {
            return ModText.T("Block");
        }
        if (filename.Contains("star"))
        {
            return ModText.T("Star");
        }
        if (filename.Contains("orb"))
        {
            return ModText.T("Orb");
        }
        if (filename.Length == 0) return "?";
        return char.ToUpper(filename[0]) + filename.Substring(1);
    }

}
