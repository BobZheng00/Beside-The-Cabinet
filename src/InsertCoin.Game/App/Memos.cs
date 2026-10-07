using Godot;
using InsertCoin.Game.Cabinet;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// The player's memos (see <see cref="Memo"/>): printed out of the cabinet's slot, by her or as notes to self, read
/// and written on the paper itself, out on the desktop, several at once (see <see cref="MemoSheet"/>), or glanced at by
/// pointing at one, carried by hand and stuck anywhere on the cabinet's side, or kept in the archive (the memory book's
/// Memos tab). The cabinet shows what the store holds; everything that changes a memo goes through here.
/// </summary>
public partial class Memos : Node
{
    private MindStore _store = null!;
    private MemoBoard _board = null!;
    private Func<string?, string> _nameOf = null!;
    private Func<string?> _herName = null!;
    private Func<Vector2, bool> _onCabinet = null!;
    private readonly List<MemoSheet> _sheets = [];          // open on the desktop, oldest first
    private MemoSheet.Draft? _draft;                        // a new memo left unprinted, to carry on with
    private long? _carrying;

    /// <summary>A memo was printed, moved, changed or thrown away (the memory book's tab follows).</summary>
    public event Action? Changed;

    /// <summary>The player printed a memo of their own for her to read (title, text): she sees it come out, and thinks it over.</summary>
    public event Action<string, string>? Written;

    /// <param name="nameOf">Who printed a memo, for the reader ("From Shino"), from a character's id.</param>
    /// <param name="herName">Who is on the stool and can read the player's memos, or null (nobody, or no mind to read with).</param>
    /// <param name="onCabinet">A canvas point is on the cabinet (a memo let go there goes back where it was; off it, it is taken down).</param>
    public void Build(MindStore store, MemoBoard board, Func<string?, string> nameOf, Func<string?> herName, Func<Vector2, bool> onCabinet)
    {
        _store = store;
        _board = board;
        _nameOf = nameOf;
        _herName = herName;
        _onCabinet = onCabinet;
        Show();
    }

    /// <summary>A memo's sheet is over this screen point (the cabinet underneath is not pointed at).</summary>
    public bool Covers(Vector2I screenPoint) => _sheets.Any(s => s.Covers(screenPoint));

    /// <summary>A sheet in a window of its own on the desktop, at a size easy to read whatever the cabinet's size.</summary>
    private MemoSheet NewSheet()
    {
        var scale = Mathf.Max(_board.WindowScale(), 0.82f);
        var window = new Window
        {
            Title = "INSERT COIN memo", Borderless = true, Transparent = true, TransparentBg = true, Transient = true, Visible = false,
            Unfocusable = Paper.OffScreen,                  // a test run never takes focus
            ContentScaleFactor = scale, Size = (Vector2I)(MemoSheet.Outer * scale).Ceil(),
        };
        var sheet = new MemoSheet();
        window.AddChild(sheet);
        AddChild(window);
        sheet.Build(this);
        _sheets.Add(sheet);
        sheet.TreeExiting += () => _sheets.Remove(sheet);
        return sheet;
    }

    /// <summary>A canvas point (on the cabinet) on the screen, in pixels.</summary>
    public Vector2I OnScreen(Vector2 canvasPoint) => (Vector2I)((Vector2)DisplayServer.WindowGetPosition() + canvasPoint * _board.WindowScale()).Round();

    /// <summary>
    /// Where a sheet rests on the screen: beside the cabinet, left of it while there is room, then right of it, each
    /// one open after the first beside the last; with no room left, over the cabinet, a little lower each.
    /// </summary>
    public Vector2I Home(MemoSheet sheet, Vector2I size)
    {
        var cabinet = new Rect2I(DisplayServer.WindowGetPosition(), DisplayServer.WindowGetSize());
        var before = _sheets.Count(s => s != sheet && !s.Leaving);
        var top = cabinet.Position.Y + cabinet.Size.Y / 10;
        var step = size.X + 6;
        if (Paper.OffScreen)
            return new Vector2I(cabinet.Position.X - step * (before + 1), top);    // a test run: off every screen, with the cabinet
        var screen = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        var left = Math.Max(0, (cabinet.Position.X - screen.Position.X) / step);
        var right = Math.Max(0, (screen.End.X - cabinet.End.X) / step);
        var at = before < left ? new Vector2I(cabinet.Position.X - step * (before + 1), top)
               : before < left + right ? new Vector2I(cabinet.End.X + 6 + step * (before - left), top)
               : new Vector2I(cabinet.Position.X + (cabinet.Size.X - size.X) / 2, top) + Vector2I.One * 28 * (before - left - right);
        return at.Clamp(screen.Position, screen.End - size);
    }

    /// <summary>Where a memo is on the cabinet now (the middle of its paper), or null when it isn't on it.</summary>
    public Vector2? Where(long id) =>
        All.FirstOrDefault(m => m.Id == id) is { Place: not MemoPlace.Archived } ? _board.DevPoint(id) : null;

    /// <summary>A new memo left unprinted (null: none), for the next one to start from.</summary>
    public void KeepDraft(MemoSheet.Draft? draft) => _draft = draft;

    public IReadOnlyList<Memo> All => _store.Memos();

    /// <summary>Titles of the memos on the cabinet (hanging, then pinned), for her to know what's there.</summary>
    public IEnumerable<string> OnTheCabinet => All.Where(m => m.Place != MemoPlace.Archived).OrderBy(m => m.Place).ThenBy(m => m.Pin).Select(m => m.Title);

    /// <summary>Who may read the player's memos now (see Build), or null.</summary>
    public string? HerName => _herName();

    /// <summary>
    /// A new memo, hanging from the slot (the one there before goes to the archive). It is kept at once; the paper
    /// comes out of the slot after <paramref name="delay"/> (her sending it from her phone, say).
    /// </summary>
    /// <param name="by">The character who printed it, or null for one the player wrote.</param>
    /// <param name="tell">One of the player's own: she reads it (see <see cref="Written"/>).</param>
    public long Print(string title, string text, string? by, double delay = 0, string? colour = null, string? sticker = null, bool tell = true)
    {
        var id = _store.AddMemo(title, text, by, DateTimeOffset.Now, colour: colour, sticker: sticker);
        if (delay > 0)
            GetTree().CreateTimer(delay).Timeout += () => Show(printing: id);
        else
            Show(printing: id);
        GD.Print($"Memos: printed \"{title}\" ({text.Length} characters){(by is null ? $", the player's own{(tell ? "" : ", kept from her")}" : "")}");
        if (by is null && tell)
            Written?.Invoke(title, text);
        return id;
    }

    /// <summary>The cabinet shows what is kept: the memo hanging from the slot, and those pinned on the side.</summary>
    private void Show(long? printing = null)
    {
        var memos = _store.Memos();
        if (memos.FirstOrDefault(m => m.Place == MemoPlace.Hanging) is { } hanging)
            _board.Hang(Shown(hanging), print: hanging.Id == printing);
        else
            _board.Unhang(tear: false);
        _board.Pin([.. memos.Where(m => m.Place == MemoPlace.Pinned && m.Spot is not null).OrderBy(m => m.Pin)
                           .Select(m => (Shown(m), new Vector2((float)m.Spot!.Value.A, (float)m.Spot.Value.B), (float)m.Tilt))]);
        Changed?.Invoke();
    }

    private static MemoBoard.Shown Shown(Memo memo) => new(memo.Id, memo.Title, memo.Glimpse(160), memo.Colour, memo.Sticker, memo.Glimpse(420));

    /// <summary>A click on the cabinet: a memo there opens. False when the point is on none.</summary>
    public bool Clicked(Vector2 canvasPoint)
    {
        if (_board.Hit(canvasPoint) is not { } id)
            return false;
        Open(id);
        return true;
    }

    public void Hover(Vector2? canvasPoint) => _board.Hover(_devPointer ?? canvasPoint);

    private Vector2? _devPointer;

    /// <summary>Dev: a pretend pointer resting on the memo hanging from the slot (or pinned latest but <paramref name="place"/>), or none.</summary>
    public void DevHover(int? place, bool off = false)
    {
        var memo = place is { } i ? All.Where(m => m.Place == MemoPlace.Pinned).OrderByDescending(m => m.Pin).ElementAtOrDefault(i)
                                  : All.FirstOrDefault(m => m.Place == MemoPlace.Hanging);
        _devPointer = off || memo is null ? null : _board.DevPoint(memo.Id);
    }

    // ---- By hand -----------------------------------------------------------------------------------------------------

    /// <summary>A press here would take hold of a memo.</summary>
    public bool Grabs(Vector2 canvasPoint) => _board.Hit(canvasPoint) is not null;

    /// <summary>A pull began on a memo: off the cabinet it comes (torn off the slot, or peeled off the side), into the hand.</summary>
    public void Take(Vector2 pressedAt, Vector2 now)
    {
        if (_board.Hit(pressedAt) is not { } id)
            return;
        _carrying = id;
        _board.Lift(id, now);
    }

    public void Carry(Vector2 canvasPoint)
    {
        if (_carrying is not null)
            _board.Carry(canvasPoint);
    }

    /// <summary>
    /// Let go: on the side panel it sticks there; elsewhere on the cabinet it goes back where it was; off the cabinet
    /// it is taken down, into the archive.
    /// </summary>
    public void LetGo(Vector2 canvasPoint)
    {
        if (_carrying is not { } id)
            return;
        _carrying = null;
        var spot = _board.SpotAt(canvasPoint);
        _board.PutDown();
        if (spot is { } landing)
        {
            if (!_store.PinMemo(id, landing.Spot.X, landing.Spot.Y, landing.Tilt))
                GD.Print("Memos: the side is full; it goes back");
            else
                GD.Print($"Memos: stuck on the side at {landing.Spot.X:0}, {landing.Spot.Y:0}");
        }
        else if (!_onCabinet(canvasPoint))
        {
            _store.MoveMemo(id, MemoPlace.Archived);
            GD.Print("Memos: taken off the cabinet, into the archive");
        }
        Show();
    }

    // ---- Reading, writing, keeping -------------------------------------------------------------------------------------

    /// <summary>Opens a memo to read, on a sheet of its own (one already open comes to the front instead).</summary>
    public void Open(long id)
    {
        if (All.FirstOrDefault(m => m.Id == id) is not { } memo)
            return;
        if (_sheets.FirstOrDefault(s => s.MemoId == id && !s.Leaving) is { } open)
        {
            open.Raise();
            return;
        }
        var at = Where(id) ?? new Vector2(_board.CabinetMiddle, _board.Canvas.Y - 40);       // one in the archive comes up from below
        NewSheet().Read(memo, From(memo), at, memo.Place == MemoPlace.Pinned ? (float)memo.Tilt : 0);
    }

    /// <summary>Whose it is, as the reader and the memory book say it: "From Shino", or "Your memo".</summary>
    public string From(Memo memo) => memo.By is null ? "Your memo" : $"From {_nameOf(memo.By)}";

    /// <summary>A memo of their own, to write (it prints out of the slot once saved); one being written comes to the front.</summary>
    public void Write()
    {
        if (_sheets.FirstOrDefault(s => s.WritingNew) is { } writing)
            writing.Raise();
        else
            NewSheet().Write(_draft, _board.SlotPoint);
    }

    /// <summary>On the cabinet's side, at a free spot (to move it, it is carried by hand); false when the side is full.</summary>
    public bool Pin(long id)
    {
        if (!_board.CanPin)
            return false;
        var taken = All.Where(m => m.Place == MemoPlace.Pinned && m.Id != id && m.Spot is not null)
                       .Select(m => new Vector2((float)m.Spot!.Value.A, (float)m.Spot.Value.B)).ToList();
        var (spot, tilt) = _board.FreeSpot(taken);
        if (!_store.PinMemo(id, spot.X, spot.Y, tilt))
            return false;
        Show();
        return true;
    }

    /// <summary>Off the cabinet and into the archive: one hanging from the slot is torn off and falls.</summary>
    public void Archive(long id)
    {
        var wasHanging = _store.Memos().Any(m => m.Id == id && m.Place == MemoPlace.Hanging);
        _store.MoveMemo(id, MemoPlace.Archived);
        if (wasHanging)
            _board.Unhang(tear: true);
        Show();
    }

    public void Save(long id, string title, string text)
    {
        _store.UpdateMemo(id, title, text);
        Show();
    }

    /// <summary>Its paper and sticker (null: none).</summary>
    public void Style(long id, string colour, string? sticker)
    {
        _store.StyleMemo(id, colour, sticker);
        Show();
    }

    public void Delete(long id)
    {
        var wasHanging = _store.Memos().Any(m => m.Id == id && m.Place == MemoPlace.Hanging);
        _store.DeleteMemo(id);
        if (wasHanging)
            _board.Unhang(tear: true);
        Show();
    }

    // ---- Dev ---------------------------------------------------------------------------------------------------------

    /// <summary>Dev: opens the memo hanging from the slot, or the one pinned latest but <paramref name="place"/>.</summary>
    public void DevOpen(int? place)
    {
        var memo = place is { } i ? All.Where(m => m.Place == MemoPlace.Pinned).OrderByDescending(m => m.Pin).ElementAtOrDefault(i)
                                  : All.FirstOrDefault(m => m.Place == MemoPlace.Hanging);
        if (memo is not null)
            Open(memo.Id);
    }

    /// <summary>Dev: the latest sheet's tools and choices (pin, off, edit, done, print, delete, close, esc; colour:pink, sticker:star, write:title:text, tell, move:dx:dy).</summary>
    public void DevSheet(string what) => _sheets.LastOrDefault(s => !s.Leaving)?.DevPress(what);

    /// <summary>Dev: carries the hanging memo by hand to a spot on the side (side panel units), or off the cabinet when none, in steps.</summary>
    public async void DevCarry(Vector2? spot)
    {
        if (All.FirstOrDefault(m => m.Place == MemoPlace.Hanging) is not { } memo)
            return;
        var from = _board.DevPoint(memo.Id);
        var to = spot is { } s ? _board.DevPoint(null, s) - new Vector2(0, 20) : from + new Vector2(260, 40);
        Take(from, from);
        for (var step = 1; step <= 20; step++)
        {
            Carry(from.Lerp(to, step / 20f));
            await ToSignal(GetTree().CreateTimer(0.05), SceneTreeTimer.SignalName.Timeout);
        }
        LetGo(to);
    }

    /// <summary>Dev: a few memos to try things on: one hanging, three stuck on the side (one hers with code, one the player's), one archived.</summary>
    public void DevSeed(string by)
    {
        var now = DateTimeOffset.Now;
        var hint = _store.AddMemo("Two Sum: a nudge",
            "You've written two loops. What if, as you walk the list once, you could ask: have I already seen the number that makes this one add up to the target?\n\n" +
            "- What would you need to remember about the numbers you've passed?\n- Which structure answers \"have I seen x?\" in one step?\n\n" +
            "```\nfor i, x in enumerate(nums):\n    # what goes here?\n```", by, now.AddMinutes(-30), MemoPlace.Archived, sticker: "star");
        var plan = _store.AddMemo("This week", "1. Two Mediums a day\n2. One Hard on Saturday\n3. Rest on Sunday, really", by, now.AddMinutes(-20), MemoPlace.Archived, "mint");
        var own = _store.AddMemo("Groceries", "milk, eggs, rice, green tea", null, now.AddMinutes(-10), MemoPlace.Archived, "yellow", "cat");
        _store.AddMemo("Old one", "Archived already.", by, now.AddDays(-2), MemoPlace.Archived);
        _store.PinMemo(hint, 62, 150, -4);
        _store.PinMemo(plan, 122, 148, 3);
        _store.PinMemo(own, 92, 420, -2);
        _store.AddMemo("Sliding window", "Keep a window [left, right] and move the left edge only when the window breaks the rule.", by, now, colour: "pink", sticker: "heart");
        Show();
    }
}
