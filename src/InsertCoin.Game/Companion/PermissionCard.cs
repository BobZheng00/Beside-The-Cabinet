using Godot;
using InsertCoin.Mind;

namespace InsertCoin.Game.Companion;

/// <summary>
/// The card by her stool when she asks to use a tool that acts outside the app (see <see cref="Toolbox"/>): what
/// she wants to do and on what, always the real address whatever she said about it, with Allow once, Always (for
/// that site) and No. Clicks only: a key pressed in another app never answers it.
/// </summary>
public partial class PermissionCard : Node2D
{
    public const float Width = 340;

    /// <summary>How long a card waits for an answer; after that it counts as none.</summary>
    public const double Waits = 120;

    private PanelContainer _panel = null!;
    private Label _what = null!, _detail = null!;
    private Button _once = null!, _always = null!, _no = null!;
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
            AutowrapMode = TextServer.AutowrapMode.Arbitrary, MaxLinesVisible = 2, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            CustomMinimumSize = new Vector2(Width - 32, 0),
        };
        _detail.AddThemeFontSizeOverride("font_size", 14);
        _detail.AddThemeColorOverride("font_color", Paper.Accent);
        rows.AddChild(_what);
        rows.AddChild(_detail);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 6);
        _once = Choice("Allow once", "", Answer.Once);
        _always = Choice("Always", "Quiet", Answer.Always);
        _no = Choice("No", "Quiet", Answer.No);
        buttons.AddChild(_once);
        buttons.AddChild(_always);
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
            button.Pressed += () => _answer?.TrySetResult(answer);
            return button;
        }
    }

    /// <summary>Shows the card standing at <paramref name="bottomLeft"/> and waits for the player's answer.</summary>
    /// <returns>Their answer; <see cref="Answer.None"/> when the reply was called off or they let it be.</returns>
    public async Task<Answer> Ask(Asking asking, Vector2 bottomLeft, CancellationToken cancel)
    {
        _answer?.TrySetResult(Answer.None);                    // one question at a time
        var answer = _answer = new TaskCompletionSource<Answer>(TaskCreationOptions.RunContinuationsAsynchronously);
        _what.Text = asking.What;
        _detail.Text = asking.Detail;
        _detail.Visible = asking.Detail.Length > 0;
        _always.Text = asking.Scope is { } site ? $"Always for {site}" : "Always";
        _always.TooltipText = asking.Scope is { } scope
            ? $"From now on she may do this on {scope} without asking. Chat settings → Abilities can take it back."
            : "From now on she may do this without asking. Chat settings → Abilities can take it back.";
        Position = bottomLeft;
        _tween?.Kill();
        Visible = true;
        Scale = new Vector2(1, 0.4f);
        Modulate = new Color(1, 1, 1, 0);
        _tween = CreateTween();
        _tween.TweenProperty(this, "scale", Vector2.One, 0.22).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.Parallel().TweenProperty(this, "modulate:a", 1f, 0.12);

        await using var called = cancel.Register(() => answer.TrySetResult(Answer.None));
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

    /// <summary>Dev: answers the card showing, as its buttons would.</summary>
    public void DevAnswer(Answer answer) => _answer?.TrySetResult(answer);
}
