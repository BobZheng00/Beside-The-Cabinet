using Godot;

namespace InsertCoin.Game.Cabinet;

/// <summary>
/// The marquee's lit pixel display (the user's choice, 2026-10-22): a board of pink LED dots that reads INSERT COIN at
/// rest, and the name of the CRT's button under the pointer while there is one, in big letters that read at a glance on
/// the smallest cabinet. It behaves as a dot-matrix sign does (the user, trying it: "the text appear and switch too
/// fast, which does not really looks like pixel display"): each dot glows up over a moment and fades more slowly, unlit
/// dots stay faintly there, and a change goes as a sign's does: the old text fades out, then the new one is drawn in a
/// column at a time from the left. A name only shows once the pointer has stayed on its button a moment, so passing
/// across the buttons doesn't flicker through every name, and a change waits for the one under way to finish. A name
/// too long for the board scrolls through it, from its start.
/// </summary>
public partial class MarqueeDisplay : Polygon2D
{
    public const string AtRest = "INSERT COIN";
    private const int Columns = 75, Rows = 15, Top = 4;         // twelve letters fit; the art's panel is in this proportion
    private const int Gap = 12;                                 // the room between the end of a scrolling name and its start again

    // ---- Timings, in seconds ----
    private const double GlowUp = 0.06;          // a dot coming on, from dark to full
    private const double FadeOut = 0.22;         // a dot going out, from full to dark (an LED's afterglow; longer smears a scroll)
    private const double Clear = FadeOut;        // a change: the board goes dark while the old text fades out
    private const double WipeColumn = 0.009;     // then the new one is drawn in, this long a column (the board's 75: 0.7 s)
    private const double Settle = 0.18;          // the pointer stays on a button this long before its name shows
    private const double BackToRest = 0.6;       // and off them this long before INSERT COIN comes back (crossing the gaps between buttons)
    private const double Hold = 1.0, Step = 0.07;               // a long name is read from its start, then moves a column at a time

    private readonly Image _dots = Image.CreateEmpty(Columns, Rows, false, Image.Format.L8);
    private readonly byte[] _bytes = new byte[Columns * Rows];
    private readonly float[] _glow = new float[Columns * Rows];
    private ImageTexture _texture = null!;
    private string _text = AtRest, _wanted = AtRest;
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
        for (var i = 0; i < _glow.Length; i++)
            _glow[i] = Lit(_text, 0, i % Columns, i / Columns) ? 1 : 0;     // lit already as the cabinet appears
        Update(0);
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
            _changing = 0;
            _shown = 0;
            GD.Print($"Marquee: {_text}");
        }
        if (_changing >= 0 && (_changing += delta) >= Clear + Columns * WipeColumn)
            _changing = -1;
        else if (_changing < 0)
            _shown += delta;

        var offset = Scrolled(_text);
        var drawn = _changing < 0 ? Columns : (_changing - Clear) / WipeColumn;      // columns of the new text drawn in so far
        var changed = false;
        for (var i = 0; i < _glow.Length; i++)
        {
            var (column, row) = (i % Columns, i / Columns);
            var on = column < drawn && Lit(_text, offset, column, row);
            var glow = on ? Math.Min(1, _glow[i] + (float)(delta / GlowUp)) : Math.Max(0, _glow[i] - (float)(delta / FadeOut));
            if (glow == _glow[i] && delta > 0)
                continue;
            _glow[i] = glow;
            _bytes[i] = (byte)(glow * glow * 255);          // the eye sees a dot's light fall off fast at first
            changed = true;
        }
        if (!changed)
            return;
        _dots.SetData(Columns, Rows, false, Image.Format.L8, _bytes);
        _texture.Update(_dots);
    }

    /// <summary>How many columns a long text has scrolled by now (0 while it is drawn in and read from its start).</summary>
    private int Scrolled(string text) => Width(text) <= Columns || _shown < Hold ? 0 : (int)((_shown - Hold) / Step) % (Width(text) + Gap);

    private static int Width(string text) => text.Length * 6 - 1;

    /// <summary>Whether a dot is lit for the text: centred when it fits, else from <paramref name="offset"/> columns in.</summary>
    private static bool Lit(string text, int offset, int column, int row)
    {
        var width = Width(text);
        var at = width > Columns ? (column + offset) % (width + Gap) : column - (Columns - width) / 2;
        if (row < Top || row >= Top + 7 || at < 0 || at >= width || at % 6 == 5)
            return false;
        return Font.GetValueOrDefault(text[at / 6], Blank)[(row - Top) * 5 + at % 6] == '#';
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
