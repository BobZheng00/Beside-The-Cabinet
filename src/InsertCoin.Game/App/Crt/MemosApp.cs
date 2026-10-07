using Godot;
using InsertCoin.Game.Cabinet;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// MEMOS on the CRT: every memo as a little paper tile in its colour, with its sticker and title, the ones on the
/// cabinet first; the first tile writes a new one. A tile opens its memo's sheet over the cabinet. Four to a page.
/// </summary>
public partial class MemosApp(Memos memos) : CrtApp
{
    private static readonly Vector2 Cell = new(180, 136), Grid = new(20, 10), TileSize = new(160, 116);

    private List<Memo> _shown = [];
    private int _page, _hovered = -1;

    public override string Id => "memos";
    public override string Label => "MEMOS";
    public override Color Tint => new("FFE9A6");

    public override void _Ready() => memos.Changed += () =>
    {
        if (Visible)
            Opened();
    };

    public override void Opened()
    {
        // on the cabinet first (hanging, then pinned, the latest on top first), then the rest, newest first
        _shown = [.. memos.All.OrderBy(m => m.Place).ThenByDescending(m => m.Place == MemoPlace.Pinned ? m.Pin : 0).ThenByDescending(m => m.At)];
        _page = Math.Clamp(_page, 0, Pages - 1);
        QueueRedraw();
    }

    private int Pages => Math.Max(1, (_shown.Count + 1 + 3) / 4);         // the new tile comes first

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

    private int? TileAt(Vector2 at)
    {
        for (var slot = 0; slot < 4; slot++)
            if (Tile(slot).HasPoint(at) && _page * 4 + slot <= _shown.Count)
                return _page * 4 + slot;
        return null;
    }

    private static Rect2 Tile(int slot) => new(Grid + new Vector2(slot % 2 * Cell.X, slot / 2 * Cell.Y) + (Cell - TileSize) / 2 - new Vector2(0, 6), TileSize);

    public override void Click(Vector2 at)
    {
        if (TileAt(at) is not { } index)
            return;
        if (index == 0)
            memos.Write();
        else
            memos.Open(_shown[index - 1].Id);
    }

    public override void Hover(Vector2? at)
    {
        var hovered = at is { } p ? TileAt(p) ?? -1 : -1;
        if (hovered == _hovered)
            return;
        _hovered = hovered;
        QueueRedraw();
    }

    public override void _Draw()
    {
        for (var slot = 0; slot < 4; slot++)
        {
            var index = _page * 4 + slot;
            if (index > _shown.Count)
                break;
            var box = Tile(slot);
            var line = index == _hovered ? Accent : Ink;
            if (index == 0)
            {
                // a blank sheet to write on
                Box(box, Glow, line, 3, 10);
                DrawLine(box.GetCenter() + new Vector2(-16, -12), box.GetCenter() + new Vector2(16, -12), line, 6, true);
                DrawLine(box.GetCenter() + new Vector2(0, -28), box.GetCenter() + new Vector2(0, 4), line, 6, true);
                Centred("NEW", Pixel, 15, line, new Rect2(box.Position + new Vector2(0, box.Size.Y - 40), new Vector2(box.Size.X, 30)));
                continue;
            }
            var memo = _shown[index - 1];
            Box(box, MemoLook.Paper(memo.Colour), line, 3, 6);
            if (memo.Place != MemoPlace.Archived)                // on the cabinet: a strip of tape across its top
                DrawRect(new Rect2(box.GetCenter().X - 26, box.Position.Y - 6, 52, 14), new Color(1f, 0.66f, 0.8f, 0.85f));
            if (memo.Sticker is { } sticker)
                MemoLook.Sticker(this, sticker, box.Position + new Vector2(box.Size.X - 20, 20), 24);
            DrawMultilineString(Round, box.Position + new Vector2(10, 32), memo.Title, HorizontalAlignment.Left, box.Size.X - (memo.Sticker is null ? 20 : 44),
                                19, 3, Ink);
        }
        if (Pages > 1)
            for (var p = 0; p < Pages; p++)
                DrawCircle(new Vector2(Size.X / 2 + (p - (Pages - 1) / 2f) * 20, Size.Y - 14), p == _page ? 6 : 4.5f, p == _page ? Accent : Ink with { A = 0.35f });
    }

    public override void DrawGlyph(CanvasItem on, Rect2 box)
    {
        // a memo with a few lines on it, taped up a little askew (turned by hand: the system's own turn and scale are in force)
        var c = box.GetCenter();
        Vector2 At(float x, float y) => c + new Vector2(x * box.Size.X, y * box.Size.Y).Rotated(-0.12f);
        var paper = new[] { At(-0.34f, -0.4f), At(0.34f, -0.4f), At(0.34f, 0.42f), At(-0.34f, 0.42f) };
        on.DrawColoredPolygon(paper, new Color("FFF7EC"));
        on.DrawPolyline([.. paper, paper[0]], Ink, 3, true);
        for (var i = 0; i < 3; i++)
            on.DrawLine(At(-0.22f, -0.16f + i * 0.17f), At(i == 2 ? 0.08f : 0.22f, -0.16f + i * 0.17f), Ink, 3, true);
        on.DrawColoredPolygon([At(-0.16f, -0.48f), At(0.16f, -0.48f), At(0.16f, -0.34f), At(-0.16f, -0.34f)], new Color(1f, 0.55f, 0.75f, 0.9f));
    }
}
