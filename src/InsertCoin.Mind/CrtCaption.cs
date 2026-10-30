using System.Globalization;

namespace InsertCoin.Mind;

/// <summary>
/// The caption under a flash on the cabinet's screen (ScreenView): how wide a line is planned, in Latin capitals (a
/// Chinese character, or an emoji, counts as two). The screen measures each line in its real font to pick its size,
/// and gives a caption the room for every line it draws, so a plan that runs a little wide wraps once more, never cut.
/// </summary>
public static class CrtCaption
{
    /// <summary>How wide a planned line is, at most: about what fits at the caption's small size (24 px), wide capitals and all.</summary>
    public const int SmallLine = 23;

    /// <summary>How wide <paramref name="line"/> is, in Latin capitals.</summary>
    public static int Width(string line) => Elements(line).Sum(Wide);

    /// <summary>How wide one character is: two for a Chinese, Japanese or Korean one, or an emoji, else one.</summary>
    public static int Wide(string element) => element.Length > 1 || element[0] >= '⺀' ? 2 : 1;

    /// <summary>The characters of <paramref name="text"/> as the reader sees them: an emoji's two halves stay together.</summary>
    public static List<string> Elements(string text)
    {
        var elements = new List<string>();
        var each = StringInfo.GetTextElementEnumerator(text);
        while (each.MoveNext())
            elements.Add(each.GetTextElement());
        return elements;
    }
}
