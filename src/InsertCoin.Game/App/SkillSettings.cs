using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Skills: what she knows how to do the player's way (see <see cref="Skills"/>). The skills in the app's own
/// skills folder, with a new one made there, one imported into it and the folder opened; then each skills folder the
/// player linked, read where it is, with unlinking. Each skill has its name, its description and on/off; one that can't
/// be read says why; and a page of its own, where the player grants it folders to work in (see <see cref="Notes"/>),
/// chooses how freely she works there, and undoes what she changed. Each change is used at once.
/// </summary>
public partial class SkillSettings : VBoxContainer
{
    /// <summary>The native file dialog: its title, mode and filters, and what to do with what was picked.</summary>
    public delegate void Picker(string title, FileDialog.FileModeEnum mode, string[] filters, Action<string[]> picked);

    private Skills _skills = null!;
    private Notes _notes = null!;
    private string _name = "";
    private Picker _pick = null!;
    private Func<Control> _permissions = null!;
    private Action _changed = null!;
    private string? _said;
    private bool _saidBad, _saidByChanges;
    private string? _open;                                  // the skill whose own page shows, by Skills.KeyOf; null: the list

    /// <param name="permissions">Makes the choices of how freely she uses her note tools.</param>
    /// <param name="changed">The linked folders, the skills switched off or the folders granted changed: to save.</param>
    public void Build(Skills skills, Notes notes, string name, Picker pick, Func<Control> permissions, Action changed)
    {
        _skills = skills;
        _notes = notes;
        _name = name;
        _pick = pick;
        _permissions = permissions;
        _changed = changed;
        Fill();
    }

    /// <summary>The page again, as the skills are on disk now.</summary>
    private void Fill()
    {
        foreach (var old in GetChildren())
        {
            RemoveChild(old);
            old.QueueFree();
        }
        if (_open is not null && _skills.All().FirstOrDefault(s => s.Broken is null && _skills.KeyOf(s) == _open) is { } open)
        {
            SkillPage(open);
            return;
        }
        _open = null;
        Form.Page(this, "Skills");
        Form.Hint(this, $"What {_name} knows how to do your way. A skill is a folder with a SKILL.md in it: a name and a one-line description " +
                        "at its top, then the instructions, in the open Agent Skills format that other AI agents (Claude Code among them) read " +
                        $"too. Only each skill's name and description are always with {_name}; its instructions are read when it's used, from " +
                        "the file each time, so a change you make applies the next time. A skill's scripts are never run.");
        if (_said is not null)
            Form.Say(Form.Result(this), _said, _saidBad ? Paper.Bad : Paper.Good);

        var all = _skills.All();
        Form.Subheading(this, "In this app");
        List(all.Where(s => Skills.IsIn(s, _skills.Own)).ToList(), "None yet.");
        var buttons = Form.Buttons(this);
        Form.Button(buttons, "New skill", () =>
        {
            var made = _skills.Make();
            OS.ShellOpen(made);
            Said($"Made {Path.GetFileName(made)}: fill in its description and instructions in its SKILL.md.", bad: false);
        });
        Form.Button(buttons, "Import a skill…", () => _pick("Import a skill", FileDialog.FileModeEnum.OpenFile,
            ["*.zip, SKILL.md ; A skill: its .zip, or the SKILL.md in its folder"], paths =>
            {
                try
                {
                    Said($"Imported {Path.GetFileName(_skills.Import(paths[0]))}.", bad: false);
                }
                catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    Said(e.Message, bad: true);
                }
            }));
        Form.Button(buttons, "Open skills folder", () =>
        {
            Directory.CreateDirectory(_skills.Own);
            OS.ShellOpen(_skills.Own);
        });
        Form.Hint(this, "New skill makes one here to fill in. Import copies a skill here (its .zip, or the SKILL.md in its folder), " +
                        "for this app only.");

        foreach (var folder in _skills.Linked)
        {
            var heading = Form.Subheading(this, Breakable(folder));
            heading.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            heading.CustomMinimumSize = new Vector2(200, 0);
            List(all.Where(s => Skills.IsIn(s, folder)).ToList(),
                 Directory.Exists(folder) ? "No skills in it." : "This folder isn't there now.");
            var row = Form.Buttons(this);
            Form.Button(row, "Open", () => OS.ShellOpen(folder));
            Form.Button(row, "Unlink", () =>
            {
                _skills.Linked = [.. _skills.Linked.Where(f => f != folder)];
                _changed();
                Said($"Unlinked {Breakable(folder)}. Nothing in it was changed.", bad: false);
            });
        }
        Form.Subheading(this, "Elsewhere");
        Form.Button(Form.Buttons(this), "Link a skills folder…", () => _pick("Link a skills folder", FileDialog.FileModeEnum.OpenDir, [], paths => Link(paths[0])));
        Form.Hint(this, "A folder of skills kept elsewhere is read where it is, never copied: Claude Code's own (.claude\\skills in your " +
                        $"user folder), say, or one in your notes. A skill changed there is the same one {_name} reads.");
        Form.Hint(this, $"What a skill says, and any file {_name} reads from one, goes to your AI provider with your conversation.");
    }

    /// <summary>Each skill: on or off, with its description; one that can't be read, with why.</summary>
    private void List(IReadOnlyList<Skill> skills, string none)
    {
        if (skills.Count == 0)
        {
            Form.Hint(this, none);
            return;
        }
        foreach (var skill in skills)
        {
            var row = Form.Buttons(this);
            var key = _skills.KeyOf(skill);
            if (skill.Broken is null)
                Form.Tick(row, skill.Name, !_skills.IsOff(skill), on =>
                {
                    _skills.Off = on ? _skills.Off.Where(k => k != key && k != skill.Folder).ToHashSet() : _skills.Off.Append(key).ToHashSet();
                    _changed();
                });
            else
                row.AddChild(new Label { Text = skill.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true });
            if (skill.Broken is null)
                Form.Button(row, "Folders", () => Show(key));
            Form.Button(row, "Open", () => OS.ShellOpen(skill.Folder));
            var about = Form.Hint(Form.Indented(this), skill.Broken is not null ? $"Can't be used: {skill.Broken}."
                                                       : skill.Hint is null ? skill.Description
                                                       : $"{skill.Description}\nUsed anyway, though {skill.Hint}.");
            if (skill.Broken is not null)
                about.AddThemeColorOverride("font_color", Paper.Bad);
        }
    }

    private void Link(string folder)
    {
        folder = Path.GetFullPath(folder);
        if (File.Exists(Path.Combine(folder, Skills.FileName)))
            Said("That folder is a skill itself: link the folder it's in, or import it.", bad: true);
        else if (folder == Path.GetFullPath(_skills.Own) || _skills.Linked.Contains(folder))
            Said("That folder is here already.", bad: true);
        else
        {
            _skills.Linked = [.. _skills.Linked, folder];
            _changed();
            Said($"Linked {Breakable(folder)}: {Skills.In(folder).Count switch { 0 => "no skills in it yet", 1 => "1 skill in it", var n => $"{n} skills in it" }}.", bad: false);
        }
    }

    /// <summary>
    /// A skill's own page: the folders it works in, each with Remove, and Grant a folder; how freely she works in them;
    /// and her recent changes there, each with Undo, and the note as it was before.
    /// </summary>
    private void SkillPage(Skill skill)
    {
        Form.Page(this, skill.Name);
        if (_said is not null && !_saidByChanges)
            Form.Say(Form.Result(this), _said, _saidBad ? Paper.Bad : Paper.Good);
        Form.Hint(this, skill.Description);

        Form.Subheading(this, "Folders it works in");
        var granted = _notes.GrantedTo(skill);
        if (granted.Count == 0)
            Form.Hint(this, $"None yet: for this skill, {_name} reaches none of your files.");
        foreach (var folder in granted)
        {
            var row = Form.Buttons(this);
            var path = Form.Hint(row, Directory.Exists(folder) ? Breakable(folder) : $"{Breakable(folder)} (it isn't there now)");
            path.CustomMinimumSize = new Vector2(200, 0);
            Form.Button(row, "Open", () => OS.ShellOpen(folder));
            Form.Button(row, "Remove", () =>
            {
                Grant(skill, [.. granted.Where(f => f != folder)]);
                Said($"{skill.Name} no longer works in {Breakable(folder)}. Nothing in it was changed; what {_name} changed there can be " +
                     "undone here again only if you grant it again.", bad: false);
            });
        }
        Form.Button(Form.Buttons(this), "Grant a folder…", () => _pick($"A folder for {skill.Name} to work in", FileDialog.FileModeEnum.OpenDir, [],
                                                                        paths => Grant(skill, paths[0])));
        Form.Hint(this, $"While {skill.Name} is on, {_name} can read, search and write the notes (text files) in these folders, and nothing " +
                        $"else of yours. What {_name} reads there goes to your AI provider with your conversation. A folder kept in sync " +
                        "(OneDrive, say) is fine; links inside one aren't followed, and hidden folders (.obsidian, .git) are left alone.");

        Form.Subheading(this, "Recent changes");
        if (_said is not null && _saidByChanges)            // what Undo came to, where it was pressed
            Reveal(Form.Result(this), _said, _saidBad);
        var changes = _notes.ChangesIn([.. granted, skill.Folder]).Take(Changes).ToList();     // its own SKILL.md too, as the player agreed
        foreach (var change in changes)
        {
            var row = Form.Buttons(this);
            var done = string.Equals(change.Folder, Path.TrimEndingDirectorySeparator(Path.GetFullPath(skill.Folder)), StringComparison.OrdinalIgnoreCase)
                ? $"Changed how {skill.Name} works ({Skills.FileName})" : Done(change, Where(change, granted));
            var said = Form.Hint(row, $"{done} · {When(change.At)}");
            said.CustomMinimumSize = new Vector2(200, 0);
            Form.Button(row, "Undo", () =>
            {
                try
                {
                    var (done, what) = _notes.Undo(change);
                    Said(what, bad: !done, byChanges: true);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Said($"That couldn't be undone: {e.Message}", bad: true, byChanges: true);
                }
            });
            if (_notes.Before(change) is { } before && File.Exists(before))
                Form.Button(row, "Before", () => OS.ShellOpen(before));
        }
        Form.Hint(this, (changes.Count == 0 ? "Nothing yet. " : "") +
                        $"Before {_name} changes a note here, or this skill's instructions as you agreed, this app keeps it as it was, for {Notes.KeptFor.Days} days, so you can undo the change " +
                        $"(Before shows it). Undo puts a note back only if it is still as {_name} left it, so a change made since is never lost.");

        Form.Subheading(this, $"What {_name} may do there");
        AddChild(_permissions());
        Form.Hint(this, "The same in every skill's folders. Moving and deleting ask first unless you allow them. A skill's own files " +
                        "(its SKILL.md) are never changed through notes.");
        Form.Button(Form.Buttons(this), "All skills", () => Show(null));
    }

    /// <summary>Her changes listed on a skill's page, the newest first.</summary>
    private const int Changes = 20;

    /// <summary>
    /// The folder a change is in, as the page names it: its own name, or its whole path when two of the skill's folders
    /// have that name (or the folder isn't granted to it now).
    /// </summary>
    private static string Where(NoteChange change, IReadOnlyList<string> granted)
    {
        var name = Path.GetFileName(change.Folder);
        return granted.Count(f => string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(f)), name, StringComparison.OrdinalIgnoreCase)) == 1
            ? name : Breakable(change.Folder);
    }

    private static string Done(NoteChange change, string folder) => change.Kind switch
    {
        NoteChangeKind.Created => $"Made {folder}/{change.Path}",
        NoteChangeKind.Appended => $"Added to {folder}/{change.Path}",
        NoteChangeKind.Edited => $"Changed {folder}/{change.Path}",
        NoteChangeKind.Moved => $"Moved {folder}/{change.Path} to {change.To}",
        _ => $"Deleted {folder}/{change.Path}",
    };

    /// <summary>What came of an Undo, beside the changes, and the page scrolled to show it once laid out.</summary>
    private async void Reveal(Label label, string text, bool bad)
    {
        Form.Say(label, text, bad ? Paper.Bad : Paper.Good);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInstanceValid(label))
            return;
        for (var at = label.GetParent(); at is not null; at = at.GetParent())
            if (at is ScrollContainer scroll)
            {
                scroll.EnsureControlVisible(label);
                return;
            }
    }

    private static string When(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        var days = (DateTimeOffset.Now.Date - local.Date).Days;
        return $"{(days == 0 ? "today" : days == 1 ? "yesterday" : local.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture))} {local:HH:mm}";
    }

    /// <summary>A folder picked for <paramref name="skill"/> to work in, if it can be.</summary>
    private void Grant(Skill skill, string folder)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (_notes.WhyNot(skill, folder) is { } why)
            Said(why, bad: true);
        else
        {
            Grant(skill, [.. _notes.GrantedTo(skill), folder]);
            Said($"{skill.Name} works in {Breakable(folder)} now.", bad: false);
        }
    }

    private void Grant(Skill skill, string[] folders)
    {
        var grants = new Dictionary<string, string[]>(_notes.Grants);
        if (folders.Length == 0)
            grants.Remove(_skills.KeyOf(skill));
        else
            grants[_skills.KeyOf(skill)] = folders;
        _notes.Grants = grants;
        _changed();
    }

    /// <summary>A skill's own page (by <see cref="Skills.KeyOf"/>), or the list.</summary>
    private void Show(string? key)
    {
        (_open, _said) = (key, null);
        Fill();
    }

    /// <summary>Dev: opens the page of the skill called <paramref name="name"/>, as its Folders button does.</summary>
    public void DevOpen(string name)
    {
        if (_skills.All().FirstOrDefault(s => s.Name == name && s.Broken is null) is { } skill)
            Show(_skills.KeyOf(skill));
        else
            GD.PrintErr($"Skills: no skill called {name}");
    }

    /// <summary>Dev: grants a folder to the skill whose page is open, as picking it in "Grant a folder…" does.</summary>
    public void DevGrant(string folder)
    {
        if (_open is not null && _skills.All().FirstOrDefault(s => _skills.KeyOf(s) == _open) is { } skill)
            Grant(skill, folder);
        else
            GD.PrintErr("Skills: no skill's page is open");
    }

    /// <summary>A path that wraps after a \ rather than mid-word: a zero-width space after each is a place to break.</summary>
    private static string Breakable(string path) => path.Replace("\\", "\\\u200B");

    /// <summary>Dev: links a skills folder, as picking it in "Link a skills folder…" does.</summary>
    public void DevLink(string folder) => Link(folder);

    /// <summary>What came of something just done, shown at the top as the page is made again.</summary>
    /// <param name="byChanges">Said by the recent changes (an Undo), not at the top.</param>
    private void Said(string text, bool bad, bool byChanges = false)
    {
        GD.Print($"Skills: {text.Replace("\u200B", "")}");
        (_said, _saidBad, _saidByChanges) = (text, bad, byChanges);
        Fill();
    }
}
