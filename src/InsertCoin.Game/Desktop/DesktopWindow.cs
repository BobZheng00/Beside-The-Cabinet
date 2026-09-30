using System.Runtime.InteropServices;
using Godot;

namespace InsertCoin.Game.Desktop;

/// <summary>
/// Makes the transparent cabinet window behave like a desktop object: clicks pass through everywhere except the
/// art's opaque pixels, and it can be dragged by its body.
/// Window regions are not used: on Windows their edges draw faint lines over empty space.
/// </summary>
public partial class DesktopWindow : Node
{
    /// <summary>Raised on a left click that is not the start of a drag, with the canvas point.</summary>
    public event Action<Vector2>? Clicked;
    /// <summary>Canvas point under the cursor, or null when outside the art.</summary>
    public event Action<Vector2?>? Hover;

    private Bitmap _hitMask = null!;
    private float _maskScale;
    private Vector2 _canvas;
    private nint _hwnd;
    private bool? _clickThrough;
    private bool _pressed, _dragging;
    private Vector2I _pressAt, _dragOffset;

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

    private bool OverArt(Vector2 canvasPoint)
    {
        var px = canvasPoint * _maskScale;
        var size = _hitMask.GetSize();
        return px.X >= 0 && px.Y >= 0 && px.X < size.X && px.Y < size.Y && _hitMask.GetBit((int)px.X, (int)px.Y);
    }

    private void Poll()
    {
        var cursor = CursorCanvas();
        var over = OverArt(cursor);
        Hover?.Invoke(over ? cursor : null);
        SetClickThrough(!over && !_pressed);
    }

    public override void _Input(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                _pressed = true;
                _dragging = false;
                _pressAt = DisplayServer.MouseGetPosition();
                _dragOffset = _pressAt - DisplayServer.WindowGetPosition();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                if (_pressed && !_dragging)
                    Clicked?.Invoke(CursorCanvas());
                _pressed = _dragging = false;
                break;
            case InputEventMouseMotion when _pressed:
                var mouse = DisplayServer.MouseGetPosition();
                if (!_dragging && (mouse - _pressAt).LengthSquared() > 16)
                    _dragging = true;
                if (_dragging)
                    DisplayServer.WindowSetPosition(mouse - _dragOffset);
                break;
        }
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
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    }
}
