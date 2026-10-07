using Godot;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// FOCUS on the CRT: a focus session (Pomodoro, see <see cref="Focus"/>). Before one, the lengths (a click steps
/// through them), whether their music plays along, and START. During one, the part and its round, the time left, how
/// much is behind, the rounds up to the long break, and pause, skip and stop.
/// </summary>
public partial class FocusApp(Focus focus) : CrtApp
{
    private static readonly int[] FocusLengths = [15, 20, 25, 30, 45, 50, 60, 90], BreakLengths = [3, 5, 10, 15];

    public override string Id => "focus";
    public override string Label => "FOCUS";
    public override Color Tint => new("FFD4CC");

    public override void _Ready() => focus.Changed += () =>
    {
        if (IsVisibleInTree())
            QueueRedraw();
    };

    public override void Opened() => QueueRedraw();
    public override void Click(Vector2 at) => Press(at);
    public override void Hover(Vector2? at) => Point(at);

    public override void _Draw()
    {
        ClearButtons();
        if (focus.Session is { } session)
            DrawSession(session);
        else
            DrawReady();
    }

    private void DrawReady()
    {
        var plan = focus.Plan;
        Centred("READY?", Pixel, 14, Accent, new Rect2(0, 12, Size.X, 24));
        Centred($"{plan.Focus:00}:00", Pixel, 54, Ink, new Rect2(0, 42, Size.X, 70));
        Button(new Rect2(40, 122, 154, 46), $"FOCUS {plan.Focus}", () => Lengths(Following(FocusLengths, plan.Focus), plan.Break), font: Round, size: 21);
        Button(new Rect2(206, 122, 154, 46), $"BREAK {plan.Break}", () => Lengths(plan.Focus, Following(BreakLengths, plan.Break)), font: Round, size: 21);
        Button(new Rect2(40, 178, 320, 42), focus.WithMusic ? "WITH MY MUSIC" : "NO MUSIC", () => focus.WithMusic = !focus.WithMusic,
               chosen: focus.WithMusic, font: Round, size: 20);
        Button(new Rect2(40, 232, 320, 58), "START", () => focus.Start(), chosen: true, size: 24);
    }

    private void DrawSession(FocusSession session)
    {
        if (focus.Readout() is not { } readout)
            return;
        Centred(readout.Caption, Pixel, 14, Accent, new Rect2(0, 12, Size.X, 24));
        Centred(readout.Big, Pixel, 58, Ink, new Rect2(0, 40, Size.X, 76));
        var bar = new Rect2(40, 128, 320, 16);
        DrawRect(bar, Ink with { A = 0.2f });
        DrawRect(bar with { Size = bar.Size with { X = bar.Size.X * Mathf.Clamp(readout.Done, 0, 1) } }, Ink);
        // the rounds up to the long break: done, under way, to come
        var every = session.Plan.LongEvery;
        var into = session.Part == FocusPart.Focus ? (session.Round - 1) % every : (session.Round - 1) % every + 1;
        for (var i = 0; i < every; i++)
        {
            var centre = new Vector2(Size.X / 2 + (i - (every - 1) / 2f) * 30, 176);
            if (i < into)
                DrawCircle(centre, 9, Accent);
            else if (i == into && session.Part == FocusPart.Focus)
                DrawArc(centre, 8, 0, Mathf.Tau, 24, Accent, 4, true);
            else
                DrawCircle(centre, 6, Ink with { A = 0.3f });
        }
        Button(new Rect2(40, 222, 100, 58), session.Paused ? "GO" : "PAUSE", session.Paused ? focus.Resume : focus.Pause, chosen: session.Paused, size: 14);
        Button(new Rect2(150, 222, 100, 58), "SKIP", () => focus.Skip(), size: 14);
        Button(new Rect2(260, 222, 100, 58), "STOP", () => focus.Stop(), size: 14);
    }

    private void Lengths(int focusMinutes, int breakMinutes) => focus.SetLengths(focusMinutes, breakMinutes);

    private static int Following(int[] lengths, int now) => lengths.FirstOrDefault(l => l > now, lengths[0]);

    public override void DrawGlyph(CanvasItem on, Rect2 box)
    {
        // a tomato kitchen timer: the fruit, its leaves, and the dial's mark
        var centre = box.GetCenter() + new Vector2(0, box.Size.Y * 0.06f);
        var r = box.Size.X * 0.36f;
        on.DrawCircle(centre, r, new Color("E8505B"));
        on.DrawArc(centre, r, 0, Mathf.Tau, 40, Ink, 3, true);
        on.DrawArc(centre, r * 0.72f, Mathf.DegToRad(200), Mathf.DegToRad(250), 8, new Color(1, 1, 1, 0.55f), 3, true);     // its shine
        var top = centre - new Vector2(0, r);
        var leaf = new Color("5BAA5B");
        foreach (var angle in new[] { -40f, 0f, 40f })
        {
            var tip = top + new Vector2(0, -r * 0.42f).Rotated(Mathf.DegToRad(angle));
            var side = (tip - top).Orthogonal().Normalized() * r * 0.14f;
            on.DrawColoredPolygon([top + side, tip, top - side], leaf);
        }
        on.DrawLine(top, top + new Vector2(0, -r * 0.3f), Ink, 3, true);
        on.DrawLine(centre, centre + new Vector2(r * 0.42f, -r * 0.42f), Ink, 4, true);
        on.DrawCircle(centre, 3.5f, Ink);
    }
}
