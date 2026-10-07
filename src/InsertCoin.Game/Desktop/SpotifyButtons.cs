using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InsertCoin.Game.Desktop;

/// <summary>
/// Spotify's own buttons, pressed through Windows' UI Automation (the accessibility interface screen readers use): no
/// keystrokes, no mouse, no focus taken. A playlist's page in Spotify has a button named "Play" and the playlist's
/// name ("Pause" and its name while it plays), which starts it from its beginning: the one thing Spotify's links and
/// Windows' media controls can't do. Spotify names its buttons in its own language, so this works with Spotify in
/// English (the user's, checked 2026-10-06). UI Automation is reached through its COM interface directly, the few calls
/// used declared below (Godot's .NET doesn't load the desktop runtime that wraps it). Every call blocks while Spotify
/// answers: run it off the main thread.
/// </summary>
public static class SpotifyButtons
{
    /// <summary>What came of looking for a playlist's play button.</summary>
    public enum Pressing { Pressed, AlreadyPlaying, Missing }

    private const int Descendants = 4, ControlTypeProperty = 30003, ButtonControlType = 50000, InvokePattern = 10000;

    /// <summary>Presses "Play <paramref name="playlist"/>" on Spotify's page for it, if the page shows it.</summary>
    public static Pressing Play(string playlist)
    {
        var wanted = Plain(playlist);
        foreach (var (name, button) in Buttons())
        {
            if (name == "pause " + wanted)
                return Pressing.AlreadyPlaying;
            if (name == "play " + wanted && button.GetCurrentPattern(InvokePattern) is IUIAutomationInvokePattern invoke)
            {
                invoke.Invoke();
                return Pressing.Pressed;
            }
        }
        return Pressing.Missing;
    }

    /// <summary>Puts Spotify's window down again (after it was started for the player, to keep it out of their way).</summary>
    public static void Minimise()
    {
        if (Handle() is { } handle)
            ShowWindow(handle, 6);                          // SW_MINIMIZE: the window under it becomes the active one again
    }

    /// <summary>Spotify is running with a window.</summary>
    public static bool Running => Handle() is not null;

    /// <summary>Dev: how many play buttons Spotify's window shows, and whether one for <paramref name="playlist"/> is among them (presses nothing).</summary>
    public static string Probe(string? playlist)
    {
        if (Handle() is null)
            return "Spotify has no window";
        var names = Buttons().Select(b => b.Name).ToList();
        var plays = names.Count(n => n.StartsWith("play ") || n.StartsWith("pause "));
        var mine = playlist is null ? "" : names.Contains("play " + Plain(playlist)) ? $", \"Play {playlist}\" is there"
            : names.Contains("pause " + Plain(playlist)) ? $", \"{playlist}\" is playing" : $", no button for \"{playlist}\"";
        return $"{names.Count} buttons, {plays} to play or pause{mine}";
    }

    /// <summary>Every button in Spotify's window, with its name as compared.</summary>
    private static IEnumerable<(string Name, IUIAutomationElement Button)> Buttons()
    {
        if (Handle() is not { } handle)
            yield break;
        var automation = (IUIAutomation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("ff48dba4-60ef-4201-aa87-54103eef594e"))!)!;
        var window = automation.ElementFromHandle(handle);
        var buttons = window.FindAll(Descendants, automation.CreatePropertyCondition(ControlTypeProperty, ButtonControlType));
        for (var i = 0; i < buttons.Length(); i++)
        {
            var button = buttons.GetElement(i);
            yield return (Plain(button.CurrentName() ?? ""), button);
        }
    }

    private static nint? Handle() =>
        Process.GetProcessesByName("Spotify").Select(p => p.MainWindowHandle).FirstOrDefault(h => h != 0) is var handle and not 0 ? handle : null;

    /// <summary>A name as compared: spaces run together, any case.</summary>
    private static string Plain(string name) => string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint handle, int show);

    // ---- UI Automation's COM interface: only what is used; the slots before each are kept in order, unused ------------

    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        void CompareElements();
        void CompareRuntimeIds();
        void GetRootElement();
        IUIAutomationElement ElementFromHandle(nint window);
        void ElementFromPoint();
        void GetFocusedElement();
        void GetRootElementBuildCache();
        void ElementFromHandleBuildCache();
        void ElementFromPointBuildCache();
        void GetFocusedElementBuildCache();
        void CreateTreeWalker();
        void ControlViewWalker();
        void ContentViewWalker();
        void RawViewWalker();
        void RawViewCondition();
        void ControlViewCondition();
        void ContentViewCondition();
        void CreateCacheRequest();
        void CreateTrueCondition();
        void CreateFalseCondition();
        IUIAutomationCondition CreatePropertyCondition(int property, [MarshalAs(UnmanagedType.Struct)] object value);
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void SetFocus();
        void GetRuntimeId();
        void FindFirst();
        IUIAutomationElementArray FindAll(int scope, IUIAutomationCondition condition);
        void FindFirstBuildCache();
        void FindAllBuildCache();
        void BuildUpdatedCache();
        void GetCurrentPropertyValue();
        void GetCurrentPropertyValueEx();
        void GetCachedPropertyValue();
        void GetCachedPropertyValueEx();
        void GetCurrentPatternAs();
        void GetCachedPatternAs();
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object? GetCurrentPattern(int pattern);
        void GetCachedPattern();
        void GetCachedParent();
        void GetCachedChildren();
        void CurrentProcessId();
        void CurrentControlType();
        void CurrentLocalizedControlType();
        [return: MarshalAs(UnmanagedType.BStr)]
        string? CurrentName();
    }

    [ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElementArray
    {
        int Length();
        IUIAutomationElement GetElement(int index);
    }

    [ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationCondition;

    [ComImport, Guid("fb377fbe-8ea6-46d5-9c73-6499642d3059"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationInvokePattern
    {
        void Invoke();
    }
}
