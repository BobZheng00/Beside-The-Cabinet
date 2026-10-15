using Godot;

namespace InsertCoin.Game.Desktop;

/// <summary>
/// Dev (<c>--offscreen --shots=DIR</c>): screenshots of the app's own windows, taken when a hidden test run asks. Such a
/// run keeps the app on a Windows desktop of its own, where nothing reaches the player's screen, taskbar or focus, and
/// where Windows can't capture a window: so the app saves each window's picture itself. A request is a file
/// <c>&lt;n&gt;.want</c> in the folder; the answer is <c>&lt;n&gt;-&lt;window&gt;.png</c> for every window showing and
/// <c>&lt;n&gt;.txt</c>, a line per window (its id, native handle, position, size, picture and title, tab-separated),
/// then the request is deleted.
/// </summary>
public partial class TestShots : Node
{
    public string Folder { get; init; } = "";

    public override void _Process(double delta)
    {
        foreach (var want in Directory.GetFiles(Folder, "*.want"))
        {
            var n = Path.GetFileNameWithoutExtension(want);
            var lines = new List<string>();
            foreach (var id in DisplayServer.GetWindowList())
            {
                if (InstanceFromId(DisplayServer.WindowGetAttachedInstanceId(id)) is not Window { Visible: true } window)
                    continue;
                var picture = Path.Combine(Folder, $"{n}-{id}.png");
                window.GetTexture().GetImage().SavePng(picture);
                var at = DisplayServer.WindowGetPosition(id);
                var size = DisplayServer.WindowGetSize(id);
                var handle = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, id);
                lines.Add(string.Join('\t', id, handle, at.X, at.Y, size.X, size.Y, picture, window.Title));
            }
            File.WriteAllLines(Path.Combine(Folder, n + ".txt"), lines);
            File.Delete(want);                      // last: the run waits for it to go
        }
    }
}
