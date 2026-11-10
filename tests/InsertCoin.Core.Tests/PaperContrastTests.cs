using System.Globalization;
using System.Text.RegularExpressions;

namespace InsertCoin.Core.Tests;

/// <summary>Hints and the settings' list headings stay easy to read. Paper.cs is read as text, as tests don't reference the game.</summary>
public class PaperContrastTests
{
    private static readonly string Paper = ReadPaper();

    private static string ReadPaper()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(folder!.FullName, "src", "InsertCoin.Game")))
            folder = folder.Parent;
        return File.ReadAllText(Path.Combine(folder.FullName, "src", "InsertCoin.Game", "Companion", "Paper.cs"));
    }

    private static string Named(string colour) => Regex.Match(Paper, $@"\b{colour} = new\(""([0-9A-Fa-f]{{6}})""\)").Groups[1].Value;

    private static double Luminance(string hex)
    {
        double Channel(int at)
        {
            var c = int.Parse(hex.Substring(at, 2), NumberStyles.HexNumber) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
    }

    private static double Contrast(string a, string b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    [Fact]
    public void Black_on_white_measures_twenty_one_to_one() => Assert.Equal(21, Contrast("000000", "FFFFFF"), 3);

    [Theory]
    [InlineData("Hint", "Sheet")]
    [InlineData("Hint", "Wash")]
    [InlineData("Side", "Sheet")]
    [InlineData("Side", "Wash")]
    public void Hints_and_the_lists_entries_are_at_least_four_and_a_half_to_one_on_their_paper(string label, string paper)
    {
        var used = Regex.Match(Paper, $@"""font_color"", ""{label}"", (\w+)\)").Groups[1].Value;
        Assert.InRange(Contrast(Named(used), Named(paper)), 4.5, 21);
    }
}
