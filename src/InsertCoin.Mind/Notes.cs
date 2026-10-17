using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace InsertCoin.Mind;

/// <summary>What she did to a note, for the skill's page to list and undo.</summary>
public enum NoteChangeKind
{
    Created,
    Appended,
    Edited,
    Moved,
    Deleted,
}

/// <summary>
/// A change she made to a note in a folder a skill works in, kept so the player can undo it: the note as it was is
/// copied into the app's own profile first (never into their folder), named by <see cref="Id"/>, unless she made it.
/// </summary>
/// <param name="Folder">The granted folder, in full.</param>
/// <param name="Path">The note, from that folder, with / between its parts.</param>
/// <param name="To">Where she moved it, the same way; null for any other change.</param>
/// <param name="After">The note's SHA-256 as she left it, so an undo can tell whether anyone changed it since; null once deleted.</param>
public sealed record NoteChange(string Id, DateTimeOffset At, NoteChangeKind Kind, string Folder, string Path, string? To, string? After);

/// <summary>
/// A change she suggests to a skill's instructions as the player's ways change, for them to see on her card before
/// anything is written (docs/specs/2026-10-15-skills.md, step 3).
/// </summary>
/// <param name="Folder">The skill's own folder.</param>
/// <param name="Replaces">The passage she would change, as its SKILL.md has it.</param>
/// <param name="With">What it becomes.</param>
/// <param name="Before">The whole lines the passage is in, as they are now; <paramref name="After"/>, as they would be.</param>
public sealed record SkillChange(string Skill, string Folder, string Replaces, string With, string Before, string After)
{
    /// <summary>
    /// The change in a line, as her card shows it: the new wording, and what it replaces without the words the two
    /// start with alike ("Put tasks under ## Today" (was: "Tasks")).
    /// </summary>
    public string Plainly
    {
        get
        {
            var same = 0;
            while (same < Replaces.Length && same < With.Length && Replaces[same] == With[same])
                same++;
            var cut = same == Replaces.Length ? 0 : Replaces.LastIndexOf(' ', Math.Max(same - 1, 0)) + 1;
            return $"\"{Prompt.Fit(OneLine(With), 120)}\" (was: \"{Prompt.Fit(OneLine(Replaces[cut..]), 80)}\")";
        }
    }

    /// <summary>The exact lines before and after, as See the change shows them.</summary>
    public string Lines => $"Before:\n{Before.Replace("\r", "")}\n\nAfter:\n{After.Replace("\r", "")}";

    private static string OneLine(string text) => string.Join(' ', text.Split((char[])['\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>
/// The folders skills work in: the player's own files a skill is about (a notes vault, a folder of documents), granted
/// to it on its page in Settings → Skills, never by the skill (docs/specs/2026-10-15-skills.md). While a skill is on,
/// her note tools reach its folders and nothing else: each path starts with its folder's name, and is checked here
/// (no "..", no other drive, no link on the way, nothing hidden). Only text is read and written, up to
/// <see cref="Biggest"/>. Before she changes a note, the note as it was is kept in the app's profile, so the player can
/// undo it, for <see cref="KeptFor"/>.
/// </summary>
public sealed class Notes
{
    /// <summary>The most a note may hold to be read or written, in bytes (to tune): notes are text, so far less.</summary>
    public const int Biggest = 1024 * 1024;

    /// <summary>How long what she changed can be undone (to tune).</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(30);

    /// <summary>The kinds of file she may write (and move or delete): text she can read back.</summary>
    public static readonly string[] Writable = [".md", ".markdown", ".txt", ".org", ".csv", ".json", ".yaml", ".yml", ".canvas"];

    private const int Listed = 200, Results = 20;

    /// <summary>The most notes searched in one folder (attachments and other files not counted), saying so when there are more.</summary>
    public const int SearchLimit = 5_000;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly Skills _skills;
    private readonly string _kept;
    private readonly TimeProvider _clock;
    private List<NoteChange>? _changes;
    private readonly object _gate = new();                 // her tools run off the game's thread; the page's Undo on it

    /// <param name="kept">Where what she changed is kept, in the app's profile.</param>
    public Notes(Skills skills, string kept, TimeProvider? clock = null)
    {
        _skills = skills;
        _kept = Path.GetFullPath(kept);
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The folders each skill works in, by <see cref="Skills.KeyOf"/>, as the player granted them.</summary>
    public IReadOnlyDictionary<string, string[]> Grants { get; set; } = new Dictionary<string, string[]>();

    /// <summary>The folders granted to <paramref name="skill"/>.</summary>
    public IReadOnlyList<string> GrantedTo(Skill skill) => Grants.GetValueOrDefault(_skills.KeyOf(skill)) ?? [];

    /// <summary>The app's own profile, which no grant may hold or be in: she would reach her own settings.</summary>
    private string Profile => Path.GetDirectoryName(_skills.Own)!;

    /// <summary>Why <paramref name="folder"/> can't be granted to <paramref name="skill"/>, or null when it can.</summary>
    public string? WhyNot(Skill skill, string folder)
    {
        var full = Trimmed(folder);
        if (!Directory.Exists(full))
            return "That folder isn't there.";
        if (IsDrive(full))
            return "A whole drive can't be granted: choose a folder on it.";
        if (Holds(full, Profile) || Holds(Profile, full))
            return "That folder holds this app's own files, so it can't be granted.";
        return GrantedTo(skill).Any(f => Same(Trimmed(f), full)) ? "That folder is granted already." : null;
    }

    /// <summary>
    /// The folders she can reach now, those granted to the skills switched on, each with the name its paths start with:
    /// the folder's own name, numbered when two share one ("notes", "notes-2"), in the same order every time.
    /// </summary>
    public IReadOnlyList<(string Label, string Folder)> Reachable()
    {
        var all = new List<(string, string)>();
        foreach (var folder in _skills.On().SelectMany(GrantedTo).Select(Trimmed).Where(f => !IsDrive(f) && !Holds(f, Profile) && !Holds(Profile, f)))
        {
            if (all.Any(a => Same(a.Item2, folder)))
                continue;
            var name = Path.GetFileName(folder);
            var label = name;
            for (var n = 2; all.Any(a => string.Equals(a.Item1, label, StringComparison.OrdinalIgnoreCase)); n++)
                label = $"{name}-{n}";
            all.Add((label, folder));
        }
        return all;
    }

    /// <summary>The names <paramref name="skill"/>'s folders go by in her paths (see <see cref="Reachable"/>).</summary>
    public IReadOnlyList<string> LabelsOf(Skill skill, IReadOnlyList<(string Label, string Folder)> reachable) =>
        [.. GrantedTo(skill).Select(Trimmed).Select(f => reachable.FirstOrDefault(r => Same(r.Folder, f)).Label).OfType<string>()];

    // ---- Her tools ------------------------------------------------------------------------------------------------------

    /// <summary>A folder's notes and folders (no path: the folders she can reach), one level, hidden ones and links left out.</summary>
    public string List(string? path)
    {
        var reachable = Reachable();
        if (string.IsNullOrWhiteSpace(path))
            return reachable.Count == 0 ? "No folders are granted to your skills now."
                : "Your skills' folders:\n" + string.Join("\n", reachable.Select(r => $"- {r.Label}/ (for {string.Join(", ", SkillsOf(r.Folder))})"));
        if (Find(path, reachable, out var why) is not { } found)
            return why;
        if (!Directory.Exists(found.Full))
            return File.Exists(found.Full) ? $"{Shown(found)} is a note, not a folder: read it instead." : $"There is no {Shown(found)}.";
        var folder = new DirectoryInfo(found.Full);
        var entries = folder.EnumerateFileSystemInfos().Where(e => !e.Name.StartsWith('.') && !Skills.IsLink(e))
                            .OrderBy(e => e is FileInfo).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (entries.Count == 0)
            return $"{Shown(found)}/ is empty.";
        var lines = entries.Take(Listed).Select(e => e is FileInfo f
            ? $"- {f.Name} ({Size(f.Length)}, changed {When(f.LastWriteTimeUtc)})"
            : $"- {e.Name}/");
        var more = entries.Count > Listed ? $"\n… and {entries.Count - Listed} more." : "";
        return $"{Shown(found)}/:\n{string.Join("\n", lines)}{more}";
    }

    /// <summary>A note's text, cut at <see cref="Skills.ReadLimit"/> characters, saying so.</summary>
    public string Read(string path)
    {
        if (Note(path, out var why) is not { } found)
            return why;
        if (Text(found, out why) is not { } text)
            return why;
        return Skills.Cut(text.Words, Shown(found));
    }

    /// <summary>
    /// The notes that have the most of the words (in their text or their path), most first: each with up to three of its
    /// lines that have the most of them, by line number. Hidden folders, links and files that aren't text are passed over.
    /// </summary>
    public string Search(string words)
    {
        var wanted = words.Split((char[])[' ', '\t', '\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
                          .Select(w => w.Trim('"', '\'')).Where(w => w.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.Count == 0)
            return "Give some words to search for.";
        var reachable = Reachable();
        if (reachable.Count == 0)
            return "No folders are granted to your skills now.";
        var hits = new List<(int Score, DateTime Changed, string Path, string[] Lines)>();
        var (online, unsearched) = (0, new List<string>());
        foreach (var (label, folder) in reachable)
        {
            var notes = Walk(new DirectoryInfo(folder)).Where(f => Writable.Contains(f.Extension, StringComparer.OrdinalIgnoreCase)).Take(SearchLimit + 1).ToList();
            if (notes.Count > SearchLimit)
            {
                unsearched.Add(label);
                notes.RemoveAt(SearchLimit);
            }
            foreach (var file in notes)
            {
                if (file.Length > Biggest)
                    continue;
                if (OnlineOnly(file.Attributes))
                {
                    online++;                               // reading it would download it, one by one: not for a search
                    continue;
                }
                string text;
                try
                {
                    text = Decode(File.ReadAllBytes(file.FullName)).Words;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                var shown = $"{label}/{Path.GetRelativePath(folder, file.FullName).Replace('\\', '/')}";
                int Count(string s) => wanted.Count(w => s.Contains(w, StringComparison.OrdinalIgnoreCase));
                var score = wanted.Count(w => text.Contains(w, StringComparison.OrdinalIgnoreCase) || shown.Contains(w, StringComparison.OrdinalIgnoreCase));
                if (score == 0)
                    continue;
                var lines = text.Replace("\r\n", "\n").Split('\n').Select((line, i) => (Line: line, At: i + 1, Has: Count(line)))
                                .Where(l => l.Has > 0).OrderByDescending(l => l.Has).ThenBy(l => l.At).Take(3).OrderBy(l => l.At)
                                .Select(l => $"  {l.At}: {Prompt.Fit(l.Line.Trim(), 160)}").ToArray();
                hits.Add((score, file.LastWriteTimeUtc, shown, lines));
            }
        }
        var passed = (online == 0 ? "" : $"\n{online} {(online == 1 ? "note is" : "notes are")} kept online only, so not searched (read_note reads one).")
                   + (unsearched.Count == 0 ? "" : $"\nOnly the first {SearchLimit:N0} notes in {string.Join(", ", unsearched)} were searched.");
        if (hits.Count == 0)
            return $"No notes have {string.Join(" or ", wanted)} in them.{passed}";
        var best = hits.OrderByDescending(h => h.Score).ThenByDescending(h => h.Changed).Take(Results)
                       .Select(h => h.Lines.Length == 0 ? $"- {h.Path} (in its name)" : $"- {h.Path}\n{string.Join("\n", h.Lines)}");
        return Skills.Cut($"Notes with {string.Join(", ", wanted)}, the most of them first{(hits.Count > Results ? $" (the first {Results} of {hits.Count})" : "")}:\n" +
                          string.Join("\n", best) + passed, "the results");
    }

    /// <summary>
    /// Whether a file is kept online only (OneDrive's or Dropbox's placeholder): offline, or read only by downloading it
    /// (FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS, or on open).
    /// </summary>
    public static bool OnlineOnly(FileAttributes attributes) =>
        (attributes & (FileAttributes.Offline | (FileAttributes)0x400000 | (FileAttributes)0x40000)) != 0;

    /// <summary>A new note, with <paramref name="text"/> in it; refused where one is there already.</summary>
    public string Create(string path, string text)
    {
        lock (_gate)
        {
            if (Writing(path, mustExist: false, out var why) is not { } found)
                return why;
            if (File.Exists(found.Full) || Directory.Exists(found.Full))
                return $"There is a {Shown(found)} already: add to it or edit it instead.";
            var bytes = Encoding.UTF8.GetBytes(Ended(text));
            if (bytes.Length > Biggest)
                return TooBig(found);
            Directory.CreateDirectory(Path.GetDirectoryName(found.Full)!);
            using (var stream = new FileStream(found.Full, FileMode.CreateNew, FileAccess.Write))
                stream.Write(bytes);
            Add(new NoteChange(NewId(), _clock.GetLocalNow(), NoteChangeKind.Created, found.Folder, found.Relative, null, Hash(bytes)));
            return $"Made {Shown(found)}.";
        }
    }

    /// <summary>Adds <paramref name="text"/> at the end of a note, on a line of its own.</summary>
    public string Append(string path, string text)
    {
        lock (_gate)
        {
            if (Writing(path, mustExist: true, out var why) is not { } found)
                return why;
            if (Changeable(found, out why) is not { } note)
                return why;
            var newline = note.Words.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";   // the note's own line endings
            var added = Ended(text).Replace("\r\n", "\n").Replace("\n", newline);
            var joined = note.Words.Length == 0 || note.Words.EndsWith('\n') ? note.Words + added : note.Words + newline + added;
            return Write(found, note, joined, NoteChangeKind.Appended, $"Added to {Shown(found)}.");
        }
    }

    /// <summary>Replaces a passage of a note, one that is there exactly once, with <paramref name="with"/> (empty takes it out).</summary>
    public string Edit(string path, string passage, string with)
    {
        lock (_gate)
        {
            if (Writing(path, mustExist: true, out var why) is not { } found)
                return why;
            if (Changeable(found, out why) is not { } note)
                return why;
            if (passage.Length == 0)
                return "Give the passage to change, as the note has it.";
            // the note's own line endings: she writes \n, a note made on Windows may have \r\n
            var windows = note.Words.Contains("\r\n", StringComparison.Ordinal);
            string Lines(string text) => windows ? text.Replace("\r\n", "\n").Replace("\n", "\r\n") : text;
            var old = Occurrences(note.Words, passage) == 0 ? Lines(passage) : passage;
            var replacement = Lines(with);
            return Occurrences(note.Words, old) switch
            {
                0 => $"{Shown(found)} doesn't have that passage: read it again and give the passage exactly as it is.",
                1 => Write(found, note, note.Words.Replace(old, replacement, StringComparison.Ordinal), NoteChangeKind.Edited, $"Changed {Shown(found)}."),
                var n => $"That passage is in {Shown(found)} {n} times: give more of it, so it is there only once.",
            };
        }
    }

    /// <summary>Moves or renames a note within its folder; refused where a note is there already.</summary>
    public string Move(string path, string to)
    {
        lock (_gate)
        {
            if (Writing(path, mustExist: true, out var why) is not { } found)
                return why;
            if (Writing(to, mustExist: false, out why) is not { } into)
                return why;
            if (!Same(found.Folder, into.Folder))
                return "A note can only be moved within its own folder.";
            if (File.Exists(into.Full) || Directory.Exists(into.Full))
                return $"There is a {Shown(into)} already.";
            if (Text(found, out why) is not { } note)
                return why;
            var at = Keep(found, note.Bytes, () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(into.Full)!);
                File.Move(found.Full, into.Full);
            });
            Add(new NoteChange(at, _clock.GetLocalNow(), NoteChangeKind.Moved, found.Folder, found.Relative, into.Relative, Hash(note.Bytes)));
            return $"Moved {Shown(found)} to {Shown(into)}.";
        }
    }

    /// <summary>Deletes a note (kept in the app's profile, so the player can undo it).</summary>
    public string Delete(string path)
    {
        lock (_gate)
        {
            if (Writing(path, mustExist: true, out var why) is not { } found)
                return why;
            if (Text(found, out why) is not { } note)
                return why;
            var at = Keep(found, note.Bytes, () => File.Delete(found.Full));
            Add(new NoteChange(at, _clock.GetLocalNow(), NoteChangeKind.Deleted, found.Folder, found.Relative, null, null));
            return $"Deleted {Shown(found)}.";
        }
    }

    // ---- Her suggested changes to a skill ------------------------------------------------------------------------------

    /// <summary>
    /// Her suggested change to a skill's instructions (a passage there exactly once, below its front matter), with the
    /// whole lines it touches before and after, for the player to see; nothing is written. Null, saying why, when it
    /// can't be made.
    /// </summary>
    public SkillChange? Propose(string skill, string replaces, string with, out string why)
    {
        lock (_gate)
        {
            if (_skills.On().FirstOrDefault(s => s.Name == skill.Trim()) is not { } found)
            {
                why = $"There is no skill called {skill} switched on.";
                return null;
            }
            if (SkillFile(found.Name, found.Folder, out why) is not { } file || Changeable(file, out why) is not { } note)
                return null;
            if (replaces.Length == 0 || replaces == with)
            {
                why = "Give the passage to change, as its instructions have it, and what it becomes.";
                return null;
            }
            var windows = note.Words.Contains("\r\n", StringComparison.Ordinal);
            string Lines(string text) => windows ? text.Replace("\r\n", "\n").Replace("\n", "\r\n") : text;
            var old = Occurrences(note.Words, replaces) == 0 ? Lines(replaces) : replaces;
            var at = note.Words.IndexOf(old, StringComparison.Ordinal);
            var times = Occurrences(note.Words, old);
            why = times == 0 ? $"{found.Name}'s instructions don't have that passage: read them again and give it exactly as it is."
                : times > 1 ? $"That passage is in {found.Name}'s instructions {times} times: give more of it, so it is there only once."
                : at < BodyStart(note.Words) ? "That's in its front matter (its name and description), which only the player changes: suggest a change to its instructions."
                : "";
            if (why.Length > 0)
                return null;
            var start = at == 0 ? 0 : note.Words.LastIndexOf('\n', at - 1) + 1;
            var end = note.Words.IndexOf('\n', at + old.Length) is var newline and >= 0 ? newline : note.Words.Length;
            if (end > start && note.Words[end - 1] == '\r')
                end--;
            return new SkillChange(found.Name, found.Folder, replaces, with, note.Words[start..end],
                                   note.Words[start..at] + Lines(with) + note.Words[(at + old.Length)..end]);
        }
    }

    /// <summary>
    /// Writes a change the player agreed to: only its lines, and only if the skill's instructions still have them as they
    /// were shown; otherwise nothing is written, and she is told they changed meanwhile. The old version is kept, so
    /// the skill's page can undo it as it undoes a note.
    /// </summary>
    public string Apply(SkillChange change)
    {
        lock (_gate)
        {
            if (SkillFile(change.Skill, change.Folder, out var why) is not { } file || Changeable(file, out why) is not { } note)
                return why;
            if (Occurrences(note.Words, change.Before) != 1 || note.Words.IndexOf(change.Before, StringComparison.Ordinal) < BodyStart(note.Words))
                return $"{change.Skill}'s instructions have changed since you suggested that, so nothing was written. Read them again before suggesting it again.";
            return Write(file, note, note.Words.Replace(change.Before, change.After, StringComparison.Ordinal), NoteChangeKind.Edited,
                         $"Written: from now on {change.Skill}'s instructions say \"{Prompt.Fit(change.With, 200)}\".");
        }
    }

    /// <summary>
    /// A skill's SKILL.md, if it may be written: only a skill directly in the app's own skills folder or one the player
    /// linked (a skill shipped with the app would be elsewhere, and isn't written in place), neither its folder nor the
    /// file a link.
    /// </summary>
    private Found? SkillFile(string name, string folder, out string why)
    {
        why = "";
        var holder = Path.GetDirectoryName(Trimmed(folder));
        if (holder is null || !_skills.Linked.Prepend(_skills.Own).Any(f => Same(Trimmed(f), holder)))
            why = $"{name} isn't in this app's skills folder or one the player linked, so it can't be changed.";
        else if (!Directory.Exists(folder) || Skills.IsLink(new DirectoryInfo(folder)))
            why = $"{name}'s folder isn't there, or is a link, so it can't be changed.";
        else if (Skills.Inside(folder, Skills.FileName, out var inside) is { } path)
            return new Found(name, Trimmed(folder), Skills.FileName, path);
        else
            why = inside ?? "";
        return null;
    }

    /// <summary>Where a SKILL.md's instructions start: after its front matter, as <see cref="Skills.Parse"/> finds it.</summary>
    private static int BodyStart(string text) =>
        Regex.Match(text, @"\A---[ \t]*\r?\n(?:.*?\r?\n)?(?:---|\.\.\.)[ \t]*(?:\r?\n|\z)", RegexOptions.Singleline) is { Success: true } front ? front.Length : 0;

    // ---- Undo ---------------------------------------------------------------------------------------------------------

    /// <summary>Her changes still kept in <paramref name="folders"/>, the newest first.</summary>
    public IReadOnlyList<NoteChange> ChangesIn(IEnumerable<string> folders)
    {
        lock (_gate)
        {
            var wanted = folders.Select(Trimmed).ToList();
            return [.. Changes().Where(c => wanted.Any(f => Same(f, c.Folder))).OrderByDescending(c => c.At)];
        }
    }

    /// <summary>The note as it was before <paramref name="change"/>, kept in the app's profile; null for a note she made.</summary>
    public string? Before(NoteChange change) => change.Kind == NoteChangeKind.Created ? null : Copy(change);

    /// <summary>
    /// Undoes one of her changes, only if the note is still as she left it, so nothing anyone changed since is ever
    /// overwritten: otherwise it stays as it is, and the version from before stays kept. Says what came of it.
    /// </summary>
    public (bool Done, string Said) Undo(NoteChange change)
    {
        lock (_gate)
        {
            try
            {
                return Undoing(change);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return (false, $"{change.To ?? change.Path} couldn't be undone: {e.Message} Nothing was changed; try again once it's free.");
            }
        }
    }

    private (bool Done, string Said) Undoing(NoteChange change)
    {
        var name = change.To ?? change.Path;
        if (Within(change.Folder, change.Path, mustExist: false, out var why) is not { } at
            || (change.To is null ? at : Within(change.Folder, change.To, mustExist: false, out why)) is not { } now)
            return (false, why);
        var there = File.Exists(now.Full) ? Hash(File.ReadAllBytes(now.Full)) : null;
        if (change.Kind != NoteChangeKind.Deleted && there != change.After)
            return (false, there is null ? $"{name} isn't there now, so nothing was undone."
                                         : $"{name} has changed since, so it was left as it is; the version from before is still kept.");
        if (change.Kind is NoteChangeKind.Deleted or NoteChangeKind.Moved && (File.Exists(at.Full) || Directory.Exists(at.Full)))
            return (false, $"There is a {change.Path} again since, so it was left as it is; the version from before is still kept.");
        if (change.Kind is NoteChangeKind.Appended or NoteChangeKind.Edited or NoteChangeKind.Deleted && !File.Exists(Copy(change)))
            return (false, $"The version of {change.Path} from before isn't kept any more, so nothing was undone.");
        switch (change.Kind)
        {
            case NoteChangeKind.Created:
                File.Delete(now.Full);
                break;
            case NoteChangeKind.Moved:
                Directory.CreateDirectory(Path.GetDirectoryName(at.Full)!);
                File.Move(now.Full, at.Full);
                break;
            default:                                        // appended, edited, deleted: the note as it was
                Directory.CreateDirectory(Path.GetDirectoryName(at.Full)!);
                File.WriteAllBytes(at.Full, File.ReadAllBytes(Copy(change)));
                break;
        }
        Forget(change);
        return (true, change.Kind switch
        {
            NoteChangeKind.Created => $"Undone: {change.Path} is gone again.",
            NoteChangeKind.Moved => $"Undone: {change.To} is back at {change.Path}.",
            NoteChangeKind.Deleted => $"Undone: {change.Path} is back.",
            _ => $"Undone: {change.Path} is as it was.",
        });
    }

    // ---- Paths -----------------------------------------------------------------------------------------------------------

    /// <summary>A note or folder a path names: its granted folder, its path from there (with /) and in full.</summary>
    private sealed record Found(string Label, string Folder, string Relative, string Full);

    private static string Shown(Found found) => found.Relative.Length == 0 ? found.Label : $"{found.Label}/{found.Relative}";

    /// <summary>What <paramref name="path"/> names: its first part is a folder's name (see <see cref="Reachable"/>), the rest inside it.</summary>
    private Found? Find(string path, IReadOnlyList<(string Label, string Folder)> reachable, out string why)
    {
        why = "";
        var given = path.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(given) || given.StartsWith('/') || given.Contains(':'))
        {
            why = $"{path} isn't in your skills' folders: a path starts with the folder's name ({Names(reachable)}).";
            return null;
        }
        var slash = given.IndexOf('/');
        var label = slash < 0 ? given : given[..slash];
        var folder = reachable.FirstOrDefault(r => string.Equals(r.Label, label, StringComparison.OrdinalIgnoreCase));
        if (folder.Folder is null)
        {
            why = reachable.Count == 0 ? "No folders are granted to your skills now."
                : $"There is no folder called {label}: a path starts with the folder's name ({Names(reachable)}).";
            return null;
        }
        return Within(folder.Folder, slash < 0 ? "" : given[(slash + 1)..], mustExist: false, out why) is { } found ? found with { Label = folder.Label } : null;
    }

    private static string Names(IReadOnlyList<(string Label, string Folder)> reachable) => string.Join(", ", reachable.Select(r => r.Label));

    /// <summary>
    /// <paramref name="relative"/> inside the granted <paramref name="folder"/>, if it stays there: no "..", nothing
    /// hidden (a name starting with a dot: .obsidian, .git), no link on the way (links aren't followed; a cloud-synced
    /// folder isn't one, see <see cref="Skills.IsLink"/>), and, with <paramref name="mustExist"/>, there. Each step that
    /// is there is checked by its real name, as the folder lists it: a short name ("CLAUDE~1") reaches a hidden one
    /// (".claude") otherwise.
    /// </summary>
    private static Found? Within(string folder, string relative, bool mustExist, out string why)
    {
        why = "";
        var root = Trimmed(folder);
        var label = Path.GetFileName(root);
        var parts = relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(p => p.Trim() == ".."))
        {
            why = $"{relative} would leave the folder: only what's inside it can be reached.";
            return null;
        }
        if (parts.Any(p => p != p.TrimEnd(' ', '.')))
        {
            why = $"{relative} has a name ending in a space or a dot, which Windows doesn't keep: give the name without it.";
            return null;
        }
        if (parts.Any(p => p.StartsWith('.') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            why = $"{relative} is hidden, or isn't a plain path: only the folder's own notes can be reached.";
            return null;
        }
        if (!Directory.Exists(root))
        {
            why = $"The folder {label} isn't there now.";
            return null;
        }
        var full = Path.GetFullPath(Path.Combine([root, .. parts]));
        if (!Same(full, root) && !Holds(root, full))
        {
            why = $"{relative} would leave the folder: only what's inside it can be reached.";
            return null;
        }
        var real = new List<string>();                      // the steps by their real names, as the folder lists them
        var step = root;
        var steps = Same(full, root) ? [] : Path.GetRelativePath(root, full).Split(Path.DirectorySeparatorChar);
        for (var i = 0; i < steps.Length; i++)
        {
            var info = new DirectoryInfo(step).EnumerateFileSystemInfos(steps[i]).FirstOrDefault();
            if (info is null && Path.Exists(Path.Combine(step, steps[i])))
            {
                why = $"{relative} isn't a plain path (a short name, say): only the folder's own notes can be reached, by their names.";
                return null;
            }
            if (info is null)
            {
                if (!mustExist)
                {
                    real.AddRange(steps[i..]);              // the rest is to be made
                    break;
                }
                why = $"There is no {label}/{string.Join('/', parts)}.";
                return null;
            }
            if (info.Name.StartsWith('.'))
            {
                why = $"{relative} is hidden: only the folder's own notes can be reached.";
                return null;
            }
            if (Skills.IsLink(info))
            {
                why = $"{label}/{string.Join('/', parts)} goes through a link, and links aren't followed: only the folder's own notes can be reached.";
                return null;
            }
            real.Add(info.Name);
            step = info.FullName;
        }
        return new Found(label, root, string.Join('/', real), real.Count == 0 ? root : Path.Combine([root, .. real]));
    }

    /// <summary>A note that is there, by her path.</summary>
    private Found? Note(string path, out string why)
    {
        if (Find(path, Reachable(), out why) is not { } found)
            return null;
        if (Directory.Exists(found.Full))
        {
            why = $"{Shown(found)} is a folder: list it instead.";
            return null;
        }
        if (!File.Exists(found.Full))
        {
            why = $"There is no {Shown(found)}.";
            return null;
        }
        return found;
    }

    /// <summary>A note she may write, by her path: a kind of text file (see <see cref="Writable"/>), there if it must be.</summary>
    private Found? Writing(string path, bool mustExist, out string why)
    {
        if ((mustExist ? Note(path, out why) : Find(path, Reachable(), out why)) is not { } found)
            return null;
        if (!Writable.Contains(Path.GetExtension(found.Full), StringComparer.OrdinalIgnoreCase)
            || Regex.IsMatch(Path.GetFileNameWithoutExtension(found.Full), @"^(con|prn|aux|nul|com\d|lpt\d)$", RegexOptions.IgnoreCase))
        {
            why = $"Only text notes ({string.Join(", ", Writable)}) can be written, so {Shown(found)} can't.";
            return null;
        }
        // a skill's instructions change only as the player approves (docs/specs/2026-10-15-skills.md, step 3), never through these
        if (_skills.Linked.Prepend(_skills.Own).Any(s => Holds(Trimmed(s), found.Full)))
        {
            why = $"{Shown(found)} is in a skills folder: skills aren't changed through notes.";
            return null;
        }
        return found;
    }

    /// <summary>
    /// A note's text as read: its bytes, its words, whether it starts with a byte-order mark, and whether it is UTF-8
    /// (a note in another encoding reads with stand-ins for what isn't, and is never written back).
    /// </summary>
    private sealed record Content(byte[] Bytes, string Words, bool Marked, bool Utf8);

    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static Content Decode(byte[] bytes)
    {
        var marked = bytes.AsSpan().StartsWith((byte[])[0xEF, 0xBB, 0xBF]);
        var (start, length) = (marked ? 3 : 0, bytes.Length - (marked ? 3 : 0));
        try
        {
            return new(bytes, Strict.GetString(bytes, start, length), marked, Utf8: true);
        }
        catch (DecoderFallbackException)
        {
            return new(bytes, Encoding.UTF8.GetString(bytes, start, length), marked, Utf8: false);
        }
    }

    /// <summary>A note's text to change: one in UTF-8 only, so writing it back loses nothing.</summary>
    private static Content? Changeable(Found found, out string why)
    {
        if (Text(found, out why) is not { } note)
            return null;
        if (note.Utf8)
            return note;
        why = $"{Shown(found)} isn't in UTF-8, so it can't be changed without spoiling some of it: it can only be read.";
        return null;
    }

    /// <summary>A note's text, if it is text and not too big.</summary>
    private static Content? Text(Found found, out string why)
    {
        why = "";
        if (new FileInfo(found.Full).Length > Biggest)
        {
            why = TooBig(found);
            return null;
        }
        var bytes = File.ReadAllBytes(found.Full);
        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            why = $"{Shown(found)} isn't text, so it can't be read.";
            return null;
        }
        return Decode(bytes);
    }

    private static string TooBig(Found found) => $"{Shown(found)} is too big: notes up to {Biggest / 1024 / 1024} MB can be read and written.";

    /// <summary>Writes a note's new text, its old one kept first, with the byte-order mark it had.</summary>
    private string Write(Found found, Content note, string text, NoteChangeKind kind, string said)
    {
        var bytes = (note.Marked ? Encoding.UTF8.GetPreamble() : []).Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        if (bytes.Length > Biggest)
            return TooBig(found);
        var id = Keep(found, note.Bytes, () => WriteBytes(found.Full, bytes));
        Add(new NoteChange(id, _clock.GetLocalNow(), kind, found.Folder, found.Relative, null, Hash(bytes)));
        return said;
    }

    /// <summary>
    /// Keeps the note as it is now in the app's profile, then makes her <paramref name="change"/>; returns the change's
    /// id. A change that fails with the note untouched (locked or read-only) takes the copy away again; one that fails
    /// after the note was touched (a vault on a network share or a USB stick gone mid-write, the note cut short) keeps
    /// the copy and records the change, so Undo puts the note back. Either way it says why.
    /// </summary>
    private string Keep(Found found, byte[] bytes, Action change)
    {
        var id = NewId();
        var copy = Path.Combine(_kept, id + Path.GetExtension(found.Full));
        Directory.CreateDirectory(_kept);
        File.WriteAllBytes(copy, bytes);
        try
        {
            change();
        }
        catch
        {
            string? now;
            try
            {
                now = File.Exists(found.Full) ? Hash(File.ReadAllBytes(found.Full)) : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                now = null;                                 // can't tell how it was left: keep the copy
            }
            if (now == Hash(bytes))
                File.Delete(copy);
            else
                Add(new NoteChange(id, _clock.GetLocalNow(), NoteChangeKind.Edited, found.Folder, found.Relative, null, now));
            throw;
        }
        return id;
    }

    /// <summary>How a note's new bytes are written: <see cref="File.WriteAllBytes(string, byte[])"/>; a test makes it fail midway.</summary>
    public Action<string, byte[]> WriteBytes { get; init; } = File.WriteAllBytes;

    private static string NewId() => Guid.NewGuid().ToString("N");

    private string Copy(NoteChange change) => Path.Combine(_kept, change.Id + Path.GetExtension(change.Path));

    // ---- What she changed, kept -------------------------------------------------------------------------------------------

    private string Ledger => Path.Combine(_kept, "changes.json");

    /// <summary>Her changes kept, those older than <see cref="KeptFor"/> pruned (with their copies).</summary>
    private List<NoteChange> Changes()
    {
        var loading = _changes is null;
        if (_changes is null)
        {
            try
            {
                _changes = File.Exists(Ledger) ? JsonSerializer.Deserialize<List<NoteChange>>(File.ReadAllText(Ledger), Json) ?? [] : [];
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
                _changes = [];
            }
        }
        var old = _changes.Where(c => _clock.GetUtcNow() - c.At > KeptFor).ToList();
        foreach (var change in old)
            Forget(change, save: false);
        if (old.Count > 0)
            Save();
        if (loading || old.Count > 0)
            Sweep();
        return _changes;
    }

    /// <summary>Takes away copies older than <see cref="KeptFor"/> that no change names: the app stopped before recording one.</summary>
    private void Sweep()
    {
        if (!Directory.Exists(_kept))
            return;
        var named = _changes!.Select(c => c.Id).ToHashSet();
        foreach (var copy in Directory.EnumerateFiles(_kept).Where(f => !Path.GetFileName(f).StartsWith("changes.json", StringComparison.Ordinal)))
            if (!named.Contains(Path.GetFileNameWithoutExtension(copy)) && _clock.GetUtcNow() - File.GetLastWriteTimeUtc(copy) > KeptFor)
                try
                {
                    File.Delete(copy);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // open elsewhere: next time
                }
    }

    private void Add(NoteChange change)
    {
        Changes().Add(change);
        Save();
    }

    private void Forget(NoteChange change, bool save = true)
    {
        _changes?.RemoveAll(c => c.Id == change.Id);
        if (File.Exists(Copy(change)))
            File.Delete(Copy(change));
        if (save)
            Save();
    }

    private void Save()
    {
        Directory.CreateDirectory(_kept);
        var writing = Ledger + ".new";
        File.WriteAllText(writing, JsonSerializer.Serialize(_changes, Json));
        File.Move(writing, Ledger, overwrite: true);
    }

    // ---- Small things ------------------------------------------------------------------------------------------------------

    /// <summary>The skills switched on that work in <paramref name="folder"/>, by name.</summary>
    private IEnumerable<string> SkillsOf(string folder) => _skills.On().Where(s => GrantedTo(s).Any(f => Same(Trimmed(f), folder))).Select(s => s.Name);

    /// <summary>The files in a folder and the folders in it, leaving out hidden ones and links.</summary>
    private static IEnumerable<FileInfo> Walk(DirectoryInfo folder)
    {
        foreach (var entry in Entries(folder))
            if (entry is FileInfo file)
                yield return file;
            else
                foreach (var inner in Walk((DirectoryInfo)entry))
                    yield return inner;
    }

    /// <summary>What is in a folder, leaving out hidden things and links; nothing for one that can't be read.</summary>
    private static FileSystemInfo[] Entries(DirectoryInfo folder)
    {
        try
        {
            return [.. folder.EnumerateFileSystemInfos().Where(e => !e.Name.StartsWith('.') && !Skills.IsLink(e))];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static int Occurrences(string text, string passage)
    {
        var count = 0;
        for (var at = text.IndexOf(passage, StringComparison.Ordinal); at >= 0; at = text.IndexOf(passage, at + passage.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string Ended(string text) => text.EndsWith('\n') ? text : text + "\n";

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string Trimmed(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a folder is a whole drive (D:\, or a network share's root).</summary>
    private static bool IsDrive(string folder) => Path.GetPathRoot(folder) is { } root && Same(Trimmed(root), folder);

    /// <summary>Whether <paramref name="outer"/> is <paramref name="inner"/> or holds it.</summary>
    private static bool Holds(string outer, string inner) =>
        Same(outer, inner) || inner.StartsWith(outer.EndsWith(Path.DirectorySeparatorChar) ? outer : outer + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string Size(long bytes) => bytes < 1024 ? $"{bytes} bytes" : $"{bytes / 1024.0:0.#} KB";

    private string When(DateTime utc) => TimeZoneInfo.ConvertTime(new DateTimeOffset(utc, TimeSpan.Zero), _clock.LocalTimeZone).ToString("yyyy-MM-dd HH:mm", Prompt.English);
}
