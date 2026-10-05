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

    /// <summary>
    /// Whether any key other than <paramref name="name"/> is held now: the talk key pressed with another (Right Ctrl + C)
    /// is a shortcut, not a tap. Shift, Ctrl and Alt in general are left out: the talk key itself counts as one of them.
    /// </summary>
    public static bool OthersDown(string? name)
    {
        var own = Keys.FirstOrDefault(k => k.Name == (name ?? Default)).Code;
        for (var code = 0x08; code <= 0xFE; code++)
            if (code != own && code is not (0x10 or 0x11 or 0x12) && (GetAsyncKeyState(code) & 0x8000) != 0)
                return true;
        return false;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
