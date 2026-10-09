using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using InsertCoin.Game.Companion;

namespace InsertCoin.Game.Desktop;

/// <summary>
/// The titles of the player's open windows, the one in front first, each with the app it belongs to: what the taskbar
/// shows, not hidden or tool windows, and not the cabinet's own. A test run never reads the player's windows: it gets
/// stand-in titles.
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

    /// <summary>A window's title and app as listed, or null for one the taskbar wouldn't show.</summary>
    private static string? Shown(IntPtr window, int own)
    {
        if (!IsWindowVisible(window) || GetWindow(window, Owner) != IntPtr.Zero || (GetWindowLong(window, ExStyle) & ToolWindow) != 0)
            return null;
        if (DwmGetWindowAttribute(window, Cloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
            return null;                                    // another desktop's, or a suspended app's
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
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint relation);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int most);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
