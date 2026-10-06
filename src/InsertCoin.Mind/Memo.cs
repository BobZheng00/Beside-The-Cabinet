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
/// <param name="Pin">Its place on the cabinet's side, while pinned (0 the top).</param>
public sealed partial record Memo(long Id, string Title, string Text, string? By, DateTimeOffset At, MemoPlace Place, int Pin = 0)
{
    /// <summary>How many fit on the cabinet's side.</summary>
    public const int PinnedAtMost = 5;

    /// <summary>How long a title and a memo may be: a title is a few words; a memo, a page or two.</summary>
    public const int TitleLength = 60, TextLength = 6000;

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
