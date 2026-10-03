using System.Text;

namespace InsertCoin.Mind;

/// <summary>
/// Cuts text that streams in into sentences, so her voice can start on the first one while the rest is still being
/// written. A full stop, question or exclamation mark ends a sentence (with any closing quotes or brackets after it);
/// a Latin one only when a space follows, so "3.5" stays whole. Chinese and Japanese marks end it at once.
/// </summary>
public sealed class Sentences
{
    private const string Ends = ".!?。！？…", WideEnds = "。！？…", Closers = "\"'”’」』）)】~～";

    private readonly StringBuilder _pending = new();
    private bool _ending, _wide;

    /// <summary>Feeds the next piece of text; returns the sentences it completed.</summary>
    public List<string> Feed(string piece)
    {
        var done = new List<string>();
        foreach (var c in piece)
        {
            if (_ending)
            {
                if (Ends.Contains(c) || Closers.Contains(c))
                {
                    _pending.Append(c);
                    _wide |= WideEnds.Contains(c);
                    continue;
                }
                if (_wide || char.IsWhiteSpace(c))
                    Flush(done);
                _ending = _wide = false;
            }
            if (c == '\n')
            {
                Flush(done);
                continue;
            }
            _pending.Append(c);
            if (Ends.Contains(c))
            {
                _ending = true;
                _wide = WideEnds.Contains(c);
            }
        }
        return done;
    }

    /// <summary>The end of the text: what is left is the last sentence, if it says anything.</summary>
    public string? Finish()
    {
        var done = new List<string>();
        Flush(done);
        _ending = _wide = false;
        return done.FirstOrDefault();
    }

    private void Flush(List<string> done)
    {
        var sentence = _pending.ToString().Trim();
        _pending.Clear();
        if (sentence.Any(char.IsLetterOrDigit))     // "…" on its own is not something to say
            done.Add(sentence);
    }
}
