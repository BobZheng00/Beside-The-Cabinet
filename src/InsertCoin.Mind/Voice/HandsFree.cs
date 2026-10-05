namespace InsertCoin.Mind;

/// <summary>
/// When she listens without the talk key: the player's choices (Chat settings → Listening). All of it is off until
/// they turn it on, and never on in a call, in a full-screen app or while a game runs unless they allow it.
/// </summary>
/// <remarks>
/// Two ways, after what voice assistants do (docs/specs/2026-10-05-hands-free-listening.md):
/// <list type="bullet">
/// <item>follow-up listening: once she has finished saying something, a few seconds for an answer, as Alexa's
/// Follow-Up Mode (5 s) and Google's Continued Conversation (8 s) do;</item>
/// <item>a conversation: switched on and off by the player, she listens after everything she says until they have
/// been quiet for <see cref="ConversationSeconds"/>.</item>
/// </list>
/// </remarks>
public sealed record HandsFree
{
    public const double Shortest = 3, Longest = 20;
    /// <summary>In a conversation, how long she waits for the player before the conversation ends.</summary>
    public const double ConversationSeconds = 30;

    /// <summary>What she has just said: an answer to the player, or, on her own, a question or a remark.</summary>
    public enum Said { Answer, Question, Remark }

    /// <summary>Listen for a few seconds after she says something (see <see cref="FollowUpSeconds"/> and the kinds below).</summary>
    public bool FollowUp { get; init; }
    public double FollowUpSeconds { get; init; } = 6;
    public bool AfterAnswers { get; init; } = true;
    public bool AfterQuestions { get; init; } = true;
    public bool AfterRemarks { get; init; }

    /// <summary>A double tap of the talk key starts or ends a conversation (the tray has a switch for it too).</summary>
    public bool ConversationKey { get; init; } = true;

    /// <summary>She may listen on her own during a call (another app has the microphone or camera).</summary>
    public bool InCalls { get; init; }
    /// <summary>…while Windows holds notifications back: full screen, presenting, quiet time.</summary>
    public bool InFullScreen { get; init; }
    /// <summary>…while a game runs.</summary>
    public bool WhilePlaying { get; init; }

    /// <summary>How long she listens after saying something of this kind (seconds); null: not at all.</summary>
    public double? After(Said said, bool conversing) =>
        conversing ? ConversationSeconds
        : FollowUp && said switch { Said.Answer => AfterAnswers, Said.Question => AfterQuestions, _ => AfterRemarks }
            ? Math.Clamp(FollowUpSeconds, Shortest, Longest)
            : null;

    /// <summary>Whether she may listen on her own now.</summary>
    public bool Allows(bool inCall, bool fullScreen, bool playing) =>
        (InCalls || !inCall) && (InFullScreen || !fullScreen) && (WhilePlaying || !playing);

    /// <summary>What a line was: an answer to the player, or, said on her own, a question if it ends with one.</summary>
    public static Said Kind(bool answer, string line)
    {
        if (answer)
            return Said.Answer;
        var end = line.TrimEnd().TrimEnd(Closers);
        return end.EndsWith('?') || end.EndsWith('？') ? Said.Question : Said.Remark;
    }

    private static readonly char[] Closers = ['"', '\'', '”', '’', '」', '』', ')', '）'];
}
