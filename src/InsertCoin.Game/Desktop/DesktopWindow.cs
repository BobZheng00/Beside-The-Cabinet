using System.Runtime.InteropServices;
using Godot;

namespace InsertCoin.Game.Desktop;

/// <summary>
/// Makes the transparent cabinet window behave like a desktop object: clicks pass through everywhere except the
/// art's opaque pixels, and it can be dragged by its body; what can be carried (a memo) is carried instead.
/// Window regions are not used: on Windows their edges draw faint lines over empty space.
/// </summary>
public partial class DesktopWindow : Node
{
    /// <summary>Raised on a left click that is not the start of a drag, with the canvas point.</summary>
    public event Action<Vector2>? Clicked;
    /// <summary>The left button went down on the window (not on a control that keeps the mouse), with the canvas point.</summary>
    public event Action<Vector2>? Pressed;
    /// <summary>The press became a drag of the window.</summary>
    public event Action? DragStarted;
    /// <summary>The left button came up again, after <see cref="Clicked"/> if it was a click.</summary>
    public event Action? Released;
    /// <summary>Canvas point under the cursor, or null when outside the art.</summary>
    public event Action<Vector2?>? Hover;
    /// <summary>Anything solid besides the art's opaque pixels (things drawn live), tested with a canvas point.</summary>
    public Func<Vector2, bool>? AlsoSolid { get; set; }
    /// <summary>Controls that take the mouse themselves (a text field): pressing there neither clicks nor drags the window.</summary>
    public Func<Vector2, bool>? KeepsMouse { get; set; }
    /// <summary>Things the mouse can carry (a memo), tested where the press was: pulled, they are carried instead of the window.</summary>
    public Func<Vector2, bool>? Grabs { get; set; }
    /// <summary>A pull began on something that can be carried, with the canvas point pressed; then where it is carried to; then where it is let go.</summary>
    public event Action<Vector2>? CarryStarted, Carried, Dropped;
    /// <summary>A mouse button went down somewhere else on the desktop, not on the cabinet nor on another of the app's windows over it (seen by polling, so a very quick click can be missed).</summary>
    public event Action? PressedElsewhere;
    /// <summary>Another of the app's windows is over this screen point (a memo's sheet): the cabinet underneath is not pointed at.</summary>
    public Func<Vector2I, bool>? Covered { get; set; }

    private Bitmap _hitMask = null!;
    private float _maskScale;
    private Vector2 _canvas;
    private nint _hwnd;
    private bool? _clickThrough;
    private bool _pressed, _dragging, _grabbed, _buttonDown;
    private Vector2I _pressAt, _dragOffset;
    private Vector2 _pressCanvas;

    public void Build(Texture2D hitArt, float artScale, Vector2 canvas)
    {
        _hitMask = new Bitmap();
        _hitMask.CreateFromImageAlpha(hitArt.GetImage(), 0.35f);
        _maskScale = artScale;
        _canvas = canvas;
        _hwnd = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        var poll = new Godot.Timer { WaitTime = 1.0 / 30, Autostart = true };
        poll.Timeout += Poll;
        AddChild(poll);
        if (Companion.Paper.OffScreen)
            GD.Print("DesktopWindow: a test run: the player's mouse buttons are never read, nor their pointer followed");
    }

    /// <summary>Window pixels per canvas unit.</summary>
    public float WindowScale => DisplayServer.WindowGetSize().X / _canvas.X;

    private Vector2 CursorCanvas() => (Vector2)(DisplayServer.MouseGetPosition() - DisplayServer.WindowGetPosition()) / WindowScale;

    /// <summary>The canvas point is on the cabinet (its art, or anything solid drawn live).</summary>
    public bool Solid(Vector2 canvasPoint) => OverArt(canvasPoint);

    private bool OverArt(Vector2 canvasPoint)
    {
        if (AlsoSolid?.Invoke(canvasPoint) == true)
            return true;
        var px = canvasPoint * _maskScale;
        var size = _hitMask.GetSize();
        return px.X >= 0 && px.Y >= 0 && px.X < size.X && px.Y < size.Y && _hitMask.GetBit((int)px.X, (int)px.Y);
    }

    private void Poll()
    {
        var cursor = CursorCanvas();
        var over = Covered?.Invoke(DisplayServer.MouseGetPosition()) != true && OverArt(cursor);
        Hover?.Invoke(over ? cursor : null);
        SetClickThrough(!over && !_pressed);
        // a test run never watches the player's own mouse
        var down = !Companion.Paper.OffScreen && ((Win32.GetAsyncKeyState(Win32.VkLButton) | Win32.GetAsyncKeyState(Win32.VkRButton)) & 0x8000) != 0;
        if (down && !_buttonDown && !over && !_pressed && Covered?.Invoke(DisplayServer.MouseGetPosition()) != true)
            PressedElsewhere?.Invoke();                     // a memo's sheet is the app's own: reading one is not pressing away
        _buttonDown = down;
        KeepWindowsAbove();
    }

    public override void _Input(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } when KeepsMouse?.Invoke(CursorCanvas()) != true:
                _pressed = true;
                _dragging = false;
                _pressAt = DisplayServer.MouseGetPosition();
                _dragOffset = _pressAt - DisplayServer.WindowGetPosition();
                _pressCanvas = CursorCanvas();
                _grabbed = Grabs?.Invoke(_pressCanvas) == true;
                Pressed?.Invoke(_pressCanvas);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                var pressed = _pressed;
                if (_pressed && !_dragging)
                    Clicked?.Invoke(CursorCanvas());
                else if (_pressed && _grabbed)
                    Dropped?.Invoke(CursorCanvas());
                _pressed = _dragging = _grabbed = false;
                if (pressed)
                    Released?.Invoke();
                break;
            case InputEventMouseMotion when _pressed:
                var mouse = DisplayServer.MouseGetPosition();
                if (!_dragging && (mouse - _pressAt).LengthSquared() > 16)
                {
                    _dragging = true;
                    if (_grabbed)
                        CarryStarted?.Invoke(_pressCanvas);
                    else
                        DragStarted?.Invoke();
                }
                if (_dragging && _grabbed)
                    Carried?.Invoke(CursorCanvas());
                else if (_dragging)
                    DisplayServer.WindowSetPosition(mouse - _dragOffset);
                break;
        }
    }

    /// <summary>Keeps the window above others (or not) without activating it, so it never steals keyboard focus.</summary>
    public void SetTopmost(bool topmost) =>
        Win32.SetWindowPos(_hwnd, topmost ? Win32.HwndTopmost : Win32.HwndNoTopmost, 0, 0, 0, 0, Win32.SwpNoMove | Win32.SwpNoSize | Win32.SwpNoActivate);

    /// <summary>Raises the window above everything for a few seconds, then lets it sink back.</summary>
    public void Rise(double seconds, bool stayOnTop)
    {
        SetTopmost(true);
        if (!stayOnTop)
            GetTree().CreateTimer(seconds).Timeout += () => SetTopmost(false);
    }

    /// <summary>
    /// Keeps the app's own windows the player opened (Settings, the memory book, Chat history, a dialog, a memo's sheet)
    /// stacked just above the cabinet, with Windows' file dialogs the app opened above them, and above other apps exactly
    /// when the cabinet is: Godot's windows aren't owned by the cabinet's in Windows' terms, so the cabinet rising, or
    /// kept on top, would otherwise cover them. Tickets stay above them all. A window is only ever moved to just above
    /// the next one down, so nothing else already above the cabinet (the player's game) is covered, and none is
    /// activated. Popups (a list's choices, a hover tip) keep Godot's own order.
    /// </summary>
    private void KeepWindowsAbove()
    {
        HashSet<nint> kept = [], tickets = [];
        foreach (var id in DisplayServer.GetWindowList())
            if (id != DisplayServer.MainWindowId && !DisplayServer.WindowGetFlag(DisplayServer.WindowFlags.Popup, id))
                (DisplayServer.WindowGetFlag(DisplayServer.WindowFlags.AlwaysOnTop, id) ? tickets : kept)
                    .Add((nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, id));
        List<nint> onTop = [], dialogs = [], windows = [];
        for (var w = Win32.GetTopWindow(0); w != 0; w = Win32.GetWindow(w, Win32.GwHwndNext))
            if (kept.Contains(w) && Win32.IsWindowVisible(w))
                windows.Add(w);
            else if (tickets.Contains(w) && Win32.IsWindowVisible(w))
                onTop.Add(w);
            else if (FileDialog(w))
                dialogs.Add(w);
        var top = Topmost(_hwnd);
        // top first, each kind in the order it is in now, except that one Godot has just shown (not yet on top with the
        // cabinet) is in front of the others
        List<nint> wanted = [.. onTop, .. dialogs, .. windows.OrderBy(w => top && !Topmost(w) ? 0 : 1), _hwnd];
        for (var i = wanted.Count - 2; i >= 0; i--)
        {
            var kind = top || tickets.Contains(wanted[i]);                  // a ticket is always kept on top
            if (Topmost(wanted[i]) != kind || !Above(wanted[i], wanted[i + 1]))
                PlaceAbove(wanted[i], wanted[i + 1], kind);
        }
    }

    /// <summary>Puts the window just above another, among the windows kept on top or not, as the cabinet is.</summary>
    private static void PlaceAbove(nint window, nint below, bool top)
    {
        const uint flags = Win32.SwpNoMove | Win32.SwpNoSize | Win32.SwpNoActivate;
        if (!top && Topmost(window))
            Win32.SetWindowPos(window, Win32.HwndNoTopmost, 0, 0, 0, 0, flags);
        var over = Win32.GetWindow(below, Win32.GwHwndPrev);
        if (over == window)
            return;
        var after = over == 0 ? (top ? Win32.HwndTopmost : Win32.HwndTop)
            : !top && Topmost(over) ? Win32.HwndTop                             // the one below heads the windows not kept on top
            : over;                                                             // just under the window above it
        Win32.SetWindowPos(window, after, 0, 0, 0, 0, flags);
    }

    private static bool Above(nint window, nint below)
    {
        for (var w = Win32.GetWindow(below, Win32.GwHwndPrev); w != 0; w = Win32.GetWindow(w, Win32.GwHwndPrev))
            if (w == window)
                return true;
        return false;
    }

    /// <summary>One of Windows' own dialogs (a file dialog) the app opened and is showing.</summary>
    private static bool FileDialog(nint window)
    {
        if (!Win32.IsWindowVisible(window) || Win32.GetWindowThreadProcessId(window, out var process) == 0 || process != System.Environment.ProcessId)
            return false;
        var kind = new System.Text.StringBuilder(16);
        Win32.GetClassName(window, kind, kind.Capacity);
        return kind.ToString() == "#32770";
    }

    /// <summary>The app's windows, top first, as they are stacked on screen; "(on top)" marks those kept above other apps.</summary>
    public string DevOrder()
    {
        var own = DisplayServer.GetWindowList().Select(id => (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, id)).ToHashSet();
        var names = new List<string>();
        var title = new System.Text.StringBuilder(256);
        for (var w = Win32.GetTopWindow(0); w != 0; w = Win32.GetWindow(w, Win32.GwHwndNext))
            if (own.Contains(w) && Win32.IsWindowVisible(w) || FileDialog(w))
            {
                Win32.GetWindowText(w, title, title.Capacity);
                names.Add((w == _hwnd ? "the cabinet" : own.Contains(w) ? title.ToString() : $"file dialog \"{title}\"") + (Topmost(w) ? " (on top)" : ""));
            }
        return string.Join(", ", names);
    }

    private static bool Topmost(nint window) => (Win32.GetWindowLongPtr(window, Win32.GwlExStyle) & Win32.WsExTopmost) != 0;

    private void SetClickThrough(bool clickThrough)
    {
        if (clickThrough == _clickThrough)
            return;
        _clickThrough = clickThrough;
        var style = Win32.GetWindowLongPtr(_hwnd, Win32.GwlExStyle) | Win32.WsExLayered;
        style = clickThrough ? style | Win32.WsExTransparent : style & ~Win32.WsExTransparent;
        Win32.SetWindowLongPtr(_hwnd, Win32.GwlExStyle, style);
    }

    private static class Win32
    {
        public const int GwlExStyle = -20;
        public const nint WsExTransparent = 0x20, WsExLayered = 0x80000, WsExTopmost = 0x8;
        public static readonly nint HwndTop = 0, HwndTopmost = -1, HwndNoTopmost = -2;
        public const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10;
        public const uint GwHwndNext = 2, GwHwndPrev = 3;
        public const int VkLButton = 0x01, VkRButton = 0x02;
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint window);
        [DllImport("user32.dll")] public static extern nint GetWindow(nint window, uint relation);
        [DllImport("user32.dll")] public static extern nint GetTopWindow(nint parent);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint window, out int process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")] public static extern int GetClassName(nint window, System.Text.StringBuilder text, int most);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")] public static extern int GetWindowText(nint window, System.Text.StringBuilder text, int most);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    }
}
