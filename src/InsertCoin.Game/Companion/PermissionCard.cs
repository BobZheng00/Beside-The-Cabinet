using Godot;
using InsertCoin.Mind;

namespace InsertCoin.Game.Companion;

/// <summary>
/// The card by her stool when she asks to use a tool that acts outside the app (see <see cref="Toolbox"/>): what
/// she wants to do and on what, always the real address whatever she said about it, with Allow once, Always (for
/// that site) and No. Clicks only: a key pressed in another app never answers it. The same card confirms an activity
/// she counts (see <see cref="Activities"/>): Yes, count it, or Not yet; and shows her suggested change to a skill (see
/// <see cref="SkillChange"/>): Remember it or Not now, with See the change opening the exact lines; and tells the player
/// when their ChatGPT plan stops answering, with what to do about it (see <see cref="Tell"/>), or asks whether she carries
/// on with their backup meanwhile (see <see cref="AskOver"/>).
/// </summary>
public partial class PermissionCard : Node2D
{
    public const float Width = 340;

    /// <summary>How long a card waits for an answer; after that it counts as none.</summary>
    public const double Waits = 120;

    private PanelContainer _panel = null!;
    /// <summary>The most height See the change takes, the rest of the lines a scroll away: the card stays below her face.</summary>
    public const float LinesTallest = 170;

    private Label _what = null!, _detail = null!, _lines = null!;
    private ScrollContainer _scroll = null!;
    private Button _once = null!, _always = null!, _no = null!, _see = null!;
    private TaskCompletionSource<Answer>? _answer;
    private Tween? _tween;

    public bool IsOpen => _answer is not null;

    public override void _Ready()
    {
        _panel = new PanelContainer { Theme = Paper.WindowTheme(), CustomMinimumSize = new Vector2(Width, 0) };
        _panel.AddThemeStyleboxOverride("panel", Paper.Box(Paper.Sheet, Paper.Ink, 2, 12, 14, 10));
        _panel.Resized += () => _panel.Position = new Vector2(0, -_panel.Size.Y);      // it stands on its bottom edge, however tall
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 4);
        _what = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(Width - 32, 0) };
        _what.AddThemeFontOverride("font", Paper.Font(600));
        _what.AddThemeFontSizeOverride("font_size", 16);
        _what.AddThemeColorOverride("font_color", Paper.Ink);
        _detail = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 3, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            CustomMinimumSize = new Vector2(Width - 32, 0),
        };
        _detail.AddThemeFontSizeOverride("font_size", 14);
        _detail.AddThemeColorOverride("font_color", Paper.Accent);
        _lines = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _lines.AddThemeFontSizeOverride("font_size", 13);
        _lines.AddThemeColorOverride("font_color", Paper.Ink);
        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(Width - 32, 0), Visible = false,
        };
        _scroll.AddChild(_lines);
        rows.AddChild(_what);
        rows.AddChild(_detail);
        rows.AddChild(_scroll);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 6);
        _once = Choice("Allow once", "", Answer.Once);
        _always = Choice("Always", "Quiet", Answer.Always);
        _no = Choice("No", "Quiet", Answer.No);
        _see = new Button
        {
            Text = "See the change", ThemeTypeVariation = "Quiet", FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        _see.AddThemeFontSizeOverride("font_size", 14);
        _see.Pressed += SeeChange;
        buttons.AddChild(_once);
        buttons.AddChild(_always);
        buttons.AddChild(_see);
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        buttons.AddChild(_no);
        rows.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        rows.AddChild(buttons);
        _panel.AddChild(rows);
        AddChild(_panel);
        Visible = false;

        Button Choice(string text, string look, Answer answer)
        {
            var button = new Button
            {
                Text = text, ThemeTypeVariation = look, FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            };
            button.AddThemeFontSizeOverride("font_size", 14);
            button.Pressed += () => Pressed(answer);
            return button;
        }
    }

    /// <summary>Shows the card standing at <paramref name="bottomLeft"/> and waits for the player's answer.</summary>
    /// <returns>Their answer; <see cref="Answer.None"/> when the reply was called off or they let it be.</returns>
    public Task<Answer> Ask(Asking asking, Vector2 bottomLeft, CancellationToken cancel)
    {
        _once.Text = "Allow once";
        _no.Text = "No";
        _always.Visible = true;
        _see.Visible = false;
        _detail.MaxLinesVisible = 3;
        _detail.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _always.Text = asking.Scope is { } site ? $"Always for {site}" : "Always";
        _always.TooltipText = Words.Wrap(asking.Scope is { } scope
            ? $"From now on this is allowed on {scope} without asking. Settings → Abilities can take it back."
            : "From now on this is allowed without asking. Settings → Abilities can take it back.");
        return Show(asking.What, asking.Detail, bottomLeft, cancel);
    }

    /// <summary>Asks the player to confirm an activity she counts: true for yes, false for not yet, null when they let it be.</summary>
    public async Task<bool?> Confirm(string what, string detail, Vector2 bottomLeft, CancellationToken cancel)
    {
        _once.Text = "Yes, count it";
        _no.Text = "Not yet";
        _always.Visible = false;                                // each time is its own: "I'm going" isn't "I went"
        _see.Visible = false;
        _detail.MaxLinesVisible = 3;
        _detail.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        return await Show(what, detail, bottomLeft, cancel) switch { Answer.Once or Answer.Always => true, Answer.No => false, _ => null };     // "always" said aloud is a yes, as by text
    }

    /// <summary>
    /// Shows her suggested change to a skill: what changes in a line, Remember it or Not now, and See the change for the
    /// exact <paramref name="lines"/>, all of them, scrolled when long. The line (kept short by
    /// <see cref="SkillChange.Plainly"/>) is shown whole. True for Remember it, false for Not now, null when they let it be.
    /// </summary>
    public async Task<bool?> Suggest(string what, string detail, string lines, Vector2 bottomLeft, CancellationToken cancel)
    {
        _once.Text = "Remember it";
        _no.Text = "Not now";
        _always.Visible = false;
        _see.Visible = true;
        _detail.MaxLinesVisible = -1;
        _detail.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;     // whole: a trimming label has no height of its own
        _lines.Text = lines;
        return await Show(what, detail, bottomLeft, cancel) switch { Answer.Once or Answer.Always => true, Answer.No => false, _ => null };
    }

    /// <summary>
    /// Tells the player something with one thing to do about it, <paramref name="action"/>, and Close (the ChatGPT plan's
    /// limit reached, say), or with Close alone (no action): true when they chose the action. Clicks only: the action may
    /// open their browser.
    /// </summary>
    public async Task<bool> Tell(string what, string detail, string? action, Vector2 bottomLeft, CancellationToken cancel)
    {
        _once.Text = action ?? "";
        _no.Text = "Close";
        _always.Visible = false;
        _see.Visible = false;
        _detail.MaxLinesVisible = 3;
        _detail.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        return await Show(what, detail, bottomLeft, cancel, clicksOnly: true) == Answer.Once;
    }

    /// <summary>
    /// Asks whether she carries on with the player's backup while their ChatGPT plan has stopped: Yes or Not now, with
    /// <paramref name="aside"/> (Manage usage, say), which does <paramref name="doAside"/> and leaves the question open.
    /// Clicks only, so a yes is the player's own, and it waits at the PC until answered: true for Yes; false for Not now,
    /// or when it is taken back (see <see cref="Withdraw"/>).
    /// </summary>
    public async Task<bool> AskOver(string what, string detail, string aside, Action doAside, Vector2 bottomLeft)
    {
        _once.Text = "Yes";
        _no.Text = "Not now";
        _always.Text = aside;
        _always.TooltipText = "";
        _always.Visible = true;
        _see.Visible = false;
        _detail.MaxLinesVisible = 3;
        _detail.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        return await Show(what, detail, bottomLeft, CancellationToken.None, clicksOnly: true, aside: doAside, waits: false) == Answer.Once;
    }

    private bool _clicksOnly;                                   // the card showing takes no spoken answer (see Choose)
    private Action? _aside;                                     // its middle button does this instead of answering (see AskOver)

    /// <summary>A button pressed: the answer, or the card's aside, which leaves the question open.</summary>
    private void Pressed(Answer answer)
    {
        if (answer == Answer.Always && _aside is { } aside)
            aside();
        else
            _answer?.TrySetResult(answer);
    }

    /// <summary>Dev: presses the card's button for this answer, as a click would (clicks-only cards too).</summary>
    public void Press(Answer answer) => Pressed(answer);

    /// <summary>Takes back what <see cref="Tell"/> is telling, when it no longer holds (the player disconnected, say).</summary>
    public void Withdraw()
    {
        if (_clicksOnly)
            _answer?.TrySetResult(Answer.None);
    }

    /// <summary>See the change: opens or closes the exact lines (a dev action too).</summary>
    public void SeeChange()
    {
        if (!_see.Visible)
            return;
        _scroll.Visible = !_scroll.Visible;
        _see.Text = _scroll.Visible ? "Hide the change" : "See the change";
        _scroll.ScrollVertical = 0;
        _scroll.CustomMinimumSize = new Vector2(Width - 32, LinesTallest);
        _panel.ResetSize();
        if (_scroll.Visible)
            FitLines();
    }

    /// <summary>Dev: opens See the change and scrolls to the end of the lines.</summary>
    public async void SeeChangeEnd()
    {
        if (!_scroll.Visible)
            SeeChange();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _scroll.ScrollVertical = (int)_lines.Size.Y;
    }

    /// <summary>Once the lines are laid out at the card's width: the area as tall as they are, up to <see cref="LinesTallest"/>.</summary>
    private async void FitLines()
    {
        // as laid out (the scroll bar's range), not as the label measures itself: the bar's width changes the wrapping, so
        // a few frames, the area only growing
        for (var i = 0; i < 6; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!_scroll.Visible)
                return;
            var padding = _scroll.GetThemeStylebox("panel").GetMinimumSize().Y;      // the area's own box around the lines
            var need = Math.Min(LinesTallest, padding + (float)Math.Max(_scroll.GetVScrollBar().MaxValue, _lines.GetCombinedMinimumSize().Y));
            if (i > 0 && need <= _scroll.CustomMinimumSize.Y)
                continue;
            _scroll.CustomMinimumSize = new Vector2(Width - 32, need);
            _panel.ResetSize();
        }
        GD.Print($"Card: the change's lines {_scroll.GetVScrollBar().MaxValue:0} px tall, shown {_scroll.Size.Y:0} px");
    }

    /// <param name="waits">False: it waits for its answer however long that takes, not <see cref="Waits"/>.</param>
    private async Task<Answer> Show(string what, string detail, Vector2 bottomLeft, CancellationToken cancel, bool clicksOnly = false, Action? aside = null,
                                    bool waits = true)
    {
        _answer?.TrySetResult(Answer.None);                    // one question at a time
        _clicksOnly = clicksOnly;
        _aside = aside;
        _once.Visible = _once.Text.Length > 0;
        var answer = _answer = new TaskCompletionSource<Answer>(TaskCreationOptions.RunContinuationsAsynchronously);
        _what.Text = what;
        _detail.Text = detail;
        _detail.Visible = detail.Length > 0;
        _scroll.Visible = false;
        _see.Text = "See the change";
        _panel.ResetSize();                                     // as small as this card's words, after a taller one
        Position = bottomLeft;
        _tween?.Kill();
        Visible = true;
        Scale = new Vector2(1, 0.4f);
        Modulate = new Color(1, 1, 1, 0);
        _tween = CreateTween();
        _tween.TweenProperty(this, "scale", Vector2.One, 0.22).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.Parallel().TweenProperty(this, "modulate:a", 1f, 0.12);

        await using var called = cancel.Register(() => answer.TrySetResult(Answer.None));
        if (waits)
            GetTree().CreateTimer(Waits).Timeout += () => answer.TrySetResult(Answer.None);
        var given = await answer.Task;
        if (_answer == answer)
        {
            _answer = null;
            Close();
        }
        return given;
    }

    private void Close()
    {
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(this, "modulate:a", 0f, 0.15);
        _tween.TweenCallback(Callable.From(() => Visible = false));
    }

    public bool Hits(Vector2 canvasPoint) => Visible && new Rect2(Position + _panel.Position, _panel.Size).Grow(4).HasPoint(canvasPoint);

    /// <summary>Answers the card showing as its buttons would: the player said it aloud (see <see cref="SpokenAnswer"/>), or a dev action.</summary>
    public void Choose(Answer answer)
    {
        if (!_clicksOnly)
            _answer?.TrySetResult(answer);
    }
}
