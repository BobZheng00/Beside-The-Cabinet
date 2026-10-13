using System.Globalization;
using Godot;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// AGENDA on the CRT: what is coming up, four to a page, each with when (today, tomorrow, or the day) and what. A click
/// opens the memory book's Agenda tab, to change it.
/// </summary>
public partial class AgendaApp(MindStore store, Action openAgenda) : CrtApp
{
    private const float Row = 70, Top = 8;
    private List<Plan> _ahead = [];
    private int _page;

    public override string Id => "agenda";
    public override string Label => "AGENDA";
    public override Color Tint => new("CFE6FF");

    public override void Opened()
    {
        var now = DateTimeOffset.Now;
        _ahead = [.. store.Agenda().Where(p => p.End >= now).OrderBy(p => p.At)];
        _page = 0;
        QueueRedraw();
    }

    private int Pages => Math.Max(1, (_ahead.Count + 3) / 4);

    public override (int Page, int Pages) Paging => (_page, Pages);

    public override bool Swipe(int step)
    {
        var page = Math.Clamp(_page + step, 0, Pages - 1);
        if (page == _page)
            return false;
        _page = page;
        QueueRedraw();
        return true;
    }

    public override void Click(Vector2 at) => openAgenda();

    public override void _Draw()
    {
        if (_ahead.Count == 0)
        {
            Centred("NOTHING AHEAD", Pixel, 17, Ink, new Rect2(0, 90, Size.X, 40));
            Centred("Share your plans", Round, 22, Ink with { A = 0.7f }, new Rect2(0, 140, Size.X, 40));
            return;
        }
        var today = DateTimeOffset.Now.Date;
        for (var i = 0; i < 4 && _page * 4 + i < _ahead.Count; i++)
        {
            var plan = _ahead[_page * 4 + i];
            var y = Top + i * Row;
            var day = plan.At.Date == today ? "TODAY" : plan.At.Date == today.AddDays(1) ? "TOMORROW"
                : plan.At.ToString("ddd d MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
            var when = plan.AllDay ? day : $"{day} {plan.At:HH:mm}";
            DrawString(Pixel, new Vector2(30, y + 22), when, HorizontalAlignment.Left, -1, 13, Accent);
            DrawString(Round, new Vector2(30, y + 52), Fit(plan.Text, Round, 23, Size.X - 60), HorizontalAlignment.Left, -1, 23, Ink);
            if (i < 3)
                DrawLine(new Vector2(30, y + Row - 4), new Vector2(Size.X - 30, y + Row - 4), Ink with { A = 0.2f }, 2);
        }
        if (Pages > 1)
            for (var p = 0; p < Pages; p++)
                DrawCircle(new Vector2(Size.X / 2 + (p - (Pages - 1) / 2f) * 20, Size.Y - 14), p == _page ? 6 : 4.5f, p == _page ? Accent : Ink with { A = 0.35f });
    }

    public override void DrawGlyph(CanvasItem on, Rect2 box)
    {
        // a calendar page: a coloured band with two rings, and the days below
        var page = new Rect2(box.Position + new Vector2(box.Size.X * 0.12f, box.Size.Y * 0.16f), new Vector2(box.Size.X * 0.76f, box.Size.Y * 0.72f));
        on.DrawRect(page, new Color("FFF7EC"));
        on.DrawRect(new Rect2(page.Position, new Vector2(page.Size.X, page.Size.Y * 0.28f)), Accent);
        on.DrawRect(page, Ink, false, 3);
        foreach (var x in new[] { 0.3f, 0.7f })
            on.DrawLine(page.Position + new Vector2(page.Size.X * x, -6), page.Position + new Vector2(page.Size.X * x, 8), Ink, 4, true);
        for (var row = 0; row < 2; row++)
            for (var column = 0; column < 3; column++)
                on.DrawCircle(page.Position + new Vector2(page.Size.X * (0.25f + column * 0.25f), page.Size.Y * (0.52f + row * 0.24f)), 3.2f, Ink);
    }
}
