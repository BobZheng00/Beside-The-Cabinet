using System.Text;
using System.Text.RegularExpressions;

namespace InsertCoin.Mind;

/// <summary>Where a memo is: hanging from the cabinet's slot (just printed), pinned on its side, or in the archive.</summary>
public enum MemoPlace { Hanging, Pinned, Archived }

/// <summary>
/// Long text on paper (docs/specs/2026-10-05-tools-and-abilities.md, "Memos"): what she hands the player when it is
/// too long to say (a hint written out, steps, a list, what she found), or a note the player writes to themselves.
/// Memos are the player's, not her memory: forgetting leaves them be.
/// </summary>
/// <param name="By">The character who printed it, or null for the player's own.</param>
/// <param name="Pin">While pinned, how it is stacked: one pinned later lies over one pinned earlier.</param>
/// <param name="Colour">Its paper (one of <see cref="Colours"/>), so memos can be told apart.</param>
/// <param name="Sticker">A sticker on it (one of <see cref="Stickers"/>), or none.</param>
/// <param name="Spot">While pinned, where on the cabinet's side, in the side panel's own units (depth from its front, height from its top).</param>
/// <param name="Tilt">While pinned, how far it is turned on the side (degrees), as paper taped up by hand is.</param>
public sealed partial record Memo(long Id, string Title, string Text, string? By, DateTimeOffset At, MemoPlace Place, int Pin = 0,
                                  string Colour = Memo.Cream, string? Sticker = null, (double A, double B)? Spot = null, double Tilt = 0)
{
    /// <summary>How many fit on the cabinet's side (they may overlap).</summary>
    public const int PinnedAtMost = 10;

    public const string Cream = "cream", Yellow = "yellow";

    /// <summary>The papers a memo can be on: hers are cream, the player's own yellow, until changed.</summary>
    public static readonly string[] Colours = [Cream, Yellow, "pink", "mint", "lilac", "blue"];

    /// <summary>The stickers a memo can have.</summary>
    public static readonly string[] Stickers = ["star", "heart", "coin", "cat", "flower", "bolt"];

    /// <summary>The paper a new memo is on: cream for hers, yellow for the player's own.</summary>
    public static string ColourFor(string? by) => by is null ? Yellow : Cream;

    /// <summary>How long a title and a memo may be: a title is a few words; a memo, a page or two.</summary>
    public const int TitleLength = 60, TextLength = 6000;

    /// <summary>How much of the player's own memo she is told, in characters (the rest is on the paper).</summary>
    public const int ToldAtMost = 1500;

    /// <summary>
    /// What she is told when the player writes a memo of their own: what it says, and that it is theirs. She may act on
    /// it (a reminder or a date on the agenda, a word of help) or let it be, answering only [quiet], so nothing is said.
    /// </summary>
    public static string Written(string title, string text) =>
        $"The player just wrote a memo to themselves, and it printed out of the cabinet's slot. Title: \"{title.Trim()}\". It says:\n" +
        $"{(text.Trim().Length > ToldAtMost ? text.Trim()[..ToldAtMost] + "…" : text.Trim())}\n\n" +
        "It's their own note: you needn't say or do anything. If something in it is for you, act on it: put a date or a reminder on the agenda " +
        "and say so in a few words, or offer help in a line. If it needs nothing from you, answer with only [quiet].";

    /// <summary>The first lines, as the paper hanging from the slot shows them: plain words, without code fences or list marks.</summary>
    public string Glimpse(int characters = 80)
    {
        var plain = string.Join(" ", Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("```"))
                                         .Select(l => ListMark().Replace(l, "")));
        if (plain.Length <= characters)
            return plain;
        var cut = plain[..(characters - 1)];
        var space = cut.LastIndexOf(' ');
        return (space > characters / 2 ? cut[..space] : cut).TrimEnd(' ', ',', ';', ':') + "…";      // at a word, where words have spaces
    }

    /// <summary>
    /// The memo as Godot rich text: the little Markdown a model writes (headings, lists, **bold**, `code` and fenced
    /// code blocks) made into BBCode, everything else as written, square brackets kept as text.
    /// </summary>
    public static string ToBbcode(string text)
    {
        var bb = new StringBuilder();
        var code = false;
        string? list = null;                                // the list being written ("ul" or "ol"): its items wrap under their own text
        void EndList()
        {
            if (list is null)
                return;
            bb.Append($"[/{list}]\n");
            list = null;
        }
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.TrimStart().StartsWith("```"))
            {
                EndList();
                bb.Append(code ? "[/code]\n" : "[code]");
                code = !code;
                continue;
            }
            if (code)
            {
                bb.Append(Escape(raw)).Append('\n');
                continue;
            }
            var line = raw.TrimEnd();
            if (ListMark().Match(line) is { Success: true } mark)
            {
                var kind = mark.Value.Trim() is "-" or "*" or "•" ? "ul" : "ol";
                if (list != kind)
                {
                    EndList();
                    bb.Append(kind == "ul" ? "[ul]" : "[ol type=1]");
                    list = kind;
                }
                else
                    bb.Append('\n');
                bb.Append(Inline(line[mark.Length..]));
                continue;
            }
            EndList();
            if (Heading().Match(line) is { Success: true } heading)
                bb.Append("[b]").Append(Inline(heading.Groups[1].Value)).Append("[/b]\n");
            else
                bb.Append(Inline(line)).Append('\n');
        }
        EndList();
        if (code)
            bb.Append("[/code]");
        return bb.ToString().TrimEnd('\n');
    }

    private static string Inline(string line)
    {
        var bb = new StringBuilder();
        var parts = line.Split('`');
        for (var i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 1 && i < parts.Length - 1)
                bb.Append("[code]").Append(Escape(parts[i])).Append("[/code]");
            else
                bb.Append(Bold().Replace(Escape(i % 2 == 1 ? "`" + parts[i] : parts[i]), "[b]$1[/b]"));
        }
        return bb.ToString();
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+(.*)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s*([-*•]|\d{1,2}[.)])\s+")]
    private static partial Regex ListMark();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Bold();
}
