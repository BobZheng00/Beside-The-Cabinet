using Godot;

namespace InsertCoin.Game.Cabinet;

/// <summary>
/// The marquee's lit pixel display (the user's choice, 2026-10-22): a board of dots that reads INSERT COIN at rest, and
/// the name of the CRT's button under the pointer while there is one, in big lit letters that read at a glance on the
/// smallest cabinet. A name too long for the board scrolls through it, from its start, again and again.
/// </summary>
public partial class MarqueeDisplay : Polygon2D
{
    public const string AtRest = "INSERT COIN";
    private const int Columns = 75, Rows = 15, Top = 4;         // twelve letters fit; the art's panel is in this proportion
    private const double Hold = 0.8, Step = 0.07;               // a long name is read from its start, then moves a column at a time
    private const int Gap = 12;                                 // the room between the end of a scrolling name and its start again

    private readonly Image _dots = Image.CreateEmpty(Columns, Rows, false, Image.Format.L8);
    private ImageTexture _texture = null!;
    private string _text = AtRest;
    private double _shown;
    private int _offset = -1;

    public void Build(IReadOnlyList<Vector2> quad)
    {
        Polygon = [.. quad];
        _texture = ImageTexture.CreateFromImage(_dots);
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Cabinet/marquee_dots.gdshader") };
        var (r0, r1, r2) = Homography.QuadToUnit(quad);
        material.SetShaderParameter("inv_h_row0", r0);
        material.SetShaderParameter("inv_h_row1", r1);
        material.SetShaderParameter("inv_h_row2", r2);
        material.SetShaderParameter("grid", new Vector2(Columns, Rows));
        material.SetShaderParameter("dots", _texture);
        material.SetShaderParameter("dots_soft", _texture);
        Material = material;
        Light(0);
    }

    /// <summary>What the board reads: a name (shown in capitals), or null for INSERT COIN.</summary>
    public void Show(string? text)
    {
        text = string.IsNullOrWhiteSpace(text) ? AtRest : text.Trim().ToUpperInvariant();
        if (text == _text)
            return;
        _text = text;
        _shown = 0;
        _offset = -1;
        GD.Print($"Marquee: {text}");
        Light(0);
    }

    public override void _Process(double delta)
    {
        if (Width(_text) <= Columns)
            return;
        _shown += delta;
        Light(_shown < Hold ? 0 : (int)((_shown - Hold) / Step) % (Width(_text) + Gap));
    }

    private static int Width(string text) => text.Length * 6 - 1;

    /// <summary>Lights the dots for the text, centred when it fits, else from <paramref name="offset"/> columns in.</summary>
    private void Light(int offset)
    {
        if (offset == _offset)
            return;
        _offset = offset;
        _dots.Fill(Colors.Black);
        var width = Width(_text);
        var scrolls = width > Columns;
        for (var column = 0; column < Columns; column++)
        {
            var at = scrolls ? (column + offset) % (width + Gap) : column - (Columns - width) / 2;
            if (at < 0 || at >= width || at % 6 == 5)
                continue;
            var glyph = Font.GetValueOrDefault(_text[at / 6], Blank);
            for (var row = 0; row < 7; row++)
                if (glyph[row * 5 + at % 6] == '#')
                    _dots.SetPixel(column, Top + row, Colors.White);
        }
        _texture.Update(_dots);
    }

    // ---- The letters: five dots wide and seven high, as on a dot-matrix sign ----------------------------------------
    private static readonly string Blank = new('.', 35);
    private static readonly Dictionary<char, string> Font = new Dictionary<char, string>
    {
        ['A'] = ".###. #...# #...# #...# ##### #...# #...#", ['B'] = "####. #...# #...# ####. #...# #...# ####.",
        ['C'] = ".###. #...# #.... #.... #.... #...# .###.", ['D'] = "###.. #..#. #...# #...# #...# #..#. ###..",
        ['E'] = "##### #.... #.... ####. #.... #.... #####", ['F'] = "##### #.... #.... ####. #.... #.... #....",
        ['G'] = ".###. #...# #.... #.### #...# #...# .####", ['H'] = "#...# #...# #...# ##### #...# #...# #...#",
        ['I'] = ".###. ..#.. ..#.. ..#.. ..#.. ..#.. .###.", ['J'] = "..### ...#. ...#. ...#. ...#. #..#. .##..",
        ['K'] = "#...# #..#. #.#.. ##... #.#.. #..#. #...#", ['L'] = "#.... #.... #.... #.... #.... #.... #####",
        ['M'] = "#...# ##.## #.#.# #.#.# #...# #...# #...#", ['N'] = "#...# #...# ##..# #.#.# #..## #...# #...#",
        ['O'] = ".###. #...# #...# #...# #...# #...# .###.", ['P'] = "####. #...# #...# ####. #.... #.... #....",
        ['Q'] = ".###. #...# #...# #...# #.#.# #..#. .##.#", ['R'] = "####. #...# #...# ####. #.#.. #..#. #...#",
        ['S'] = ".#### #.... #.... .###. ....# ....# ####.", ['T'] = "##### ..#.. ..#.. ..#.. ..#.. ..#.. ..#..",
        ['U'] = "#...# #...# #...# #...# #...# #...# .###.", ['V'] = "#...# #...# #...# #...# #...# .#.#. ..#..",
        ['W'] = "#...# #...# #...# #.#.# #.#.# #.#.# .#.#.", ['X'] = "#...# #...# .#.#. ..#.. .#.#. #...# #...#",
        ['Y'] = "#...# #...# #...# .#.#. ..#.. ..#.. ..#..", ['Z'] = "##### ....# ...#. ..#.. .#... #.... #####",
        ['0'] = ".###. #...# #..## #.#.# ##..# #...# .###.", ['1'] = "..#.. .##.. ..#.. ..#.. ..#.. ..#.. .###.",
        ['2'] = ".###. #...# ....# ...#. ..#.. .#... #####", ['3'] = "##### ...#. ..#.. ...#. ....# #...# .###.",
        ['4'] = "...#. ..##. .#.#. #..#. ##### ...#. ...#.", ['5'] = "##### #.... ####. ....# ....# #...# .###.",
        ['6'] = "..##. .#... #.... ####. #...# #...# .###.", ['7'] = "##### ....# ...#. ..#.. .#... .#... .#...",
        ['8'] = ".###. #...# #...# .###. #...# #...# .###.", ['9'] = ".###. #...# #...# .#### ....# ...#. .##..",
        ['-'] = "..... ..... ..... ##### ..... ..... .....", ['.'] = "..... ..... ..... ..... ..... .##.. .##..",
        ['\''] = ".##.. ..#.. .#... ..... ..... ..... .....", ['!'] = "..#.. ..#.. ..#.. ..#.. ..#.. ..... ..#..",
        ['?'] = ".###. #...# ....# ...#. ..#.. ..... ..#..", [':'] = "..... .##.. .##.. ..... .##.. .##.. .....",
        ['/'] = "..... ....# ...#. ..#.. .#... #.... .....", ['&'] = ".##.. #..#. #.#.. .#... #.#.# #..#. .##.#",
    }.ToDictionary(g => g.Key, g => g.Value.Replace(" ", ""));
}
