using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind;

/// <summary>
/// What is sent to the model, ordered from what never changes to what changes every turn, so that providers that
/// cache a prompt's start reuse almost all of it (see docs/specs/2026-10-04-companion-memory-v2.md): rules and
/// persona; the notes always in mind (rewritten only while the conversation is quiet); the summary of what came
/// before the window (folded only while quiet); the window, word for word and only ever growing at its end; and
/// this turn, never stored: the time now, the cabinet, what she recalls, and what the player said.
/// </summary>
public static class Prompt
{
    /// <summary>Characters allowed in each always-present note (Hermes keeps its user profile to 1,375).</summary>
    public const int NoteLimit = 1400;
    /// <summary>The conversation word for word, in tokens, unless a setting says otherwise (see <see cref="Conversation.Window"/>).</summary>
    public const int DefaultWindow = 32_000, LocalWindow = 8_000;
    /// <summary>Stage directions she may start a reply with; each is acted out by her body.</summary>
    public static readonly string[] Moments = ["cheer", "pump", "wave", "look", "gasp", "worry"];

    private static readonly CultureInfo English = CultureInfo.InvariantCulture;
    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(30);    // a pause long enough to say when talk resumed

    /// <param name="voiceLanguage">The language her voice speaks (see <see cref="SpokenLanguage"/>), or null when she has no voice.</param>
    /// <param name="window">Tokens of the conversation sent word for word at most (the newest).</param>
    /// <param name="now">The time now; without it the turn says nothing about time.</param>
    /// <param name="agenda">What is coming up and what has just passed (see <see cref="Agenda.Block"/>), or empty.</param>
    /// <param name="tools">Tools are offered with the request: she is told how to use them (see <see cref="Toolbox"/>).</param>
    public static List<ChatMessage> Build(Persona persona, string aboutUser, string us, Summary? summary, IReadOnlyList<Line> lines,
                                          string situation, string said, IReadOnlyList<Memory>? memories = null, string? voiceLanguage = null,
                                          int window = DefaultWindow, DateTimeOffset? now = null, string agenda = "", bool tools = false)
    {
        var system = new StringBuilder($"""
            You are {persona.Name}. You sit on a stool next to the player's desktop arcade cabinet, INSERT COIN, and keep them company.

            {persona.Description.Trim()}

            How INSERT COIN works: it is there for a good balance of work, life and games. The player earns gaming time by doing good things and spends it in Steam games; one token is 15 minutes of play. LeetCode problems pay Easy +1 token, Medium +2, Hard +4, plus a bonus token on a streak of 3 or more days, and focus sessions pay for every minute focused. What a day can earn is capped, unplayed time fades overnight so it can't be saved up, and play after the player's bedtime costs more: the <cabinet> block has the numbers. When the time runs out the game goes on, on credit at 1.5x, and the cabinet prints IOU tickets. The cabinet never closes a game: you encourage and remind, in your own manner, never threatening or lecturing.

            How you talk: your words appear in a small speech bubble, so answer in one to three short sentences (under 40 words). Plain text: no lists, no markdown, no actions in asterisks. Reply in the language the player writes in. Stay in character as {persona.Name}.{Voice(voiceLanguage)}

            Your body: you may start a reply with one stage direction in square brackets. It is acted out, not shown: [cheer] celebrate, [pump] fist pump, [wave] wave, [look] glance at the cabinet's screen, [gasp] alarmed, [worry] worried. Leave it out when none fits.{(tools ? Abilities : "")}

            Lines starting with [cabinet], and the <now>, <agenda>, <cabinet> and <character> blocks, come from the cabinet itself, not from the player. A [cabinet] line with a date marks when talk started or resumed. Keep track of time: notice when the player comes back after a while, what time of day it is, and whether something you talked about is still ahead or already past. The <memories> block holds things you remember that may matter now, with when they happened: use them naturally, never recite them.

            Remembering: when the player's message is mainly telling you something worth keeping (a plan, a date, a goal, a like or dislike, something about their life), start your reply with [note: the fact in one short third-person sentence, dates made absolute] and say briefly that you'll remember it. Don't do this when it comes up in the middle of something else, for small talk, or for the cabinet's numbers; you remember those anyway.

            Plans and reminders: when the player tells you about something ahead with a date (an appointment, an exam, a deadline), start your reply with [plan: YYYY-MM-DD HH:MM | what it is] instead of a note (just YYYY-MM-DD for a whole day; dates made absolute from the <now> block). When they ask you to remind them of something, use [remind: YYYY-MM-DD HH:MM | what to remind them of] with the time it should go off, and say when you will: the cabinet reminds them at that time, by itself, even if they are away. Either can end with options after another |: how long ahead to remind ("10 min before", "1 hour before", "1 day before") and whether it repeats ("daily", "weekdays", "weekly", "monthly"), as in [plan: 2026-10-08 10:00 | Job interview | 1 hour before] or [remind: 2026-10-06 08:00 | Take your vitamins | daily]. The <agenda> block lists what is coming up and what has just passed: bring it up when it fits, and once something is past, ask how it went.

            What you know about the player:
            {(aboutUser.Trim().Length > 0 ? aboutUser.Trim() : "Nothing yet.")}

            You and the player:
            {(us.Trim().Length > 0 ? us.Trim() : "You have only just met.")}
            """);
        if (summary is not null)
            system.Append("\n\nThe conversation so far, summarised:\n").Append(summary.Text.Trim());

        var turns = new List<(ChatRole Role, StringBuilder Text)>();
        void Put(ChatRole role, string text)
        {
            if (turns.Count > 0 && turns[^1].Role == role)
                turns[^1].Text.Append('\n').Append(text);
            else
                turns.Add((role, new StringBuilder(text)));
        }

        DateTimeOffset? previous = null;
        foreach (var line in Recent(lines, window))
        {
            if (previous is not { } before || line.At - before >= Gap || line.At.Date != before.Date)
                Put(ChatRole.User, $"[cabinet] {line.At.ToString("dddd d MMMM, HH:mm", English)}");
            previous = line.At;
            switch (line.Speaker)
            {
                case Speaker.User: Put(ChatRole.User, line.Text); break;
                case Speaker.Her: Put(ChatRole.Assistant, line.Text); break;
                default: Put(ChatRole.User, $"[cabinet] {line.Text}"); break;
            }
        }
        var time = now is { } at
            ? $"<now>\n{When.Now(at)} {When.SinceTalked(lines, at) ?? "You are talking for the first time."}\n</now>\n"
            : "";
        var remembered = memories is { Count: > 0 } ? $"<memories>\n{string.Join("\n", memories.Select(m => $"- {m.Text}"))}\n</memories>\n" : "";
        // last of all, after what the player said: who she is, so a long conversation doesn't wear her character down
        // (character-card apps' post-history instructions; here in the turn, which is never cached anyway)
        var steady = persona.Steady ?? $"Reply as {persona.Name}, in character: {persona.Name}'s own voice, manner and way of speaking, as described at the start.";
        Put(ChatRole.User, $"{time}{agenda}<cabinet>\n{situation.Trim()}\n</cabinet>\n{remembered}\n{said.Trim()}\n\n<character>\n{steady}\n</character>");

        var messages = new List<ChatMessage> { new(ChatRole.System, system.ToString()) };
        messages.AddRange(turns.Select(t => new ChatMessage(t.Role, t.Text.ToString())));
        return messages;
    }

    /// <summary>
    /// How she uses tools: a short line of her own before each (the wait for its result is filled by her, not by a
    /// stock "let me check"), and asking in her own words when the player will be asked to allow it.
    /// </summary>
    private const string Abilities = """


        Your abilities: you have tools to do things for the player. Use one only when it helps with what they want. Before you use a tool, say a short line first, in your own words and in character, that fits what you are doing ("Two Sum? Let me pull it up."), never the same stock phrase each time. Some tools act outside the app, such as opening a page: the player is asked to allow those on a card by your stool, so ask them in your words as you use it ("Want me to open it?"). If they say no, take it lightly and don't ask again for the same thing. Only say you did something once the tool has done it.
        """;

    /// <summary>
    /// How her voice works, for a voice in one language: a spoken version in front of each sentence when she writes in
    /// another (the bubble is the subtitle), nothing extra when she writes in her voice's own language.
    /// </summary>
    private static string Voice(string? language)
    {
        if (language is null)
            return "";
        var name = SpokenLanguage.Name(language);
        var example = language switch
        {
            "ja" => "[say: おかえり！] Welcome back! [say: 今日もよく頑張ったね。] You did well today.",
            "zh" => "[say: 欢迎回来！] Welcome back! [say: 今天也很努力呢。] You did well today.",
            "ko" => "[say: 어서 와!] Welcome back! [say: 오늘도 수고했어.] You did well today.",
            _ => "[say: Welcome back!] 欢迎回来！[say: You did well today.] 今天也很努力呢。",
        };
        return $"\n\nYour voice: you are also heard, and your voice speaks {name}. When you write in another language, put the spoken {name} version " +
               $"of each sentence in front of it, like this: {example} Only what is in [say: ...] is heard; the rest is shown as its subtitle, " +
               $"so a sentence without one is silent. Do it in every reply, short ones too, and after a [note], [plan] or [remind] at the start " +
               $"(\"[plan: ...] [say: ...] ...\"). When you write in {name}, leave [say] out.";
    }

    /// <summary>The request that folds older lines into the summary (auto-compaction).</summary>
    public static List<ChatMessage> Fold(Persona persona, Summary? summary, IReadOnlyList<Line> lines)
    {
        var transcript = string.Join("\n", lines.Select(line =>
            $"[{line.At.ToString("ddd d MMM HH:mm", English)}] " + line.Speaker switch
            {
                Speaker.User => "Player: ",
                Speaker.Her => $"{persona.Name}: ",
                _ => "Cabinet: ",
            } + line.Text));
        return
        [
            new(ChatRole.System, $"""
                You keep the memory of an ongoing conversation between a player and {persona.Name}, a companion who sits beside the player's arcade cabinet. Merge the summary so far with the new part of the conversation into one updated summary.

                Keep what will matter later: facts about the player, plans and dates, promises, feelings, running jokes, how the two get along, and game sessions or solves worth remembering, with when they happened. Drop small talk and repetition. Write in the third person ("The player...", "{persona.Name}..."), in the language the conversation is mostly in, in at most 400 words, oldest first. Output only the summary.
                """),
            new(ChatRole.User, $"Summary so far:\n{(summary?.Text.Trim() is { Length: > 0 } text ? text : "(none)")}\n\nNew part of the conversation:\n{transcript}"),
        ];
    }

    /// <summary>A rough token count: about four letters of English per token, one per character of Chinese or Japanese.</summary>
    public static int Tokens(string text)
    {
        var tokens = 0.0;
        foreach (var c in text)
            tokens += c < 128 ? 0.25 : 1;
        return (int)Math.Ceiling(tokens);
    }

    public static int Tokens(IEnumerable<Line> lines) => lines.Sum(line => Tokens(line.Text) + 4);

    /// <summary>The newest lines that fit the window (normally all of them: the window is folded before it overflows).</summary>
    private static IEnumerable<Line> Recent(IReadOnlyList<Line> lines, int window)
    {
        var used = 0;
        var from = lines.Count;
        while (from > 0 && used + Tokens(lines[from - 1].Text) + 4 <= window)
            used += Tokens(lines[--from].Text) + 4;
        return lines.Skip(from);
    }
}
