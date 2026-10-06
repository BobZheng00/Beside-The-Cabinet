using Godot;
using InsertCoin.Game.Skins;

namespace InsertCoin.Game.Cabinet;

/// <summary>
/// The memos on the cabinet: the one hanging from the printer's slot, just printed, and those taped to the side panel.
/// Each is paper drawn once and projected onto its place, as the receipt is. Knows nothing of where memos are kept;
/// the app says what to show.
/// </summary>
public partial class MemoBoard : Node2D
{
    /// <summary>What a memo's paper shows: its title, and its first words.</summary>
    public readonly record struct Shown(long Id, string Title, string Glimpse);

    private CabinetSkin _skin = null!;
    private MemoPaper _hanging = null!;
    private readonly List<MemoPaper> _pinned = [];
    private MemoPaper? _hovered;

    public void Build(CabinetSkin skin)
    {
        _skin = skin;
        foreach (var quad in skin.Anchors.MemoPins)
        {
            var paper = new MemoPaper(quad, skin.Anchors.MemoSize, taped: true);
            AddChild(paper);
            _pinned.Add(paper);
        }
        _hanging = new MemoPaper(skin.Anchors.ReceiptQuad, skin.Anchors.ReceiptSize, taped: false);
        AddChild(_hanging);
    }

    /// <summary>How many fit on the side: none for a skin that has no places for them.</summary>
    public int Places => _pinned.Count;

    /// <summary>A memo hangs from the slot; <paramref name="print"/>: it prints out of it first.</summary>
    public void Hang(Shown memo, bool print)
    {
        _hanging.Show(memo);
        if (print)
            _hanging.PrintOut(1.6);
    }

    /// <summary>Nothing hangs from the slot any more; <paramref name="tear"/>: it is torn off and falls away.</summary>
    public void Unhang(bool tear)
    {
        if (tear && _hanging.Visible)
            _hanging.TearOff();
        else
            _hanging.Hide();
    }

    /// <summary>The memos taped to the side, each at its place (beyond the places there are, they aren't shown).</summary>
    public void Pin(IReadOnlyDictionary<int, Shown> byPlace)
    {
        for (var place = 0; place < _pinned.Count; place++)
        {
            if (byPlace.TryGetValue(place, out var memo))
                _pinned[place].Show(memo);
            else
                _pinned[place].Clear();
        }
    }

    /// <summary>The memo under a canvas point, or null.</summary>
    public long? Hit(Vector2 canvasPoint) => Paper(canvasPoint)?.Memo;

    /// <summary>The pointer is over a memo (it brightens, as the CHECK button does), or not.</summary>
    public void Hover(Vector2? canvasPoint)
    {
        var paper = canvasPoint is { } p ? Paper(p) : null;
        if (paper == _hovered)
            return;
        if (_hovered is not null)
            _hovered.Modulate = Colors.White;
        _hovered = paper;
        if (paper is not null)
            paper.Modulate = new Color(1.08f, 1.08f, 1.12f);
    }

    private MemoPaper? Paper(Vector2 canvasPoint) =>
        _pinned.Append(_hanging).LastOrDefault(p => p.Visible && p.Memo is not null && Geometry2D.IsPointInPolygon(canvasPoint, p.Quad));

    /// <summary>Dev: a canvas point on the memo hanging from the slot, or on the side at a place.</summary>
    public Vector2 DevPoint(int? place) => (place is { } i ? _pinned[i].Quad : _hanging.Quad).Aggregate(Vector2.Zero, (sum, p) => sum + p) / 4;
}

