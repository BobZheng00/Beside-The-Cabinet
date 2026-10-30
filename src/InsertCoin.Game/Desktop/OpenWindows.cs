using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using InsertCoin.Game.Companion;

namespace InsertCoin.Game.Desktop;

/// <summary>
/// The titles of the player's open windows, the one in front first, each with the app it belongs to: what the taskbar
/// shows, not hidden or tool windows, and not the cabinet's own; and, for programs picked as play, the program in front
/// and those running, by their files alone. A test run never reads the player's windows: it gets stand-ins.
/// </summary>
public static class OpenWindows
{
    private const int MostShown = 15, ExStyle = -20, ToolWindow = 0x80, Cloaked = 14;
    private const uint Owner = 4;

    /// <summary>Titles given instead of the player's (test runs; the dev action <c>fakewindows</c>).</summary>
    public static string[]? StandIn { get; set; }

    private static readonly string[] TestTitles = ["1. Two Sum - LeetCode - Google Chrome (chrome)", "solution.py - Visual Studio Code (Code)"];
    private static readonly string[] NotWindows = ["Program Manager", "Windows Input Experience", "Microsoft Text Input Application", "Settings"];

    public static IReadOnlyList<string> Titles()
    {
        if (StandIn is not null)
            return StandIn;
        if (Paper.OffScreen)
            return TestTitles;
        var front = GetForegroundWindow();
        var own = Environment.ProcessId;
        var found = new List<(IntPtr Window, string Title)>();
        EnumWindowsProc each = (window, _) =>
        {
            if (Shown(window, own) is { } title)
                found.Add((window, title));
            return true;
        };
        EnumWindows(each, IntPtr.Zero);
        GC.KeepAlive(each);
        return [.. found.OrderBy(f => f.Window == front ? 0 : 1).Select(f => f.Title).Distinct().Take(MostShown)];
    }

    /// <summary>
    /// The window the player was working in: the frontmost the taskbar would show that isn't the cabinet's or minimised
    /// (typing to her, the slip is in front; the window under it is theirs). Null when there is none. A test run gets the
    /// first stand-in title, with no window.
    /// </summary>
    public static (IntPtr Window, string Title)? Working()
    {
        if (StandIn is not null || Paper.OffScreen)
            return (StandIn ?? TestTitles) is [var first, ..] ? (IntPtr.Zero, Bare(first)) : null;
        var own = Environment.ProcessId;
        (IntPtr, string)? found = null;
        EnumWindowsProc each = (window, _) =>
        {
            if (IsIconic(window) || Shown(window, own) is not { } title)
                return true;
            found = (window, Bare(title));
            return false;                                   // windows come frontmost first
        };
        EnumWindows(each, IntPtr.Zero);
        GC.KeepAlive(each);
        return found;
    }

    /// <summary>A title as listed, without the app it belongs to.</summary>
    private static string Bare(string listed) => listed.EndsWith(')') && listed.LastIndexOf(" (", StringComparison.Ordinal) is > 0 and var app ? listed[..app] : listed;

    /// <summary>Stand-in for the file of the program in front, and the only one running (test runs; the dev action <c>frontprogram</c>).</summary>
    public static string? FrontStandIn { get; set; }

    private static string? _lastFront;                      // the player's program in front before one of the cabinet's windows came forward

    /// <summary>
    /// The file of the program whose window is in front, for programs picked as play: only the program, never a window's
    /// title. While one of the cabinet's own windows is in front (typing to her), the one in front before it. A test run
    /// never reads the player's: it gets the stand-in, if there is one.
    /// </summary>
    public static string? FrontProgram()
    {
        if (Paper.OffScreen || FrontStandIn is not null)
            return FrontStandIn;
        GetWindowThreadProcessId(GetForegroundWindow(), out var process);
        if (process == Environment.ProcessId)
            return _lastFront;
        return _lastFront = process == 0 ? null : FileOf(process);
    }

    /// <summary>
    /// The files of the programs with a window on the taskbar, to pick from as play: found by process, never by a window's
    /// title, and not Windows' own (File Explorer, Settings) or the cabinet. A test run gets the stand-in, if there is one.
    /// </summary>
    public static IReadOnlyList<string> RunningPrograms()
    {
        if (Paper.OffScreen || FrontStandIn is not null)
            return FrontStandIn is { } standIn ? [standIn] : [];
        var processes = new HashSet<uint>();
        EnumWindowsProc each = (window, _) =>
        {
            if (OnTaskbar(window))
            {
                GetWindowThreadProcessId(window, out var process);
                processes.Add(process);
            }
            return true;
        };
        EnumWindows(each, IntPtr.Zero);
        GC.KeepAlive(each);
        processes.Remove((uint)Environment.ProcessId);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return [.. processes.Select(FileOf).OfType<string>().Where(p => !p.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
                            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>A program's name from its file's description ("VLC media player"), or else its file's name.</summary>
    public static string ProgramName(string path)
    {
        try
        {
            if (FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim() is { Length: > 0 } described)
                return described;
        }
        catch (FileNotFoundException)
        {
            // gone since it was picked: its file's name will do
        }
        return Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>The file a process runs from, or null when it can't be read (it closed, or Windows keeps it from us).</summary>
    private static string? FileOf(uint process)
    {
        var handle = OpenProcess(QueryLimitedInformation, false, process);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var path = new StringBuilder(1024);
            var size = path.Capacity;
            return QueryFullProcessImageName(handle, 0, path, ref size) ? path.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>A window the taskbar would show: visible, not owned by another, not a tool window, not on another desktop.</summary>
    private static bool OnTaskbar(IntPtr window) =>
        IsWindowVisible(window) && GetWindow(window, Owner) == IntPtr.Zero && (GetWindowLong(window, ExStyle) & ToolWindow) == 0
        && !(DwmGetWindowAttribute(window, Cloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0);   // cloaked: another desktop's, or a suspended app's

    /// <summary>A window's title and app as listed, or null for one the taskbar wouldn't show.</summary>
    private static string? Shown(IntPtr window, int own)
    {
        if (!OnTaskbar(window))
            return null;
        var length = GetWindowTextLength(window);
        if (length == 0)
            return null;
        var text = new StringBuilder(length + 1);
        GetWindowText(window, text, text.Capacity);
        var title = text.ToString().Trim();
        GetWindowThreadProcessId(window, out var process);
        if (title.Length == 0 || process == own || NotWindows.Contains(title))
            return null;
        try
        {
            using var app = Process.GetProcessById((int)process);
            return $"{title} ({app.ProcessName})";
        }
        catch (ArgumentException)
        {
            return title;                                   // it closed meanwhile
        }
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc each, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint relation);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int most);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    private const uint QueryLimitedInformation = 0x1000;
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);
}
