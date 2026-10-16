using System.Runtime.InteropServices;
using Godot;

namespace InsertCoin.Game.Desktop;

/// <summary>
/// A Windows notification from the cabinet's own tray icon (a balloon, which Windows 10 and 11 show as a toast and
/// keep in the notification centre). Godot's tray icon (StatusIndicator) has no API for it, but Godot registers each
/// icon on the main window with its indicator number as the icon's id, and the cabinet has one, so the balloon is
/// asked of that icon directly. No registration with Windows, nothing installed.
/// </summary>
public static class WindowsNotice
{
    private const int NimModify = 1, NifInfo = 0x10, NiifUser = 0x4, NiifLargeIcon = 0x20;
    private const uint TrayIconId = 0;                     // the first (and only) StatusIndicator: see TrayIcon

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public IntPtr Window;
        public uint Id;
        public int Flags;
        public int CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public int State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public int TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public int InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW", SetLastError = true)]
    private static extern bool ShellNotifyIcon(int message, ref NotifyIconData data);

    /// <summary>Shows it; false when Windows would not (the tray icon is not there, say).</summary>
    public static bool Show(string title, string text)
    {
        if (OS.GetName() != "Windows")
            return false;
        if (Companion.Paper.OffScreen)
        {
            GD.Print($"WindowsNotice: none in a test run (it would have said \"{title}\")");
            return true;                                    // as if shown, so the run goes on as the player's would
        }
        var data = new NotifyIconData
        {
            Size = Marshal.SizeOf<NotifyIconData>(),
            Window = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, 0),
            Id = TrayIconId,
            Flags = NifInfo,
            Tip = "",
            Info = text.Length > 255 ? text[..254] + "…" : text,
            InfoTitle = title.Length > 63 ? title[..62] + "…" : title,
            InfoFlags = NiifUser | NiifLargeIcon,               // the tray icon's own coin as the picture
        };
        var shown = ShellNotifyIcon(NimModify, ref data);
        if (!shown)
            GD.PrintErr($"WindowsNotice: Windows refused the notification (error {Marshal.GetLastPInvokeError()})");
        return shown;
    }
}
