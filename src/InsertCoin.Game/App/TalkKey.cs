using System.Runtime.InteropServices;

namespace InsertCoin.Game.App;

/// <summary>
/// The key the player holds, in any app, to talk to her. Its state is read every frame (GetAsyncKeyState): no
/// keyboard hook, nothing installed system-wide, and the key still does whatever it does in the app in front. So
/// the default is a key that does nothing on its own: Right Ctrl. A key that types (` ~) types into whatever has
/// the caret, and repeats while held; it suits games, where nothing is typed.
/// </summary>
public static class TalkKey
{
    public const string Default = "RightCtrl", Off = "Off";

    /// <summary>The keys offered: setting name, label, Windows virtual-key code.</summary>
    public static readonly (string Name, string Label, int Code)[] Keys =
    [
        ("RightCtrl", "Right Ctrl", 0xA3),
        ("RightAlt", "Right Alt", 0xA5),
        ("Backquote", "` ~ (also types ` in the app in front, over and over while held)", 0xC0),
        ("MouseBack", "Mouse side button (back)", 0x05),
        ("MouseForward", "Mouse side button (forward)", 0x06),
        (Off, "None: only by holding on her", 0),
    ];

    /// <summary>Whether <paramref name="name"/> (one of <see cref="Keys"/>) is held down now.</summary>
    public static bool IsDown(string? name)
    {
        var code = Keys.FirstOrDefault(k => k.Name == (name ?? Default)).Code;
        return code != 0 && (GetAsyncKeyState(code) & 0x8000) != 0;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
