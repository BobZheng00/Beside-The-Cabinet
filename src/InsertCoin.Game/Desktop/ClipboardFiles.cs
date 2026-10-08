using System.Runtime.InteropServices;
using System.Text;

namespace InsertCoin.Game.Desktop;

/// <summary>Files copied in Explorer (Ctrl+C on them), as the clipboard holds them: their paths, or none.</summary>
public static class ClipboardFiles
{
    private const uint Drop = 15;                           // CF_HDROP

    public static string[] Get()
    {
        if (!IsClipboardFormatAvailable(Drop) || !OpenClipboard(IntPtr.Zero))
            return [];
        try
        {
            var drop = GetClipboardData(Drop);
            if (drop == IntPtr.Zero)
                return [];
            var files = new List<string>();
            var count = DragQueryFile(drop, uint.MaxValue, null, 0);
            for (uint i = 0; i < count; i++)
            {
                var name = new StringBuilder((int)DragQueryFile(drop, i, null, 0) + 1);
                if (DragQueryFile(drop, i, name, (uint)name.Capacity) > 0)
                    files.Add(name.ToString());
            }
            return [.. files];
        }
        finally
        {
            CloseClipboard();
        }
    }

    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder? file, uint size);
}
