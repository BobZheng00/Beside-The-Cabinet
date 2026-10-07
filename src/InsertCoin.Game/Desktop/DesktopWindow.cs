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
        public const nint WsExTransparent = 0x20, WsExLayered = 0x80000;
        public static readonly nint HwndTopmost = -1, HwndNoTopmost = -2;
        public const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10;
        public const int VkLButton = 0x01, VkRButton = 0x02;
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    }
}
