using System.Runtime.InteropServices;

namespace InsertCoin.Game.App;

/// <summary>Time since the last keyboard/mouse input anywhere on the desktop (for the AFK pause).</summary>
public static class UserActivity
{
    public static TimeSpan IdleFor()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time)) : TimeSpan.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}
