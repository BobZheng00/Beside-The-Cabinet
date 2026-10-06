using Godot;
using InsertCoin.Game.Cabinet;

namespace InsertCoin.Game.App;

/// <summary>
/// The CRT's own small system: a click on the screen opens its menu, the item under the pointer is marked, and a
/// click on one does it. For now: write a memo, see the memos. Focus sessions, music and mini-games join it later
/// (docs/specs/2026-10-05-tools-and-abilities.md). It closes by itself after a while untouched, or on BACK.
/// </summary>
public partial class CrtMenu : Node
{
    private const double ClosesAfter = 12;

    private ScreenView _screen = null!;
    private (string Label, Action Do)[] _items = [];
    private int _hovered = -1;
    private double _untouched;
    private bool _devHeld;                                  // a dev run's mark stays, whatever the real pointer does

    public bool Open { get; private set; }

    /// <param name="items">What the menu offers, in order (BACK is added at the end).</param>
    public void Build(ScreenView screen, IEnumerable<(string Label, Action Do)> items)
    {
        _screen = screen;
        _items = [.. items, ("BACK", () => { })];
    }

    public override void _Process(double delta)
    {
        if (Open && (_untouched += delta) > ClosesAfter)
            Close();
    }

    /// <summary>A click on the cabinet: on the screen it opens the menu, or does the item clicked. False when not on the screen.</summary>
    public bool Clicked(Vector2 canvasPoint)
    {
        if (!_screen.Hits(canvasPoint) || _screen.Prompting)
            return false;
        if (!Open)
        {
            Open = true;
            _untouched = 0;
            _hovered = -1;
            Show();
            return true;
        }
        if (_screen.MenuItemAt(canvasPoint) is { } item)
        {
            Close();
            _items[item].Do();
        }
        return true;
    }

    /// <summary>The pointer moved over the cabinet (null: off it): the item under it is marked.</summary>
    public void Hover(Vector2? canvasPoint)
    {
        if (!Open || _devHeld)
            return;
        var item = canvasPoint is { } p ? _screen.MenuItemAt(p) ?? -1 : -1;
        if (item == _hovered)
            return;
        _hovered = item;
        if (item >= 0)
            _untouched = 0;
        Show();
    }

    public void Close()
    {
        if (!Open)
            return;
        Open = false;
        _devHeld = false;
        _screen.HideMenu();
    }

    private void Show() => _screen.ShowMenu("MENU", [.. _items.Select(i => i.Label)], _hovered);

    /// <summary>Dev: opens the menu and does an item by its label ("NEW MEMO"), as clicking it would.</summary>
    public void DevPick(string label)
    {
        if (Array.FindIndex(_items, i => i.Label == label) is var index and >= 0)
            _items[index].Do();
    }

    /// <summary>Dev: opens the menu with an item marked, to look at.</summary>
    public void DevShow(int hovered)
    {
        Open = true;
        _untouched = 0;
        _hovered = hovered;
        _devHeld = true;
        Show();
    }
}
