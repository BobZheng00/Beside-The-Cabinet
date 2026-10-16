using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind.Tests;

/// <summary>Her skills: folders with a SKILL.md, read where they are, inside their own folder only (docs/specs/2026-10-15-skills.md).</summary>
public sealed class SkillTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "insert-coin-mind-tests", "skills-" + Guid.NewGuid().ToString("N"));
    private readonly Skills _skills;

    public SkillTests() => _skills = new Skills(Path.Combine(_root, "profile", "skills"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static string Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private string Skill(string name, string body = "Do it this way.", string? description = "Something done the player's way.", string? folder = null) =>
        Write(Path.Combine(folder ?? _skills.Own, name, Skills.FileName),
              $"---\nname: {name}\n{(description is null ? "" : $"description: {description}\n")}---\n\n{body}\n");

    [Fact]
    public void The_front_matter_gives_a_name_and_a_one_line_description_and_the_rest_is_instructions()
    {
        var read = Skills.Parse("﻿---\r\nname: pdf-processing\r\ndescription: Extract PDF text. Use when handling PDFs. # a comment\r\nlicense: Apache-2.0\r\n" +
                                "metadata:\r\n  author: example-org\r\n  version: \"1.0\"\r\n---\r\n# Steps\r\nFirst this.\r\n");

        Assert.Null(read.Problem);
        Assert.Equal("pdf-processing", read.Name);
        Assert.Equal("Extract PDF text. Use when handling PDFs.", read.Description);
        Assert.Equal("# Steps\nFirst this.\n", read.Body);
    }

    [Theory]
    [InlineData("description: >\n  Folded over\n  two lines.", "Folded over two lines.")]
    [InlineData("description: |\n  Kept as\n  written.", "Kept as written.")]
    [InlineData("description: \"Quoted: with a colon\"", "Quoted: with a colon")]
    [InlineData("description: 'It''s quoted'", "It's quoted")]
    [InlineData("description: A plain one\n  that carries on.", "A plain one that carries on.")]
    [InlineData("description:\n  On the next line.", "On the next line.")]
    public void A_description_can_be_written_the_ways_yaml_allows(string field, string description)
    {
        var read = Skills.Parse($"---\nname: tea\n{field}\n---\nBody");

        Assert.Null(read.Problem);
        Assert.Equal(description, read.Description);
    }

    [Theory]
    [InlineData("No front matter at all.", "it has no front matter")]
    [InlineData("---\nname: tea\ndescription: Tea.\nBody without a closing line", "its front matter has no closing --- line")]
    [InlineData("---\ndescription: Tea.\n---\n", "it has no name in its front matter")]
    [InlineData("---\nname: tea\n---\n", "it has no description in its front matter")]
    [InlineData("---\nname: tea\ndescription:\n---\n", "it has no description in its front matter")]
    [InlineData("---\nname: Tea-Time\ndescription: Tea.\n---\n", "isn't one the format allows")]
    [InlineData("---\nname: -tea\ndescription: Tea.\n---\n", "isn't one the format allows")]
    [InlineData("---\nname: tea--time\ndescription: Tea.\n---\n", "isn't one the format allows")]
    [InlineData("---\nname: tea time\ndescription: Tea.\n---\n", "isn't one the format allows")]
    public void A_skill_md_the_format_wont_have_says_why(string text, string why)
    {
        Assert.Contains(why, Skills.Parse(text).Problem);
    }

    [Fact]
    public void Names_and_descriptions_past_the_formats_limits_are_refused()
    {
        Assert.Contains("isn't one the format allows", Skills.Parse($"---\nname: {new string('a', 65)}\ndescription: Tea.\n---\n").Problem);
        Assert.Null(Skills.Parse($"---\nname: {new string('a', 64)}\ndescription: Tea.\n---\n").Problem);
        Assert.Contains("longer than 1,024", Skills.Parse($"---\nname: tea\ndescription: {new string('a', 1025)}\n---\n").Problem);
    }

    [Fact]
    public void A_folder_without_a_skill_md_isnt_a_skill_and_a_broken_one_is_listed_with_why()
    {
        Skill("tea-timer");
        Skill("half-written", description: null);
        Write(Path.Combine(_skills.Own, "just-notes", "notes.txt"), "Not a skill.");
        Write(Path.Combine(_skills.Own, "misnamed", Skills.FileName), "---\nname: other-name\ndescription: Tea.\n---\n");

        var all = _skills.All();

        Assert.Equal(["half-written", "other-name", "tea-timer"], all.Select(s => s.Name));
        Assert.Contains("no description", all[0].Broken);
        Assert.Contains("isn't its folder's name", all[1].Broken);
        Assert.Null(all[2].Broken);
        Assert.Equal(["tea-timer"], _skills.On().Select(s => s.Name));
    }

    [Fact]
    public void Linked_folders_are_read_where_they_are_and_a_skill_switched_off_isnt_hers()
    {
        var vault = Path.Combine(_root, "vault", ".claude", "skills");
        Skill("journal", folder: vault);
        Skill("tea-timer");
        Skill("tea-timer", folder: vault);                  // the same name twice: the app's own comes first
        _skills.Linked = [vault];

        Assert.Equal(["journal", "tea-timer"], _skills.On().Select(s => s.Name));
        Assert.StartsWith(_skills.Own, _skills.On()[1].Folder);
        Assert.Contains("another skill is called tea-timer", _skills.All().Single(s => s.Broken is not null).Broken);
        Assert.False(File.Exists(Path.Combine(_skills.Own, "journal", Skills.FileName)));     // never copied

        _skills.Off = new HashSet<string> { _skills.On()[0].Folder };

        Assert.Equal(["tea-timer"], _skills.On().Select(s => s.Name));
        Assert.Contains("no skill called journal", _skills.Read("journal"));
    }

    [Fact]
    public void A_skill_edited_between_two_uses_is_read_as_it_is_now()
    {
        var file = Skill("tea-timer", "Warm the pot first.");
        Assert.Equal("Warm the pot first.", _skills.Read("tea-timer"));

        File.WriteAllText(file, "---\nname: tea-timer\ndescription: Tea.\n---\nSteep it for four minutes.\n");

        Assert.Equal("Steep it for four minutes.", _skills.Read("tea-timer"));
    }

    [Fact]
    public void A_file_the_instructions_mention_is_read_and_one_they_dont_isnt()
    {
        Skill("tea-timer", "The times are in [the list](references/times.md).");
        Write(Path.Combine(_skills.Own, "tea-timer", "references", "times.md"), "Green: 2 minutes.");
        Write(Path.Combine(_skills.Own, "tea-timer", "references", "secret.md"), "Not mentioned.");
        Write(Path.Combine(_skills.Own, "tea-timer", "times.md"), "Only part of another path.");

        Assert.Equal("Green: 2 minutes.", _skills.Read("tea-timer", "references/times.md"));
        Assert.Equal("Green: 2 minutes.", _skills.Read("tea-timer", @"references\times.md"));
        Assert.Contains("doesn't mention", _skills.Read("tea-timer", "references/secret.md"));
        Assert.Contains("doesn't mention", _skills.Read("tea-timer", "times.md"));
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData("references/../../outside.md")]
    public void A_path_that_tries_to_leave_the_skills_folder_is_refused(string path)
    {
        Write(Path.Combine(_skills.Own, "outside.md"), "Someone else's.");
        Skill("tea-timer", $"See {path}.");

        Assert.Contains("outside the skill's folder", _skills.Read("tea-timer", path));
    }

    [Fact]
    public void An_absolute_path_is_refused_even_when_mentioned()
    {
        var elsewhere = Write(Path.Combine(_root, "elsewhere.md"), "Someone else's.");
        Skill("tea-timer", $"See {elsewhere}.");

        Assert.Contains("outside the skill's folder", _skills.Read("tea-timer", elsewhere));
    }

    [Fact]
    public void A_link_inside_the_skills_folder_is_never_followed()
    {
        var outside = Path.Combine(_root, "outside");
        Write(Path.Combine(outside, "secret.md"), "Someone else's.");
        Skill("tea-timer", "See linked/secret.md.");
        var link = Path.Combine(_skills.Own, "tea-timer", "linked");
        if (!MakeLink(link, outside))
            return;                                         // this PC can't make one: nothing to try

        try
        {
            Assert.Contains("links aren't followed", _skills.Read("tea-timer", "linked/secret.md"));
        }
        finally
        {
            Directory.Delete(link);                         // the link itself, not what it leads to
        }
    }

    /// <summary>A symbolic link where Windows allows one, else a junction (which needs no privilege).</summary>
    private static bool MakeLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        if (!OperatingSystem.IsWindows())
            return false;
        using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!;
        mklink.WaitForExit();
        return Directory.Exists(link);
    }

    [Fact]
    public void A_missing_or_malformed_skill_md_says_so_when_read()
    {
        var file = Skill("tea-timer");
        File.WriteAllText(file, "---\nname: tea-timer\ndescription: Tea.\n---\nFine.");
        Assert.Equal("Fine.", _skills.Read("tea-timer"));
        Assert.Contains("no skill called coffee", _skills.Read("coffee"));

        File.WriteAllText(file, "---\nname: tea-timer\n---\nNo description now.");
        Assert.Contains("no skill called tea-timer", _skills.Read("tea-timer"));

        File.Delete(file);
        Assert.Empty(_skills.All());
    }

    [Fact]
    public void A_skills_script_is_never_run_and_what_isnt_text_isnt_read()
    {
        Skill("tea-timer", "scripts/brew.cmd starts a timer; assets/pot.png shows the pot.");
        var folder = Path.Combine(_skills.Own, "tea-timer");
        Write(Path.Combine(folder, "scripts", "brew.cmd"), "@echo off\r\necho brewed > \"%~dp0brewed.txt\"\r\n");
        Directory.CreateDirectory(Path.Combine(folder, "assets"));
        File.WriteAllBytes(Path.Combine(folder, "assets", "pot.png"), [0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0]);

        Assert.Equal("scripts/brew.cmd starts a timer; assets/pot.png shows the pot.", _skills.Read("tea-timer"));
        Assert.Contains("echo brewed", _skills.Read("tea-timer", "scripts/brew.cmd"));      // read as text, at most
        Assert.Contains("isn't text", _skills.Read("tea-timer", "assets/pot.png"));
        Assert.False(File.Exists(Path.Combine(folder, "scripts", "brewed.txt")));
    }

    [Fact]
    public void A_long_skill_md_is_cut_and_says_so()
    {
        var line = new string('a', 99) + "\n";
        Skill("tea-timer", string.Concat(Enumerable.Repeat(line, 300)));

        var read = _skills.Read("tea-timer");

        Assert.True(read.Length < Skills.ReadLimit + 200);
        Assert.Contains("SKILL.md was cut here", read);
        Assert.StartsWith(line, read);
    }

    [Fact]
    public void An_imported_zip_lands_in_the_apps_own_folder_and_is_listed()
    {
        var made = Path.Combine(_root, "download", "tea-timer");
        Skill("tea-timer", "From a zip.", folder: Path.GetDirectoryName(made));
        Write(Path.Combine(made, "references", "times.md"), "Green: 2 minutes.");
        var zip = Path.Combine(_root, "tea-timer-v2.zip");
        ZipFile.CreateFromDirectory(Path.GetDirectoryName(made)!, zip);

        var folder = _skills.Import(zip);

        Assert.Equal(Path.Combine(_skills.Own, "tea-timer"), folder);
        Assert.True(File.Exists(Path.Combine(folder, "references", "times.md")));
        Assert.Equal(["tea-timer"], _skills.On().Select(s => s.Name));
        Assert.Equal("From a zip.", _skills.Read("tea-timer"));
        Assert.Throws<InvalidOperationException>(() => _skills.Import(zip));               // there already: nothing overwritten
    }

    [Fact]
    public void A_skills_folder_is_imported_by_its_skill_md_and_one_without_is_refused()
    {
        var file = Skill("journal", folder: Path.Combine(_root, "elsewhere"));

        Assert.Equal(Path.Combine(_skills.Own, "journal"), _skills.Import(file));
        Assert.Throws<InvalidOperationException>(() => _skills.Import(Path.Combine(_root, "elsewhere")));
    }

    [Fact]
    public void A_new_skill_waits_for_its_description()
    {
        var folder = _skills.Make();
        var second = _skills.Make();

        Assert.Equal("new-skill", Path.GetFileName(folder));
        Assert.Equal("new-skill-2", Path.GetFileName(second));
        Assert.Contains("no description", _skills.All()[0].Broken);
    }

    [Fact]
    public void Her_skills_are_named_in_the_cached_part_with_her_tools_and_nowhere_without_them()
    {
        Skill("tea-timer");
        var persona = new Persona("Shino", "Shino is cheerful.");

        var first = Prompt.Build(persona, "", "", null, [], "cabinet now", "hi", tools: true, skills: _skills.On())[0].Text;
        var again = Prompt.Build(persona, "", "", null, [], "cabinet later", "hello", tools: true, skills: _skills.On())[0].Text;
        var without = Prompt.Build(persona, "", "", null, [], "", "hi", tools: false, skills: _skills.On())[0].Text;

        Assert.Contains("- tea-timer: Something done the player's way.", first);
        Assert.Equal(first, again);
        Assert.DoesNotContain("tea-timer", without);
    }

    [Fact]
    public async Task Her_skill_tool_reads_a_skill_from_disk()
    {
        Skill("tea-timer", "Warm the pot first.");
        var tool = BuiltIn.ReadSkill(_skills.Read);

        var read = await tool.Function.InvokeAsync(new AIFunctionArguments { ["skill"] = "tea-timer" });

        Assert.Equal("Warm the pot first.", read?.ToString());
        Assert.Equal(Permission.Allowed, tool.Standard);
        Assert.NotEqual(Tool.BuiltIn, tool.Source);         // switched on and off on the Skills page, not Abilities
    }
}
