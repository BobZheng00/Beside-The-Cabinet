using Godot;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// DONE on the CRT: the activities that count, as she sized them (see <see cref="Activities"/>), three to a page, each
/// with its worth in coins and how it stands today: counted, half counted, waiting for a picture as proof, or DID IT to
/// log it (the tap is the player's word: no card). × takes one off the list, after a second tap. New ones come from
/// telling her: she sizes them.
/// </summary>
public partial class DoneApp(Activities activities, Func<string, double> paidToday, Func<string, Task<string>> log) : CrtApp
{
    private const int PerPage = 3;
    private int _page;
    private string? _forgetting;                            // the activity whose × was tapped once
    private string? _note;                                  // a word after logging one, for a few seconds
    private ulong _noteUntil;

    public override string Id => "done";
    public override string Label => "DONE";
    public override Color Tint => new("D4F2DF");

    private IReadOnlyList<Activity> Known => activities.Known;
    private int Pages => Math.Max(1, (Known.Count + PerPage - 1) / PerPage);

    public override (int Page, int Pages) Paging => (_page, Pages);

    public override void Opened()
    {
        _page = Math.Min(_page, Pages - 1);
        _forgetting = null;
        QueueRedraw();
    }

    public override void Click(Vector2 at)
    {
        if (!Press(at))
            _forgetting = null;
        QueueRedraw();
    }

    public override void Hover(Vector2? at) => Point(at);

    public override bool Swipe(int step)
    {
        var page = Math.Clamp(_page + step, 0, Pages - 1);
        if (page == _page)
            return false;
        _page = page;
        QueueRedraw();
        return true;
    }

    public override void _Process(double delta)
    {
        if (_note is not null && Time.GetTicksMsec() > _noteUntil)
        {
            _note = null;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        ClearButtons();
        var known = Known;
        if (known.Count == 0)
        {
            Centred("NOTHING YET", Pixel, 15, Ink with { A = 0.75f }, new Rect2(0, 60, Size.X, 26));
            Centred("Say what you did:", Round, 21, Ink with { A = 0.7f }, new Rect2(0, 100, Size.X, 30));
            Centred("it's sized, and shows here", Round, 21, Ink with { A = 0.7f }, new Rect2(0, 130, Size.X, 30));
            return;
        }
        var waiting = activities.Waiting;
        for (var i = 0; i < PerPage && _page * PerPage + i < known.Count; i++)
        {
            var activity = known[_page * PerPage + i];
            var y = 6 + i * 62f;
            Box(new Rect2(14, y, Size.X - 28, 56), Glow, Ink with { A = 0.5f }, 3, 12);
            DrawString(Round, new Vector2(30, y + 27), Fit(activity.Name, Round, 20, 200), HorizontalAlignment.Left, -1, 20, Ink);
            var coins = activity.Size switch { Core.Size.Small => 1, Core.Size.Medium => 2, _ => 4 };
            for (var c = 0; c < coins; c++)
            {
                var at = new Vector2(36 + c * 17, y + 42);
                DrawCircle(at, 6.5f, new Color("F4C542"));
                DrawArc(at, 6.5f, 0, Mathf.Tau, 16, Ink, 2, true);
            }
            var paid = paidToday(activity.Name);
            var status = new Rect2(238, y + 10, 100, 36);
            if (paid >= 1)
                Centred("COUNTED", Pixel, 12, Accent, status);
            else if (waiting.Contains(activity.Name))
                Centred(paid > 0 ? "HALF - PHOTO?" : "PHOTO?", Pixel, 10, Accent, status);
            else
                Button(status, "DID IT", () => _ = Log(activity.Name), chosen: true, font: Round, size: 18);
            var forgetting = _forgetting == activity.Name;
            if (forgetting)
                Button(new Rect2(Size.X - 120, y + 30, 92, 22), "FORGET?", () => Forget(activity.Name), font: Round, size: 13);
            else
                RoundButton(new Vector2(363, y + 28), 12, (c, r, ink) =>
                {
                    DrawLine(c + new Vector2(-r * 0.4f, -r * 0.4f), c + new Vector2(r * 0.4f, r * 0.4f), ink, 3, true);
                    DrawLine(c + new Vector2(-r * 0.4f, r * 0.4f), c + new Vector2(r * 0.4f, -r * 0.4f), ink, 3, true);
                }, () => _forgetting = activity.Name);
        }
        if (_note is not null)
            Centred(_note, Round, 17, Accent, new Rect2(24, 196, Size.X - 48, 24));
        if (Pages > 1)
            for (var p = 0; p < Pages; p++)
                DrawCircle(new Vector2(Size.X / 2 + (p - (Pages - 1) / 2f) * 20, 232), p == _page ? 6 : 4.5f, p == _page ? Accent : Ink with { A = 0.35f });
    }

    private async Task Log(string name)
    {
        var said = await log(name);
        _note = said.StartsWith("Counted", StringComparison.Ordinal) ? "COUNTED!"
            : said.Contains("judge_proof", StringComparison.Ordinal) ? "SHOW A PHOTO OF IT"
            : said.Contains("already", StringComparison.Ordinal) ? "ALREADY COUNTED TODAY" : "NOT COUNTED";
        _noteUntil = Time.GetTicksMsec() + 4000;
        QueueRedraw();
    }

    private void Forget(string name)
    {
        activities.Forget(name);
        _forgetting = null;
        _page = Math.Min(_page, Pages - 1);
        QueueRedraw();
    }

    public override void DrawGlyph(CanvasItem on, Rect2 box)
    {
        // a round stamp with a tick, the cabinet's way of saying "counted"
        var centre = box.GetCenter();
        var r = box.Size.X * 0.34f;
        on.DrawCircle(centre, r, new Color("FFF7EC"));
        on.DrawArc(centre, r, 0, Mathf.Tau, 40, Accent, 4, true);
        on.DrawArc(centre, r * 0.78f, 0, Mathf.Tau, 40, Accent with { A = 0.5f }, 2, true);
        on.DrawPolyline([centre + new Vector2(-r * 0.42f, 0), centre + new Vector2(-r * 0.1f, r * 0.32f), centre + new Vector2(r * 0.46f, -r * 0.34f)],
                        new Color("2E8B57"), 5, true);
    }
}
