using System.Text.RegularExpressions;

namespace InsertCoin.Mind;

/// <summary>The parts of a focus session.</summary>
public enum FocusPart { Focus, Break, LongBreak }

/// <summary>
/// How long each part of a focus session lasts, in minutes, and after how many focus parts the long break comes. The
/// classic Pomodoro: 25 minutes of focus, 5 off, 15 off after every fourth.
/// </summary>
public sealed record FocusPlan(int Focus = 25, int Break = 5, int LongBreak = 15, int LongEvery = 4)
{
    /// <summary>The same plan with every length kept sensible (a minute at least, three hours of focus at most).</summary>
    public FocusPlan Checked() => new(Math.Clamp(Focus, 1, 180), Math.Clamp(Break, 1, 60), Math.Clamp(LongBreak, 1, 90), Math.Clamp(LongEvery, 2, 10));

    /// <summary>"25 min focus, 5 min breaks, 15 min after every 4 focus parts".</summary>
    public override string ToString() => $"{Focus} min focus, {Break} min breaks, {LongBreak} min after every {LongEvery} focus parts";
}

/// <summary>A part of a focus session ended (<see cref="Ended"/>) and the next began, in round <see cref="Round"/>.</summary>
public sealed record FocusTurn(FocusPart Ended, FocusPart Began, int Round);

/// <summary>
/// A focus session, Pomodoro-style (docs/specs/2026-10-05-tools-and-abilities.md, "Focus sessions"): focus, then a
/// short break, a long one after every so many focus parts, round after round until the player stops it. Pausing stops
/// the clock; skipping ends the part at once. The time is always passed in.
/// </summary>
public sealed class FocusSession
{
    private DateTimeOffset _ends;
    private TimeSpan? _pausedLeft;

    public FocusSession(FocusPlan plan, DateTimeOffset now)
    {
        Plan = plan.Checked();
        Started = now;
        Begin(FocusPart.Focus, 1, now);
    }

    public FocusPlan Plan { get; }
    public DateTimeOffset Started { get; }
    public FocusPart Part { get; private set; }

    /// <summary>Which focus part this is, counting from 1, or the one a break follows.</summary>
    public int Round { get; private set; }

    /// <summary>How many focus parts have ended (run out or skipped).</summary>
    public int Done { get; private set; }

    public bool Paused => _pausedLeft is not null;

    /// <summary>How long the part under way lasts in all.</summary>
    public TimeSpan Length => TimeSpan.FromMinutes(Part switch { FocusPart.Focus => Plan.Focus, FocusPart.Break => Plan.Break, _ => Plan.LongBreak });

    public TimeSpan Left(DateTimeOffset now) => _pausedLeft ?? (_ends > now ? _ends - now : TimeSpan.Zero);

    /// <summary>How much of the part under way is behind (0 to 1).</summary>
    public double Through(DateTimeOffset now) => 1 - Left(now) / Length;

    public void Pause(DateTimeOffset now) => _pausedLeft ??= Left(now);

    public void Resume(DateTimeOffset now)
    {
        if (_pausedLeft is not { } left)
            return;
        _ends = now + left;
        _pausedLeft = null;
    }

    /// <summary>On to the next part once this one's time is up; null while it isn't (or while paused).</summary>
    public FocusTurn? Tick(DateTimeOffset now) => !Paused && now >= _ends ? Next(now) : null;

    /// <summary>Ends the part under way now (paused or not), and the next begins.</summary>
    public FocusTurn Skip(DateTimeOffset now) => Next(now);

    private FocusTurn Next(DateTimeOffset now)
    {
        var ended = Part;
        if (Part == FocusPart.Focus)
        {
            Done++;
            Begin(Done % Plan.LongEvery == 0 ? FocusPart.LongBreak : FocusPart.Break, Round, now);
        }
        else
            Begin(FocusPart.Focus, Round + 1, now);
        return new FocusTurn(ended, Part, Round);
    }

    private void Begin(FocusPart part, int round, DateTimeOffset now)
    {
        Part = part;
        Round = round;
        _pausedLeft = null;
        _ends = now + Length;
    }

    /// <summary>Where the session stands, for her: what part, how long left, how many done, and the lengths.</summary>
    public string Describe(DateTimeOffset now)
    {
        var part = Part switch
        {
            FocusPart.Focus => $"focusing (round {Round})",
            FocusPart.Break => "on a short break",
            _ => "on a long break",
        };
        return $"Focus session since {Started:HH:mm}: {part}, {Minutes(Left(now))} left{(Paused ? ", paused" : "")}; " +
               $"{Words.Count(Done, "focus part")} done. Lengths: {Plan}.";
    }

    // ---- What she is told, to say in her own words ---------------------------------------------------------------------

    /// <summary>The player started a session on the cabinet themselves.</summary>
    public string NoteOnStart() =>
        $"The player started a focus session on the cabinet: {Plan.Focus} minutes of focus, then a {Plan.Break}-minute break. " +
        "Wish them a good one in one short sentence.";

    /// <summary>A part ended and the next began.</summary>
    public string NoteOn(FocusTurn turn) => turn switch
    {
        { Began: FocusPart.LongBreak } =>
            $"Focus part {turn.Round} of the session is over: a {Plan.LongBreak}-minute long break starts now. Tell them in one or two short sentences: " +
            "well done, and step away for a while: stand up, stretch, have some water.",
        { Began: FocusPart.Break } =>
            $"Focus part {turn.Round} of the session is over: a {Plan.Break}-minute break starts now. Tell them in one short sentence, and suggest " +
            "standing up, stretching or getting some water; it's fine to leave the work for a bit.",
        _ => $"The break is over: focus part {turn.Round} starts now, {Plan.Focus} minutes. Tell them in one short sentence.",
    };

    /// <summary>The player ended the session on the cabinet themselves.</summary>
    public string NoteOnStop(DateTimeOffset now) =>
        $"The player ended the focus session: {Words.Count(Done, "focus part")} done in {Words.Span((long)(now - Started).TotalMilliseconds)}. " +
        "Say one short sentence about it.";

    /// <summary>The player has been at the PC a long while with no real break (not counting a focus session's own breaks).</summary>
    public static string NoteOnSitting(TimeSpan sitting) =>
        $"The player has been at the PC for {Words.Span((long)sitting.TotalMilliseconds)} without a real break. In one or two short, warm sentences, " +
        "suggest standing up, stretching or getting some water; it's fine to leave the work for a bit. Don't lecture.";

    private static string Minutes(TimeSpan left) => left < TimeSpan.FromMinutes(1) ? "under a minute" : $"{(int)Math.Ceiling(left.TotalMinutes)} min";
}

/// <summary>
/// Whether the player's words ask for a focus session (#145): she starts one only then. Asked how long a Pomodoro
/// usually is, she answered and started one as well, in three runs of four; a question about them isn't asking for one.
/// Read in the player's languages (English, Chinese, Japanese, Korean), a yes as <see cref="SpokenAnswer"/> reads one.
/// </summary>
public static class FocusRequest
{
    // asking for one outright, in a question too ("can you start one?", "能帮我开个番茄钟吗")
    private static readonly Regex Start = new(@"\b(start\w*|begin\w*|kick off|set|lets)\b|开始|开个|开一|开启|启动|打开|开番茄|开专注|帮我开|^开[吧呀啊]?$|来个|来一|定个|设个|计时|" +
                                              "始め|はじめ|スタート|開始|セット|つけて|시작|켜|맞춰|돌려");
    // wanting to focus, or for how long, said rather than asked about ("I need to study", "50 minutes")
    private static readonly Regex Focus = new(@"\b(focus\w*|pomodoro\w*|stud(y|ying)|concentrat\w*|timer)\b|专注|番茄|学习|复习|集中|写作业|ポモドーロ|勉強|집중|뽀모도로|공부|" +
                                              @"\d+\s*(min\w*|hours?|分钟|分|小时|時間|분|시간)|[一二三四五六七八九十两半]+个?(分钟|小时)");
    private static readonly Regex Question = new(@"^(how|what|whats|why|when|which|who|is|are|does|isnt)\b|吗|多少|多久|几|什么|怎么|来着|呢$|是不是|有没有|" +
                                                 "何|どのくらい|どれくらい|だっけ|か$|かな$|몇|얼마|뭐|어떻게|까요?$|나요$");
    private static readonly Regex Not = new(@"\b(dont|not|no|never|안)\b|不要|不用|别|不想|しないで|いらない|やめ|하지 ?마|말고");

    /// <summary>Whether <paramref name="said"/>, the player's words her reply answers (null when the cabinet spoke), asks for a focus session.</summary>
    public static bool In(string? said)
    {
        if (string.IsNullOrWhiteSpace(said))
            return false;
        var plain = SpokenAnswer.Plain(said);
        if (Not.IsMatch(plain))
            return false;
        if (Start.IsMatch(plain))
            return true;
        var yes = SpokenAnswer.Of(said) is Answer.Once or Answer.Always || SpokenAnswer.Of(plain.Split(' ')[0]) is Answer.Once or Answer.Always;
        var question = said.Contains('?') || said.Contains('？') || Question.IsMatch(plain);
        return !question && (yes || Focus.IsMatch(plain));
    }
}
