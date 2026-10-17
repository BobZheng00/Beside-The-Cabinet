using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Skills: what she knows how to do the player's way (see <see cref="Skills"/>). The skills in the app's own
/// skills folder, with a new one made there, one imported into it and the folder opened; then each skills folder the
/// player linked, read where it is, with unlinking. Each skill has its name, its description and on/off; one that can't
/// be read says why. Each change is used at once.
/// </summary>
public partial class SkillSettings : VBoxContainer
{
    /// <summary>The native file dialog: its title, mode and filters, and what to do with what was picked.</summary>
    public delegate void Picker(string title, FileDialog.FileModeEnum mode, string[] filters, Action<string[]> picked);

    private Skills _skills = null!;
    private string _name = "";
    private Picker _pick = null!;
    private Action _changed = null!;
    private string? _said;
    private bool _saidBad;

    /// <param name="changed">The linked folders or the skills switched off changed: to save.</param>
    public void Build(Skills skills, string name, Picker pick, Action changed)
    {
        _skills = skills;
        _name = name;
        _pick = pick;
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

    /// <summary>A path that wraps after a \ rather than mid-word: a zero-width space after each is a place to break.</summary>
    private static string Breakable(string path) => path.Replace("\\", "\\\u200B");

    /// <summary>Dev: links a skills folder, as picking it in "Link a skills folder…" does.</summary>
    public void DevLink(string folder) => Link(folder);

    /// <summary>What came of something just done, shown at the top as the page is made again.</summary>
    private void Said(string text, bool bad)
    {
        GD.Print($"Skills: {text.Replace("\u200B", "")}");
        (_said, _saidBad) = (text, bad);
        Fill();
    }
}
