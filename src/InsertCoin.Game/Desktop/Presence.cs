using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Godot;
using Microsoft.Win32;

namespace InsertCoin.Game.Desktop;

/// <summary>
/// Whether this is a moment she may speak up on her own, as Windows sees it: its own "should apps notify now?"
/// (no when the screen is locked, a full-screen app or game runs, presentation settings are on, or it is quiet time),
/// and whether another app has the microphone or camera, as in a call (Windows records each app's use of them, the
/// same record behind the taskbar's microphone icon). Her own microphone, while she listens, does not count.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Presence
{
    private const string Consent = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    /// <summary>Windows would hold notifications back now: away (locked, screen saver), full screen, presenting, quiet time.</summary>
    public static bool DoNotDisturb
    {
        get
        {
            // 1 not present, 2 busy (full screen), 3 Direct3D full screen, 4 presentation, 5 accepts notifications, 6 quiet time, 7 a full-screen store app
            if (SHQueryUserNotificationState(out var state) != 0)
                return false;
            return state is 1 or 2 or 3 or 4 or 6 or 7;
        }
    }

    /// <summary>The screen is locked, or its saver runs: nobody is at the PC (Windows' "not present").</summary>
    public static bool Locked => SHQueryUserNotificationState(out var state) == 0 && state == 1;

    /// <summary>
    /// Windows has turned the display off (its power setting, after the minutes it was told): nobody is watching. A test
    /// run never asks Windows: its display is on unless <see cref="DisplayStandIn"/> says otherwise.
    /// </summary>
    public static bool DisplayOff => DisplayStandIn ?? (!Companion.Paper.OffScreen && Display.State == 0);

    /// <summary>Dev: whether the display is off, whatever Windows says (the dev action <c>display:off</c>); null asks Windows.</summary>
    public static bool? DisplayStandIn { get; set; }

    /// <summary>
    /// The console display's state as Windows last told it: registered once, on first use, and kept up to date by
    /// Windows' callback, which tells the state at once on registering (PowerSettingRegisterNotification, needing no window).
    /// </summary>
    private static class Display
    {
        private static volatile int _state = 1;             // first, so Windows' first answer (perhaps during Register) isn't overwritten
        private static readonly Guid ConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");
        private const uint Callback = 2, SettingChange = 0x8013;
        private static readonly PowerCallback Told = (_, type, setting) =>
        {
            // POWERBROADCAST_SETTING: the setting's GUID (16 bytes), the data's length (4), then the data: a DWORD here
            if (type == SettingChange && setting != IntPtr.Zero && Marshal.ReadInt32(setting, 16) >= 4)
                _state = Marshal.ReadInt32(setting, 20);
            return 0;
        };
        private static readonly SubscribeParameters Subscribed = new() { Callback = Marshal.GetFunctionPointerForDelegate(Told) };
        private static readonly IntPtr Registration = Register();

        /// <summary>0 off, 1 on, 2 dimmed; on when Windows couldn't be asked.</summary>
        public static int State => Registration == IntPtr.Zero ? 1 : _state;

        private static IntPtr Register()
        {
            var guid = ConsoleDisplayState;
            var parameters = Subscribed;
            return PowerSettingRegisterNotification(ref guid, Callback, ref parameters, out var handle) == 0 ? handle : IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SubscribeParameters
        {
            public IntPtr Callback;
            public IntPtr Context;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PowerCallback(IntPtr context, uint type, IntPtr setting);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSettingRegisterNotification(ref Guid setting, uint flags, ref SubscribeParameters recipient, out IntPtr handle);
    }

    /// <summary>Another app is using the microphone or the camera right now: most likely a call or a meeting.</summary>
    public static bool InCall => !IgnoreCalls && (InUse("microphone") || InUse("webcam"));

    /// <summary>Dev: another app's microphone or camera doesn't count (a test run while the PC is in a call).</summary>
    public static bool IgnoreCalls { get; set; }

    private static bool InUse(string device)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(Consent + device);
            if (root is null)
                return false;
            HashSet<string>? running = null;                // the paths of running programs, read only when a record says "in use"
            bool Running(Func<string, bool> matches) => (running ??= RunningPaths()).Any(matches);
            foreach (var name in root.GetSubKeyNames().Where(n => n != "NonPackaged"))
            {
                // a store app, by its package family name (Name_PublisherId): it runs from WindowsApps\Name_…
                var folder = $@"\WindowsApps\{name.Split('_')[0]}_";
                using var app = root.OpenSubKey(name);
                if (Started(app) && Running(path => path.Contains(folder, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            using var plain = root.OpenSubKey("NonPackaged");
            if (plain is null)
                return false;
            // desktop apps are keyed by their path, with # for each backslash
            var self = OS.GetExecutablePath().Replace('/', '\\');
            foreach (var name in plain.GetSubKeyNames())
            {
                var path = name.Replace('#', '\\');
                using var app = plain.OpenSubKey(name);
                if (!path.Equals(self, StringComparison.OrdinalIgnoreCase) && Started(app) && Running(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            return false;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Started using it, not stopped, and lately. Windows never records the stop of a program that ends while using
    /// the device (a crash, an update, a reinstall), so a start with no stop is believed only from the last twelve
    /// hours, and only while that program is running.
    /// </summary>
    private static bool Started(RegistryKey? app) =>
        app?.GetValue("LastUsedTimeStart") is long start && start != 0 && app.GetValue("LastUsedTimeStop") is long stop && stop == 0
        && DateTime.FromFileTime(start) > DateTime.Now.AddHours(-12);

    private static HashSet<string> RunningPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in System.Diagnostics.Process.GetProcesses())
            using (process)
                try
                {
                    if (process.MainModule?.FileName is { } path)
                        paths.Add(path);
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // a system process we may not look into: not a call app
                }
        return paths;
    }
}
