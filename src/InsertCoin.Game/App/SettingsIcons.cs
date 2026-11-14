using Godot;

namespace InsertCoin.Game.App;

/// <summary>
/// The pixel icons of the settings' list, in the style of the screen's apps: a tinted tile with an ink edge and a picture
/// on it, drawn from these rows (# ink, o paper, g gold, . the tile).
/// </summary>
public static class SettingsIcons
{
    private static readonly Color Ink = new("6a1c5e"), Fill = new("fff7ec"), Gold = new("ffd27a");

    private static readonly Dictionary<string, (Color Tint, string[] Rows)> Icons = new()
    {
        [SettingsWindow.Missions] = (new("D4F2DF"),
        [
            "..#.........",
            "..#######...",
            "..#oooooo#..",
            "..#ooooo#...",
            "..#oooo#....",
            "..#ooooo#...",
            "..#oooooo#..",
            "..#######...",
            "..#.........",
            "..#.........",
            "..#.........",
            ".###........",
        ]),
        [SettingsWindow.General] = (new("F2D9F7"),
        [
            ".....##.....",
            ".##.####.##.",
            ".##########.",
            "..###..###..",
            ".###....###.",
            "###......###",
            "###......###",
            ".###....###.",
            "..###..###..",
            ".##########.",
            ".##.####.##.",
            ".....##.....",
        ]),
        [SettingsWindow.Companion] = (new("FFD4CC"),
        [
            "....####....",
            "...#oooo#...",
            "..#oooooo#..",
            "..#o#oo#o#..",
            "..#oooooo#..",
            "...#oooo#...",
            "....####....",
            "...######...",
            "..#oooooo#..",
            ".#oooooooo#.",
            ".#oooooooo#.",
            ".##########.",
        ]),
        [SettingsWindow.Memory] = (new("FFE9A6"),
        [
            "............",
            ".####..####.",
            "#oooo##oooo#",
            "#oooo##oooo#",
            "#o##o##o##o#",
            "#oooo##oooo#",
            "#o##o##o##o#",
            "#oooo##oooo#",
            "#oooo##oooo#",
            ".####..####.",
            ".....##.....",
            "............",
        ]),
        [SettingsWindow.Voice] = (new("E3D7FF"),
        [
            ".....##.....",
            "....#o#..#..",
            "...#oo#...#.",
            "####oo#.#.#.",
            "#ooooo#..#.#",
            "#ooooo#..#.#",
            "#ooooo#..#.#",
            "#ooooo#..#.#",
            "####oo#.#.#.",
            "...#oo#...#.",
            "....#o#..#..",
            ".....##.....",
        ]),
        [SettingsWindow.AiModel] = (new("CFE6FF"),
        [
            "...#.##.#...",
            "...#.##.#...",
            ".##########.",
            ".#oooooooo#.",
            "##oo####oo##",
            ".#oo#oo#oo#.",
            ".#oo#oo#oo#.",
            "##oo####oo##",
            ".#oooooooo#.",
            ".##########.",
            "...#.##.#...",
            "...#.##.#...",
        ]),
        [SettingsWindow.PlayTime] = (new("FFD4CC"),
        [
            "....####....",
            "..##gggg##..",
            ".#gggggggg#.",
            ".#ggg##ggg#.",
            "#gggg##gggg#",
            "#gggg##gggg#",
            "#gggg##gggg#",
            "#gggg##gggg#",
            ".#ggg##ggg#.",
            ".#gggggggg#.",
            "..##gggg##..",
            "....####....",
        ]),
        [SettingsWindow.Notifications] = (new("FFE9A6"),
        [
            ".....##.....",
            "....####....",
            "...#oooo#...",
            "..#oooooo#..",
            "..#oooooo#..",
            "..#oooooo#..",
            "..#oooooo#..",
            ".#oooooooo#.",
            "############",
            "............",
            "....####....",
            ".....##.....",
        ]),
        [SettingsWindow.Connections] = (new("D4F2DF"),
        [
            "...#....#...",
            "...#....#...",
            "...#....#...",
            ".##########.",
            ".#oooooooo#.",
            ".#oooooooo#.",
            "..#oooooo#..",
            "...##oo##...",
            ".....##.....",
            ".....##.....",
            ".....##.....",
            ".....##.....",
        ]),
        [SettingsWindow.Abilities] = (new("CFE6FF"),
        [
            ".##########.",
            ".#oooooooo#.",
            ".#oooooo#o#.",
            ".#ooooo##o#.",
            ".#o#oo##oo#.",
            ".#o##o#ooo#.",
            ".#oo###ooo#.",
            ".#ooo#oooo#.",
            "..#oooooo#..",
            "...#oooo#...",
            "....#oo#....",
            ".....##.....",
        ]),
    };

    /// <summary>A category's icon: a 16-pixel tile, each pixel drawn as two.</summary>
    public static Texture2D Of(string category)
    {
        const int tile = 16, inset = 2;                     // the picture sits inside the tile's edge
        var (tint, rows) = Icons[category];
        var image = Image.CreateEmpty(tile, tile, false, Image.Format.Rgba8);
        for (var y = 0; y < tile; y++)
        for (var x = 0; x < tile; x++)
        {
            var (edgeX, edgeY) = (Math.Min(x, tile - 1 - x), Math.Min(y, tile - 1 - y));
            var colour = edgeX + edgeY < 2 ? Colors.Transparent     // the corners, cut off
                       : edgeX == 0 || edgeY == 0 || edgeX + edgeY == 2 ? Ink
                       : tint;
            if (x >= inset && x < tile - inset && y >= inset && y < tile - inset)
                colour = rows[y - inset][x - inset] switch { '#' => Ink, 'o' => Fill, 'g' => Gold, _ => colour };
            image.SetPixel(x, y, colour);
        }
        image.Resize(tile * 2, tile * 2, Image.Interpolation.Nearest);
        return ImageTexture.CreateFromImage(image);
    }
}
