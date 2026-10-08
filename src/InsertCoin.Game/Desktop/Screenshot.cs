using System.Runtime.InteropServices;
using Godot;
using InsertCoin.Game.Companion;

namespace InsertCoin.Game.Desktop;

/// <summary>
/// A screenshot of the monitor the mouse pointer is on, as the screen shows it (the cabinet too), through Windows' own
/// drawing, as a Godot image. A test run (off screen) never takes the player's screen: only a stand-in picture, if one
/// was given.
/// </summary>
public static class Screenshot
{
    private const int SrcCopy = 0x00CC0020, CaptureBlt = 0x40000000;
    private const uint DefaultToNearest = 2;

    /// <summary>A picture taken instead of the screen (test runs; the dev action <c>fakescreen</c>).</summary>
    public static string? StandIn { get; set; }

    public static Image? Take()
    {
        if (StandIn is not null)
            return Image.LoadFromFile(StandIn);
        if (Paper.OffScreen)
            return null;
        GetCursorPos(out var pointer);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromPoint(pointer, DefaultToNearest), ref info))
            return null;
        var (left, top) = (info.Monitor.Left, info.Monitor.Top);
        var (width, height) = (info.Monitor.Right - left, info.Monitor.Bottom - top);
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        try
        {
            var old = SelectObject(memory, bitmap);
            var copied = BitBlt(memory, 0, 0, width, height, screen, left, top, SrcCopy | CaptureBlt);
            SelectObject(memory, old);                      // read only once it is out of the drawing context
            if (!copied)
                return null;
            var header = new BitmapInfoHeader { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            var pixels = new byte[width * height * 4];
            if (GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref header, 0) == 0)
                return null;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);   // Windows keeps blue first
                pixels[i + 3] = 255;
            }
            return Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr thing);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr to, int x, int y, int width, int height, IntPtr from, int fromX, int fromY, int operation);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfoHeader info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr thing);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
