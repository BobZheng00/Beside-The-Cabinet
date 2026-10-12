using Godot;
using InsertCoin.Game.Companion;

namespace InsertCoin.Game.App;

/// <summary>
/// A step of Getting started: the page of the settings that sets it up, its name, what it gives and what it takes,
/// whether she needs it, whether it is done, and what it says of it now.
/// </summary>
public sealed record StartStep(string Page, string Name, string About, bool Needed, bool Done, string Now);

/// <summary>
/// Settings → Getting started: the name she calls the player by, then a step for each thing set up outside the
/// cabinet's own screen (her mind, which she needs to talk; her voice, talking aloud, the phone and a calendar, which
/// can wait), each with Set up… (its page) and Later, and what balance starts with. Listed first in Settings and in the
/// tray while anything is left, until hidden (spec 2026-10-10-settings-and-setup.md, "Getting started").
/// </summary>
public partial class GettingStarted : VBoxContainer
{
    /// <summary>The name typed, as it is typed.</summary>
    public event Action<string>? Named;
    /// <summary>Set up… (the step's page), and Later (put off, so it no longer counts as left).</summary>
    public event Action<string>? Open, Later;
    public event Action? HideAsked;

    /// <summary>Whether anything is left: no name yet, or a step not done that she needs or that wasn't put off.</summary>
    public static bool Left(string? name, IEnumerable<StartStep> steps, IReadOnlyCollection<string> later) =>
        string.IsNullOrWhiteSpace(name) || steps.Any(s => !s.Done && (s.Needed || !later.Contains(s.Page)));

    /// <param name="balance">What balance starts with, in a sentence (the day's limits and the bedtime).</param>
    public void Build(string her, string? name, IReadOnlyList<StartStep> steps, IReadOnlyCollection<string> later, string balance)
    {
        Form.Page(this, "Getting started");
        Form.Hint(this, $"A few things to set up, each when you like. Only her mind is needed for {her} to talk with you; the rest can " +
                        "wait, and each stays on its own page, listed on the left.");

        var named = Heading("Your name", name is { Length: > 0 } ? Done : Needed);
        var field = new LineEdit { Text = name ?? "", PlaceholderText = "your name, or what you like to be called", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        field.TextChanged += text =>
        {
            Named?.Invoke(text);
            Mark(named, text.Trim().Length > 0 ? Done : Needed);
        };
        Form.Hint(this, $"What {her} calls you.");
        AddChild(field);

        foreach (var step in steps)
        {
            var put = later.Contains(step.Page);
            var head = Heading(step.Name, step.Done ? Done : step.Needed ? Needed : put ? ("Later", Paper.Faint) : ("Optional", Paper.Letters));
            Form.Button(head.GetParent<HBoxContainer>(), step.Done ? "Change…" : "Set up…", () => Open?.Invoke(step.Page), quiet: step.Done);
            if (!step.Done && !step.Needed && !put)
                Form.Button(head.GetParent<HBoxContainer>(), "Later", () => Later?.Invoke(step.Page));
            Form.Hint(this, step.About);
            Form.Say(Form.Result(this), step.Now, step.Done ? Paper.Good : step.Needed ? Paper.Bad : Paper.Letters);
        }

        var limits = Heading("Balance", ("", Paper.Letters));
        Form.Button(limits.GetParent<HBoxContainer>(), "Change…", () => Open?.Invoke("balance"));
        Form.Hint(this, balance);

        AddChild(new HSeparator());
        var end = Form.Buttons(this);
        var hide = Form.Button(end, "Hide Getting started", () => { });
        var result = Form.Result(end);
        if (!Left(name, steps, later))
            Form.Say(result, "All set: everything is done or put off for later.", Paper.Good);
        hide.Pressed += () =>
        {
            hide.Disabled = true;
            HideAsked?.Invoke();
            Form.Say(result, "Hidden: it leaves Settings and the tray. Each part stays on its own page.", Paper.Faint);
        };
    }

    private static readonly (string Word, Color Colour) Done = ("Done", Paper.Good), Needed = ("Needed", Paper.Bad);

    /// <summary>A step's heading after a line, with a word for how it stands; the row it is in takes its buttons.</summary>
    private Label Heading(string name, (string Word, Color Colour) state)
    {
        AddChild(new HSeparator());
        var row = Form.Buttons(this);
        Form.Subheading(row, name);
        var mark = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        row.AddChild(mark);
        Mark(mark, state);
        return mark;
    }

    private static void Mark(Label mark, (string Word, Color Colour) state) => Form.Say(mark, state.Word, state.Colour);
}
