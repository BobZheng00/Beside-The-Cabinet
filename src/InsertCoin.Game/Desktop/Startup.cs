using System.Runtime.Versioning;
using Godot;
using Microsoft.Win32;

namespace InsertCoin.Game.Desktop;

/// <summary>"Start with Windows": a per-user Run entry, written only when the player ticks the menu item.</summary>
[SupportedOSPlatform("windows")]
public static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "InsertCoin";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, CommandLine());
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>The exported game runs as its own exe; from the editor binary it needs the project path.</summary>
    private static string CommandLine()
    {
        var exe = OS.GetExecutablePath();
        return OS.HasFeature("editor") || !OS.HasFeature("template")
            ? $"\"{exe}\" --path \"{ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\')}\""
            : $"\"{exe}\"";
    }
}
