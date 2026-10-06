using Godot;
using InsertCoin.Game.Cabinet;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// The player's memos (see <see cref="Memo"/>): printed out of the cabinet's slot, by her or as notes to self, read
/// in a window, taped to the cabinet's side, or kept in the archive (the memory book's Memos tab). The cabinet shows
/// what the store holds; everything that changes a memo goes through here.
/// </summary>
public partial class Memos : Node
{
    private MindStore _store = null!;
    private MemoBoard _board = null!;
    private Func<string?, string> _nameOf = null!;
    private MemoReader? _reader;

    /// <summary>A memo was printed, moved, changed or thrown away (the memory book's tab follows).</summary>
    public event Action? Changed;

    /// <param name="nameOf">Who printed a memo, for the reader ("From Shino"), from a character's id.</param>
    public void Build(MindStore store, MemoBoard board, Func<string?, string> nameOf)
    {
        _store = store;
        _board = board;
        _nameOf = nameOf;
        Show();
    }

    public IReadOnlyList<Memo> All => _store.Memos();

    /// <summary>Titles of the memos on the cabinet (hanging, then pinned), for her to know what's there.</summary>
    public IEnumerable<string> OnTheCabinet => All.Where(m => m.Place != MemoPlace.Archived).OrderBy(m => m.Place).ThenBy(m => m.Pin).Select(m => m.Title);

    /// <summary>
    /// A new memo, hanging from the slot (the one there before goes to the archive). It is kept at once; the paper
    /// comes out of the slot after <paramref name="delay"/> (her sending it from her phone, say).
    /// </summary>
    /// <param name="by">The character who printed it, or null for one the player wrote.</param>
    public long Print(string title, string text, string? by, double delay = 0)
    {
        var id = _store.AddMemo(title, text, by, DateTimeOffset.Now);
        if (delay > 0)
            GetTree().CreateTimer(delay).Timeout += () => Show(printing: id);
        else
            Show(printing: id);
        GD.Print($"Memos: printed \"{title}\" ({text.Length} characters){(by is null ? ", the player's own" : "")}");
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
        _board.Pin(memos.Where(m => m.Place == MemoPlace.Pinned).ToDictionary(m => m.Pin, Shown));
        Changed?.Invoke();
    }

    private static MemoBoard.Shown Shown(Memo memo) => new(memo.Id, memo.Title, memo.Glimpse(160));

    /// <summary>A click on the cabinet: a memo there opens. False when the point is on none.</summary>
    public bool Clicked(Vector2 canvasPoint)
    {
        if (_board.Hit(canvasPoint) is not { } id)
            return false;
        Open(id);
        return true;
    }

    public void Hover(Vector2? canvasPoint) => _board.Hover(canvasPoint);

    /// <summary>Opens a memo to read (the window already open shows it instead).</summary>
    public void Open(long id)
    {
        if (All.FirstOrDefault(m => m.Id == id) is not { } memo)
            return;
        Reader().Read(memo, From(memo));
    }

    /// <summary>Whose it is, as the reader and the memory book say it: "From Shino", or "Your memo".</summary>
    public string From(Memo memo) => memo.By is null ? "Your memo" : $"From {_nameOf(memo.By)}";

    /// <summary>A memo of their own, to write (it prints out of the slot once saved).</summary>
    public void Write() => Reader().Write();

    private MemoReader Reader()
    {
        if (IsInstanceValid(_reader))
        {
            _reader!.GrabFocus();
            return _reader;
        }
        _reader = new MemoReader();
        AddChild(_reader);
        _reader.Build(this);
        return _reader;
    }

    /// <summary>Taped to the cabinet's side; false when the side is full.</summary>
    public bool Pin(long id)
    {
        if (_store.Memos().Count(m => m.Place == MemoPlace.Pinned && m.Id != id) >= Math.Min(Memo.PinnedAtMost, _board.Places)
            || !_store.MoveMemo(id, MemoPlace.Pinned))
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

    public void Delete(long id)
    {
        var wasHanging = _store.Memos().Any(m => m.Id == id && m.Place == MemoPlace.Hanging);
        _store.DeleteMemo(id);
        if (wasHanging)
            _board.Unhang(tear: true);
        Show();
    }

    // ---- Dev ---------------------------------------------------------------------------------------------------------

    /// <summary>Dev: opens the memo hanging from the slot, or the one pinned at a place.</summary>
    public void DevOpen(int? place)
    {
        var memo = place is { } i ? All.FirstOrDefault(m => m.Place == MemoPlace.Pinned && m.Pin == i) : All.FirstOrDefault(m => m.Place == MemoPlace.Hanging);
        if (memo is not null)
            Open(memo.Id);
    }

    /// <summary>Dev: the reader's buttons (pin, archive, copy, edit, save, delete).</summary>
    public void DevReader(string button) => _reader?.DevPress(button);

    /// <summary>Dev: a few memos to try things on: one hanging, three pinned (one hers with code, one the player's), one archived.</summary>
    public void DevSeed(string by)
    {
        var now = DateTimeOffset.Now;
        var hint = _store.AddMemo("Two Sum: a nudge",
            "You've written two loops. What if, as you walk the list once, you could ask: have I already seen the number that makes this one add up to the target?\n\n" +
            "- What would you need to remember about the numbers you've passed?\n- Which structure answers \"have I seen x?\" in one step?\n\n" +
            "```\nfor i, x in enumerate(nums):\n    # what goes here?\n```", by, now.AddMinutes(-30));
        var plan = _store.AddMemo("This week", "1. Two Mediums a day\n2. One Hard on Saturday\n3. Rest on Sunday, really", by, now.AddMinutes(-20));
        var own = _store.AddMemo("Groceries", "milk, eggs, rice, green tea", null, now.AddMinutes(-10));
        _store.AddMemo("Old one", "Archived already.", by, now.AddDays(-2), MemoPlace.Archived);
        foreach (var id in new[] { hint, plan, own })
            _store.MoveMemo(id, MemoPlace.Pinned);
        _store.AddMemo("Sliding window", "Keep a window [left, right] and move the left edge only when the window breaks the rule.", by, now);
        Show();
    }
}
