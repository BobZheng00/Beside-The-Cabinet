using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace InsertCoin.Mind;

/// <summary>
/// A skill: something she knows how to do the player's way, a folder with a SKILL.md in the open Agent Skills format
/// (https://agentskills.io/specification): a name and a one-line description in its front matter, instructions below
/// (docs/specs/2026-10-15-skills.md).
/// </summary>
/// <param name="Folder">The skill's own folder, where it is (a linked one is never copied).</param>
/// <param name="Broken">Why its SKILL.md can't be used, in a few words for the Skills page; null when it can.</param>
/// <param name="Hint">What in it isn't as the format asks, though it is used anyway (a name that isn't its folder's, say), for the page.</param>
public sealed record Skill(string Name, string Description, string Folder, string? Broken = null, string? Hint = null);

/// <summary>
/// The skills she has: each folder with a SKILL.md inside a skills folder, the app's own (in the player's profile) or
/// one elsewhere that the player linked, as Claude Code finds them in ~/.claude/skills. Only what each is called and
/// what it is for is always with her; she reads a skill's instructions when she uses it, from the file each time, so
/// an edit made anywhere applies the next time. Only its SKILL.md and the text files it mentions are ever read, and
/// only inside its own folder: its scripts are never run.
/// </summary>
public sealed class Skills(string own)
{
    public const string FileName = "SKILL.md";

    /// <summary>
    /// Characters one read gives her, about 5,000 tokens of English: the format recommends instructions under 5,000
    /// tokens and 500 lines (to tune). What is longer is cut, and she is told.
    /// </summary>
    public const int ReadLimit = 20_000;

    public const int NameLimit = 64, DescriptionLimit = 1024;

    /// <summary>The most of a file read from disk at once: enough for <see cref="ReadLimit"/> characters of any language.</summary>
    private const int MostBytes = ReadLimit * 4;

    /// <summary>The most a skill imported may hold, in bytes and files (to tune): skills are text, so far less.</summary>
    public const long ImportBytes = 50L * 1024 * 1024;
    public const int ImportFiles = 5_000;

    /// <summary>The app's own skills folder, in the player's profile: new and imported skills go here.</summary>
    public string Own { get; } = Path.GetFullPath(own);

    /// <summary>Whether <paramref name="skill"/> is in the skills folder <paramref name="folder"/>.</summary>
    public static bool IsIn(Skill skill, string folder) =>
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(skill.Folder)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), StringComparison.OrdinalIgnoreCase);

    /// <summary>Skills folders elsewhere that the player linked, read where they are.</summary>
    public IReadOnlyList<string> Linked { get; set; } = [];

    /// <summary>The skills the player switched off, each by <see cref="KeyOf"/> (so a rename keeps it off).</summary>
    public IReadOnlySet<string> Off { get; set; } = new HashSet<string>();

    /// <summary>
    /// What a skill is switched off by: one in the app's own folder by its folder's name, so moving the profile keeps it
    /// off; one in a linked folder by its full path.
    /// </summary>
    public string KeyOf(Skill skill) => IsIn(skill, Own) ? Path.GetFileName(skill.Folder) : skill.Folder;

    /// <summary>Whether the player switched <paramref name="skill"/> off (by its full path too, as the first build kept it).</summary>
    public bool IsOff(Skill skill) => Off.Contains(KeyOf(skill)) || Off.Contains(skill.Folder);

    /// <summary>
    /// Every skill in one skills folder, by name; a subfolder without a SKILL.md isn't a skill, nor one whose name starts
    /// with a dot (an import under way). A folder that can't be listed has none.
    /// </summary>
    public static IReadOnlyList<Skill> In(string folder)
    {
        var found = new List<Skill>();
        try
        {
            if (!Directory.Exists(folder))
                return [];
            foreach (var dir in Directory.EnumerateDirectories(folder))
            {
                var file = Path.Combine(dir, FileName);
                var named = Path.GetFileName(dir);
                if (named.StartsWith('.') || !File.Exists(file))
                    continue;
                string text;
                try
                {
                    text = Head(file).Text;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    found.Add(new Skill(named, "", dir, $"its {FileName} can't be opened ({e.Message})"));
                    continue;
                }
                var read = Parse(text);
                var name = read.Name ?? named;
                // a name the format doesn't allow has its own hint already: that it isn't the folder's name too goes without saying
                var hint = read.Name is not null && read.Name != named && Allowed(read.Name) ?$"its name, \"{read.Name}\", isn't its folder's name, \"{named}\", as the format asks" : null;
                found.Add(new Skill(name, read.Description ?? "", dir, read.Problem, Joined([hint, read.Hint])));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // listed as far as it could be: a folder gone offline or not ours to read mustn't stop her replies
        }
        return [.. found.OrderBy(s => s.Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The start of a file as text: at most <see cref="MostBytes"/> of it, so a huge file costs no more than a long one,
    /// and whether there was more.
    /// </summary>
    private static (string Text, bool Longer, bool Binary) Head(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[MostBytes + 1];
        var got = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        var kept = Math.Min(got, MostBytes);
        return (Encoding.UTF8.GetString(buffer, 0, kept).TrimStart('\uFEFF'), got > MostBytes, Array.IndexOf(buffer, (byte)0, 0, kept) >= 0);
    }

    /// <summary>
    /// Every skill, the app's own folder first, then each linked one in turn. Two with one name: the later is listed as
    /// broken, so the name she asks for means one skill.
    /// </summary>
    public IReadOnlyList<Skill> All()
    {
        var all = new List<Skill>();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var folder in Linked.Prepend(Own))
            foreach (var skill in In(folder))
            {
                if (skill.Broken is null && !names.TryAdd(skill.Name, skill.Folder))
                    all.Add(skill with { Broken = $"another skill is called {skill.Name} too ({names[skill.Name]})" });
                else
                    all.Add(skill);
            }
        return all;
    }

    /// <summary>The skills she has now: those that can be read and aren't switched off, by name.</summary>
    public IReadOnlyList<Skill> On() =>
        [.. All().Where(s => s.Broken is null && !IsOff(s)).OrderBy(s => s.Name, StringComparer.Ordinal)];

    /// <summary>
    /// What she reads when she uses a skill: its instructions (no <paramref name="file"/>), or a file they mention, by its
    /// path from the skill's folder; read from disk now, and cut at <see cref="ReadLimit"/>. What can't be read says why.
    /// </summary>
    public string Read(string name, string? file = null)
    {
        if (On().FirstOrDefault(s => s.Name == name.Trim()) is not { } skill)
            return $"There is no skill called {name} switched on.";
        var instructions = Inside(skill.Folder, FileName, out var why);
        if (instructions is null)
            return why!;
        string text;
        bool longer;
        try
        {
            (text, longer, _) = Head(instructions);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Its {FileName} can't be read: {e.Message}";
        }
        var read = Parse(text);
        if (read.Problem is not null)
            return $"Its {FileName} can't be used now: {read.Problem}.";
        if (string.IsNullOrWhiteSpace(file) || Same(file, FileName))
            return Cut(read.Body.Trim(), FileName, longer);

        var wanted = file.Trim().Replace('\\', '/');
        if (wanted.StartsWith("./", StringComparison.Ordinal))
            wanted = wanted[2..];
        // mentioned as a path of its own ("references/forms.md", "./forms.md"), not as part of another word or path
        if (!Regex.IsMatch(text.Replace('\\', '/'), $@"(?<![\w./-])(\./)?{Regex.Escape(wanted)}(?![\w/-])"))
            return $"Its {FileName} doesn't mention {file}: only the files it mentions can be read.";
        if (Inside(skill.Folder, wanted, out why) is not { } path)
            return why!;
        try
        {
            var (words, more, binary) = Head(path);
            return binary ? $"{file} isn't text, so it can't be read." : Cut(words, wanted, more);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"{file} can't be read: {e.Message}";
        }
    }

    /// <summary>
    /// Whether a step on the way to a file is a link (a symbolic link or a junction), which could lead out of the skill's
    /// folder. Only a link target counts: OneDrive and Dropbox mark their synced folders and online-only files as reparse
    /// points with none, and those are the player's own files where they are.
    /// </summary>
    public static bool IsLink(FileSystemInfo info) => info.LinkTarget is not null;

    /// <summary>
    /// The full path of <paramref name="relative"/> inside <paramref name="folder"/>, if it is a file there; null, saying
    /// why, for one outside it (by "..", an absolute path or a link on the way: links aren't followed) or missing.
    /// </summary>
    internal static string? Inside(string folder, string relative, out string? why)
    {
        why = null;
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Path.IsPathRooted(relative))
        {
            why = $"{relative} is outside the skill's folder: only its own files can be read.";
            return null;
        }
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            why = $"{relative} is outside the skill's folder: only its own files can be read.";
            return null;
        }
        // every step below the skill's folder, the file too: none may be a link (or a junction), which could lead out
        var step = root;
        foreach (var part in full[(root.Length + 1)..].Split(Path.DirectorySeparatorChar))
        {
            step = Path.Combine(step, part);
            FileSystemInfo info = Directory.Exists(step) ? new DirectoryInfo(step) : new FileInfo(step);
            if (!info.Exists)
            {
                why = $"There is no {relative} in the skill's folder.";
                return null;
            }
            if (IsLink(info))
            {
                why = $"{relative} goes through a link, and links aren't followed: only the skill's own files can be read.";
                return null;
            }
        }
        if (!File.Exists(full))
        {
            why = $"{relative} is a folder, not a file.";
            return null;
        }
        return full;
    }

    private static bool Same(string file, string name) => file.Trim().TrimStart('.', '/', '\\').Equals(name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Text cut at <see cref="ReadLimit"/>, at a line's end where one is near, saying so; <paramref name="longer"/>: the file went on past what was read.</summary>
    internal static string Cut(string text, string file, bool longer = false)
    {
        if (text.Length <= ReadLimit && !longer)
            return text;
        var limit = Math.Min(text.Length, ReadLimit);
        var end = text.LastIndexOf('\n', limit - 1);
        var kept = text[..(end > limit / 2 ? end : limit)].TrimEnd();
        return $"{kept}\n\n[{file} was cut here: it is longer than {ReadLimit:N0} characters, the most read at once. The rest isn't shown.]";
    }

    /// <summary>What a SKILL.md's front matter says, and its instructions.</summary>
    /// <param name="Name">Its name, or null when it gives none (the folder's name stands in).</param>
    /// <param name="Problem">Why it can't be used: no front matter that can be read, or no description; null when it can.</param>
    /// <param name="Hint">What isn't as the format asks, though it is used anyway (a name it doesn't allow, a description too long).</param>
    public sealed record Front(string? Name, string? Description, string Body, string? Problem, string? Hint = null);

    /// <summary>
    /// Reads a SKILL.md: the front matter between "---" lines at its top (the few fields of YAML it uses: plain, quoted
    /// and block values; the rest, such as metadata, passed over), then its instructions. Leniently, as Claude Code and
    /// the format's guide for apps read them: only a missing description, or front matter that can't be read, stops a
    /// skill; a name the format doesn't allow, or a description past its length (cut to it), is a hint.
    /// </summary>
    public static Front Parse(string text)
    {
        var lines = text.TrimStart('\uFEFF').Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].TrimEnd() != "---")
            return new(null, null, text, "it has no front matter (its name and description between --- lines at its top)");
        var close = Array.FindIndex(lines, 1, l => l.TrimEnd() is "---" or "...");
        if (close < 0)
            return new(null, null, text, "its front matter has no closing --- line");
        var fields = Fields(lines[1..close]);
        var body = string.Join("\n", lines[(close + 1)..]);
        var name = OneLine(fields.GetValueOrDefault("name"));
        var description = OneLine(fields.GetValueOrDefault("description"));
        var hints = new List<string>();
        if (description.Length > DescriptionLimit)
        {
            description = Prompt.Fit(description, DescriptionLimit);
            hints.Add($"its description is longer than the format's {DescriptionLimit:N0} characters, so only its start is used");
        }
        // what goes into her instructions is held to the format's lengths, the name as much as the description
        if (name.Length > NameLimit)
        {
            name = name[..(char.IsHighSurrogate(name[NameLimit - 1]) ? NameLimit - 1 : NameLimit)].TrimEnd();
            hints.Add($"its name is longer than the format's {NameLimit} characters, so only its start is used");
        }
        if (name.Length > 0 && !Allowed(name))
            hints.Add($"its name, \"{name}\", isn't one the format allows (lowercase letters, numbers and single hyphens)");
        return new(name.Length > 0 ? name : null, description.Length > 0 ? description : null, body,
                   description.Length == 0 ? "it has no description in its front matter" : null, Joined(hints));
    }

    /// <summary>Hints as one line for the page, or null for none.</summary>
    private static string? Joined(IEnumerable<string?> hints) =>
        hints.OfType<string>().ToList() is { Count: > 0 } all ? string.Join("; ", all) : null;

    /// <summary>A field's words on one line, however they were spread over lines.</summary>
    private static string OneLine(string? value) =>
        value is null ? "" : string.Join(' ', value.Split((char[])['\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The front matter's top-level fields that hold text; a nested one (a map or a list) is passed over.</summary>
    private static Dictionary<string, string> Fields(string[] lines)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line.TrimStart().StartsWith('#'))
                continue;
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            // the lines set in under it belong to it
            var more = new List<string>();
            while (i + 1 < lines.Length && (lines[i + 1].Length == 0 || char.IsWhiteSpace(lines[i + 1][0])))
                more.Add(lines[++i]);
            while (more.Count > 0 && more[^1].Trim().Length == 0)
                more.RemoveAt(more.Count - 1);
            if (value.Length > 0 && value[0] is '|' or '>')
            {
                var indent = more.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
                var block = more.Select(l => l.Length >= indent ? l[indent..] : l.Trim());
                fields[key] = value[0] == '|' ? string.Join("\n", block) : string.Join(" ", block.Select(l => l.Trim()).Where(l => l.Length > 0));
            }
            else if (value.Length > 0 && value[0] is '"' or '\'')
                fields[key] = Quoted(string.Join(" ", more.Select(l => l.Trim()).Prepend(value)));
            else if (value.Length == 0 && more.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() is { } first && (first.StartsWith('-') || first.Contains(':')))
                continue;                                   // a map or a list (metadata:): no text of its own
            else
                fields[key] = string.Join(" ", more.Select(l => l.Trim()).Prepend(Uncommented(value)).Where(l => l.Length > 0));
        }
        return fields;
    }

    private static string Uncommented(string value)
    {
        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        return comment >= 0 ? value[..comment].TrimEnd() : value;
    }

    private static string Quoted(string value)
    {
        var quote = value[0];
        var end = value.LastIndexOf(quote);
        var inner = end > 0 ? value[1..end] : value[1..];
        return quote == '\''
            ? inner.Replace("''", "'")
            : inner.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");
    }

    /// <summary>
    /// "New skill": a folder in the app's own skills folder with a SKILL.md to fill in. Its description is left for the
    /// player, so it is listed as needing one until written. Returns its folder.
    /// </summary>
    public string Make()
    {
        Directory.CreateDirectory(Own);
        var name = "new-skill";
        for (var n = 2; Directory.Exists(Path.Combine(Own, name)); n++)
            name = $"new-skill-{n}";
        var folder = Directory.CreateDirectory(Path.Combine(Own, name)).FullName;
        File.WriteAllText(Path.Combine(folder, FileName), $"""
            ---
            name: {name}
            description:
            ---

            # {name}

            Write here, in your own words, how this is done: the steps, what to keep in mind, examples.

            Fill in the description above too, in a sentence: what this skill does and when to use it. The name is the
            folder's name: change both together (lowercase letters, numbers and hyphens).

            """);
        return folder;
    }

    /// <summary>
    /// "Import a skill": copies a skill (its folder, or a .zip holding one) into the app's own skills folder, for this
    /// app only, under its name. Links inside it are left out. Returns its new folder.
    /// </summary>
    /// <exception cref="InvalidOperationException">It holds no SKILL.md, or one by that name is there already.</exception>
    public string Import(string path)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            var unpacked = Path.Combine(Path.GetTempPath(), "insert-coin-skill-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var zip = ZipFile.OpenRead(path))
                    Fits(zip.Entries.Count, zip.Entries.Sum(e => e.Length));
                ZipFile.ExtractToDirectory(path, unpacked);  // refuses entries that would land outside it
                var skill = File.Exists(Path.Combine(unpacked, FileName)) ? unpacked
                    : Directory.GetFiles(unpacked, FileName, SearchOption.AllDirectories).OrderBy(f => f.Length).Select(Path.GetDirectoryName).FirstOrDefault()
                      ?? throw new InvalidOperationException($"There's no {FileName} in that .zip.");
                return Copy(skill, skill == unpacked ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(skill));
            }
            finally
            {
                if (Directory.Exists(unpacked))
                    Directory.Delete(unpacked, recursive: true);
            }
        }
        var folder = File.Exists(path) && Same(Path.GetFileName(path), FileName) ? Path.GetDirectoryName(path)! : path;
        if (!File.Exists(Path.Combine(folder, FileName)))
            throw new InvalidOperationException($"There's no {FileName} in that folder.");
        return Copy(folder, Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)));
    }

    /// <summary>Refuses a skill holding more than <see cref="ImportFiles"/> files or <see cref="ImportBytes"/> bytes.</summary>
    private static void Fits(long files, long bytes)
    {
        if (files > ImportFiles || bytes > ImportBytes)
            throw new InvalidOperationException($"That's too big for a skill: at most {ImportFiles:N0} files and {ImportBytes / 1024 / 1024} MB.");
    }

    /// <summary>
    /// Copies a skill's folder into the app's own, under its name where the format allows it, else the name it came
    /// with. It is copied beside it under a dot-name first and renamed once whole, so a copy that fails leaves nothing.
    /// </summary>
    private string Copy(string from, string fallback)
    {
        var read = Parse(Head(Path.Combine(from, FileName)).Text);
        var name = read.Name is { } named && Allowed(named) ? named : Listable(fallback);
        var to = Path.Combine(Own, name);
        if (Directory.Exists(to))
            throw new InvalidOperationException($"There's a skill called {name} in this app's folder already.");
        var source = new DirectoryInfo(from);
        var files = Files(source).Take(ImportFiles + 1).ToList();
        Fits(files.Count, files.Sum(f => f.Length));
        var copying = Directory.CreateDirectory(Path.Combine(Own, ".importing-" + Guid.NewGuid().ToString("N")));
        try
        {
            foreach (var file in files)
            {
                var into = Path.Combine(copying.FullName, Path.GetRelativePath(source.FullName, file.FullName));
                Directory.CreateDirectory(Path.GetDirectoryName(into)!);
                file.CopyTo(into);
            }
            Directory.Move(copying.FullName, to);
        }
        catch
        {
            copying.Delete(recursive: true);
            throw;
        }
        return to;
    }

    /// <summary>
    /// A folder's name the list shows (see <see cref="In"/>): no dots or spaces at its start, where a dot-name is an
    /// import under way, or at its end, which Windows drops; nothing a file's name can't hold; "imported-skill" when
    /// nothing is left.
    /// </summary>
    internal static string Listable(string name)
    {
        var kept = new string([.. name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))]).Trim('.', ' ', '\t');
        return kept.Length > 0 ? kept : "imported-skill";
    }

    /// <summary>The files in a folder and the folders in it, leaving out links (see <see cref="IsLink"/>).</summary>
    private static IEnumerable<FileInfo> Files(DirectoryInfo folder) =>
        folder.EnumerateFiles().Where(f => !IsLink(f))
              .Concat(folder.EnumerateDirectories().Where(d => !IsLink(d)).SelectMany(Files));

    /// <summary>A name the format allows: lowercase letters, numbers and single hyphens, at most <see cref="NameLimit"/>.</summary>
    private static bool Allowed(string name) =>
        name.Length is > 0 and <= NameLimit && name.All(c => c == '-' || char.IsLetterOrDigit(c) && !char.IsUpper(c))
        && !name.StartsWith('-') && !name.EndsWith('-') && !name.Contains("--", StringComparison.Ordinal);
}
