using System.Text;
using System.Text.RegularExpressions;

namespace InsertCoin.Mind;

/// <summary>
/// Takes a reply as it streams in and separates what she says from what she does: "[cheer] Nice one!" plays the
/// cheer and shows "Nice one!". A bracket holding a single lower-case word (with an optional ": payload") is a
/// direction, even an unknown one, and never reaches the bubble; anything else in brackets is just text.
/// </summary>
/// <param name="text">
/// When given, the text before each direction is handed over before the direction acts, so the two keep their order
/// (her voice needs to know where in the shown text a <c>[say]</c> falls); what follows the last one is returned as usual.
/// </param>
public sealed partial class StageDirections(Action<string, string?> direction, Action<string>? text = null)
{
    private const int LongestDirection = 160;

    private readonly StringBuilder _held = new();       // from an opening bracket until we know what it is
    private bool _started;                              // some text has been shown already
    private bool _afterSpace, _skipSpace;               // the last character shown was a blank; drop the next one

    /// <summary>Feeds the next piece of the reply; returns the part of it that can be shown now.</summary>
    public string Feed(string piece)
    {
        var shown = new StringBuilder();
        foreach (var c in piece)
        {
            if (_held.Length > 0)
            {
                _held.Append(c);
                if (c == ']')
                    Resolve(shown);
                else if (_held.Length > LongestDirection)
                    Release(shown);
            }
            else if (c == '[')
                _held.Append(c);
            else if (_started || !char.IsWhiteSpace(c))         // no leading blanks left behind by a direction
            {
                if (_skipSpace && c == ' ')
                {
                    _skipSpace = false;
                    continue;
                }
                _skipSpace = false;
                _started = true;
                shown.Append(c);
                _afterSpace = c == ' ';
            }
        }
        return shown.ToString();
    }

    /// <summary>The end of the reply: whatever was held back turned out to be text.</summary>
    public string Finish()
    {
        var shown = new StringBuilder();
        if (_held.Length > 0)
            Release(shown);
        return shown.ToString();
    }

    private void Resolve(StringBuilder shown)
    {
        var match = Direction().Match(_held.ToString());
        if (!match.Success)
        {
            Release(shown);
            return;
        }
        _held.Clear();
        if (_started && shown.Length > 0 && shown[^1] == ' ')
            shown.Length--;                                     // "Great! [cheer]" leaves no trailing blank
        else if (_afterSpace)
            _skipSpace = true;                                  // that blank went out with an earlier piece: drop the one after instead
        if (text is not null && shown.Length > 0)
        {
            text(shown.ToString());
            shown.Clear();
        }
        direction(match.Groups["name"].Value, match.Groups["payload"].Success ? match.Groups["payload"].Value.Trim() : null);
    }

    private void Release(StringBuilder shown)
    {
        _started = true;
        _afterSpace = _skipSpace = false;
        shown.Append(_held);
        _held.Clear();
    }

    [GeneratedRegex(@"^\[\s*(?<name>[a-z]{2,16})\s*(:(?<payload>[^\]]*))?\]$")]
    private static partial Regex Direction();
}
