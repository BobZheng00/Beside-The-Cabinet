using System.Text;

namespace InsertCoin.Mind;

/// <summary>
/// Decides what of one reply her voice says, sentence by sentence as the reply streams in. Her voice speaks one
/// language. When the player writes in another, the model puts each sentence's spoken version in front of it as
/// <c>[say: …]</c> and those are spoken while the bubble shows the rest as a subtitle. When the player writes in
/// her voice's language, the written sentences themselves are spoken. A written sentence in another language with
/// no <c>[say]</c> stays silent: better than reading the subtitle out in the wrong language.
/// </summary>
/// <remarks>
/// It also keeps where in the shown text each spoken line belongs, so that when the player cuts her off, what they
/// heard can be found in what was shown (see <see cref="CutAt"/>).
/// </remarks>
public sealed class Speech(string language, Action<string> speak)
{
    private readonly Sentences _sentences = new();
    private readonly StringBuilder _shown = new();
    private readonly List<(int Start, int? End)> _lines = [];  // each spoken line's part of the shown text (a [say]'s ends where the next begins)
    private int _searchFrom;
    private bool _subtitled, _finished;

    public string Language => language;

    /// <summary>Nothing of the reply was given to the voice (no [say], and nothing written in its language).</summary>
    public bool Silent => _lines.Count == 0;

    /// <summary>The mood her body showed last in this reply (happy, sad, surprised), for a clip that suits it.</summary>
    public string? Mood { get; private set; }

    /// <summary>A stage direction from the reply: <c>say</c> is spoken; a move sets the mood to speak in.</summary>
    public void Direction(string name, string? payload)
    {
        Mood = name switch
        {
            "cheer" or "pump" or "wave" => "happy",
            "worry" => "sad",
            "gasp" => "surprised",
            _ => Mood,
        };
        if (name != "say" || payload?.Trim() is not { Length: > 0 } spoken)
            return;
        _subtitled = true;
        if (_lines.Count > 0 && _lines[^1].End is null)
            _lines[^1] = (_lines[^1].Start, _shown.Length);
        _lines.Add((_shown.Length, null));                  // its subtitle is what is shown from here to the next [say]
        speak(spoken);
    }

    /// <summary>The next piece of the reply's shown text.</summary>
    public void Text(string piece)
    {
        _shown.Append(piece);
        foreach (var sentence in _sentences.Feed(piece))
            Written(sentence);
    }

    public void Finish()
    {
        _finished = true;
        if (_sentences.Finish() is { } last)
            Written(last);
    }

    /// <summary>Whether a whole line (an event remark, a card's greeting) is in the voice's language, so it can be said as it is.</summary>
    public static bool Speaks(string language, string line) => SpokenLanguage.Of(line) == language;

    /// <summary>
    /// Where in the shown text (as it streamed in) the player stopped hearing her, when her voice had said
    /// <paramref name="heard"/> whole lines and was <paramref name="into"/> (0 to 1) of the way through the next; null
    /// when she had said all of a finished reply.
    /// </summary>
    public int? CutAt(int heard, double into)
    {
        if (heard >= _lines.Count)
            return _finished ? null : _lines.Count == 0 ? 0 : End(_lines.Count - 1);
        var start = _lines[heard].Start;
        return start + (int)Math.Round((End(heard) - start) * Math.Clamp(into, 0, 1));
    }

    private int End(int line) => _lines[line].End ?? (line + 1 < _lines.Count ? _lines[line + 1].Start : _shown.Length);

    private void Written(string sentence)
    {
        if (_subtitled || !Speaks(language, sentence))
            return;
        // the sentence's place in the shown text (it was trimmed, and is found from where the last one ended)
        var shown = _shown.ToString();
        var at = shown.IndexOf(sentence, _searchFrom, StringComparison.Ordinal);
        if (at < 0)
            at = _searchFrom;
        _searchFrom = Math.Min(shown.Length, at + sentence.Length);
        _lines.Add((at, _searchFrom));
        speak(sentence);
    }
}
