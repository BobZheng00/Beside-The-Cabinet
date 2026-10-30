using System.Globalization;

namespace InsertCoin.Mind;

/// <summary>
/// The caption under a flash on the cabinet's screen (ScreenView): which of its two fonts a caption takes, and how wide
/// a line may be at each, so that a line wrapped here never wraps again on the screen, where its end would be cut off.
/// Widths are in Latin capitals: a Chinese character, or an emoji, counts as two.
/// </summary>
public static class CrtCaption
{
    /// <summary>The caption's two font sizes, in the screen's pixels.</summary>
    public const int Large = 30, Small = 24;

    /// <summary>How wide a line fits at <see cref="Large"/> and at <see cref="Small"/> (the spike's "JOB INTERVIEW AT NORTHWIND" just fits at 24 px).</summary>
    public const int LargeLine = 20, SmallLine = 26;

    /// <summary>
    /// The font a caption is drawn in: the large one while every line fits it, and the caption is short; else the small.
    /// </summary>
    public static int Font(string caption) => caption.Length > 34 || caption.Split('\n').Max(Width) > LargeLine ? Small : Large;

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
