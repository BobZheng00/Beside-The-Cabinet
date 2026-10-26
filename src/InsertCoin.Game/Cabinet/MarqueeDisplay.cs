using Godot;

namespace InsertCoin.Game.Cabinet;

/// <summary>
/// The marquee's lit pixel display (the user's choice, 2026-10-22): a board of pink LED dots that reads INSERT COIN at
/// rest, and the name of the CRT's button under the pointer while there is one, in big letters that read at a glance on
/// the smallest cabinet. It behaves as a real dot-matrix sign does (the user, 2026-10-25: "too smooth and the font are
/// not pixel enough. lack the retro feeling"): the letters are a sign's 5×7 font, one dot to a font pixel, narrow
/// letters narrower; every dot is on or off, and changes go in steps: the board blanks, and the new text is drawn in a
/// whole column at a time at a sign's rate. A name only shows once the pointer has stayed on its button a moment, so
/// passing across the buttons doesn't flicker through every name, and a change waits for the one under way to finish. A
/// name too long for the board scrolls through it a whole column at a time, from its start.
/// </summary>
public partial class MarqueeDisplay : Polygon2D
{
    public const string AtRest = "INSERT COIN";
    private const int Columns = 64, Rows = 15, Top = 4;         // INSERT COIN and MUTE SOUNDS fit; square dots in the art's panel
    private const int Gap = 12;                                 // the room between the end of a scrolling name and its start again

    // ---- Timings, in seconds ----
    private const double Blank = 0.2;            // a change: the board goes dark this long
    private const double Rate = 30;              // then the new text is drawn in, this many columns a second (a sign's 15 to 30)
    private const double Settle = 0.18;          // the pointer stays on a button this long before its name shows
    private const double BackToRest = 0.6;       // and off them this long before INSERT COIN comes back (crossing the gaps between buttons)
    private const double Hold = 1.0, Step = 0.06;               // a long name is read from its start, then moves a column at a time

    private readonly Image _dots = Image.CreateEmpty(Columns, Rows, false, Image.Format.L8);
    private readonly byte[] _bytes = new byte[Columns * Rows];
    private ImageTexture _texture = null!;
    private string _text = AtRest, _wanted = AtRest;
    private byte[] _strip = Strip(AtRest);                      // the text's columns, left to right: a bit for each of the glyph's seven rows
    private double _wantedFor, _shown, _changing = -1;          // how long the change to the text has been going, or -1 once done

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
        Update(0);                                              // lit already as the cabinet appears
    }

    /// <summary>What the board should read: a name (shown in capitals), or null for INSERT COIN.</summary>
    public void Show(string? text)
    {
        text = string.IsNullOrWhiteSpace(text) ? AtRest : text.Trim().ToUpperInvariant();
        if (text == _wanted)
            return;
        _wanted = text;
        _wantedFor = 0;
    }

    public override void _Process(double delta) => Update(delta);

    private void Update(double delta)
    {
        _wantedFor += delta;
        if (_wanted != _text && _changing < 0 && _wantedFor >= (_wanted == AtRest ? BackToRest : Settle))
        {
            _text = _wanted;
            _strip = Strip(_text);
            _changing = 0;
            _shown = 0;
            GD.Print($"Marquee: {_text}");
        }
        var start = _strip.Length < Columns ? (Columns - _strip.Length) / 2 : 0;        // centred when it fits
        if (_changing >= 0 && (_changing += delta) >= Blank + Math.Min(_strip.Length, Columns) / Rate)
            _changing = -1;
        else if (_changing < 0)
            _shown += delta;

        // Columns of the new text drawn in so far, whole ones only; none while the board is dark.
        var drawn = _changing < 0 ? Columns : _changing < Blank ? 0 : start + (int)((_changing - Blank) * Rate);
        var offset = _strip.Length <= Columns || _shown < Hold ? 0 : (int)((_shown - Hold) / Step) % (_strip.Length + Gap);
        var changed = false;
        for (var i = 0; i < _bytes.Length; i++)
        {
            var (column, row) = (i % Columns, i / Columns);
            var at = _strip.Length <= Columns ? column - start : (column + offset) % (_strip.Length + Gap);
            var on = column < drawn && row >= Top && row < Top + 7 && at >= 0 && at < _strip.Length && (_strip[at] >> (row - Top) & 1) == 1;
            var value = (byte)(on ? 255 : 0);
            changed |= value != _bytes[i];
            _bytes[i] = value;
        }
        if (!changed && delta > 0)
            return;
        _dots.SetData(Columns, Rows, false, Image.Format.L8, _bytes);
        _texture.Update(_dots);
    }

    /// <summary>
    /// The text as a strip of the board's columns, as a sign sets it: each letter as wide as its dots (I and 1 take three
    /// columns, M five), a dark column between letters, three for a space. A column's bits are its dots, top row first.
    /// </summary>
    private static byte[] Strip(string text)
    {
        var columns = new List<byte>();
        foreach (var letter in text)
        {
            if (columns.Count > 0)
                columns.Add(0);
            var glyph = Font.GetValueOrDefault(letter);
            var bits = Enumerable.Range(0, 5).Select(c => (byte)Enumerable.Range(0, 7).Sum(r => glyph?[r * 5 + c] == '#' ? 1 << r : 0)).ToArray();
            var lit = Enumerable.Range(0, 5).Where(c => bits[c] != 0).ToArray();
            columns.AddRange(lit.Length == 0 ? new byte[3] : bits[lit[0]..(lit[^1] + 1)]);
        }
        return [.. columns];
    }

    // ---- The letters: five dots wide and seven high, as on a dot-matrix sign ----------------------------------------
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
