namespace InsertCoin.Mind;

/// <summary>
/// Decides what of one reply her voice says, sentence by sentence as the reply streams in. Her voice speaks one
/// language. When the player writes in another, the model puts each sentence's spoken version in front of it as
/// <c>[say: …]</c> and those are spoken while the bubble shows the rest as a subtitle. When the player writes in
/// her voice's language, the written sentences themselves are spoken. A written sentence in another language with
/// no <c>[say]</c> stays silent: better than reading the subtitle out in the wrong language.
/// </summary>
public sealed class Speech(string language, Action<string> speak)
{
    private readonly Sentences _sentences = new();
    private bool _subtitled;

    public string Language => language;

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
        speak(spoken);
    }

    /// <summary>The next piece of the reply's shown text.</summary>
    public void Text(string piece)
    {
        foreach (var sentence in _sentences.Feed(piece))
            Written(sentence);
    }

    public void Finish()
    {
        if (_sentences.Finish() is { } last)
            Written(last);
    }

    /// <summary>Whether a whole line (an event remark, a card's greeting) is in the voice's language, so it can be said as it is.</summary>
    public static bool Speaks(string language, string line) => SpokenLanguage.Of(line) == language;

    private void Written(string sentence)
    {
        if (!_subtitled && Speaks(language, sentence))
            speak(sentence);
    }
}
