using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace InsertCoin.Mind;

/// <summary>
/// Something from a chat with her bot: a message (its text or caption, a photo or a file), or a press on one of the
/// buttons under a message of hers.
/// </summary>
/// <param name="Id">Telegram's number for it: asking for what comes after it confirms it was handled.</param>
/// <param name="File">A photo's id (its largest size) or a file's, to fetch (see <see cref="Telegram.File"/>).</param>
/// <param name="Pressed">The button pressed, as it was sent (see <see cref="Telegram.Send"/>).</param>
/// <param name="Press">The press itself, to answer, so the button stops spinning.</param>
/// <param name="Message">The message whose button was pressed.</param>
public sealed record Update(long Id, string Chat, bool Private, DateTimeOffset At, string? Text = null, string? File = null, string? FileName = null,
                            string? Pressed = null, string? Press = null, long? Message = null);

/// <summary>Telegram said no: its code (401: a token it doesn't know; 409: another copy reading the same bot) and why.</summary>
public sealed class TelegramException(int code, string message) : Exception(message)
{
    public int Code => code;
}

/// <summary>
/// The player's own Telegram bot, through which she texts with them both ways (spec 2026-10-10-reaching-the-player.md,
/// "Telegram first"), by Telegram's Bot API: the cabinet asks for new messages and waits for them (long polling), so it
/// only ever calls out and opens no port. Telegram keeps what was sent for up to a day, until it is asked for.
/// </summary>
public sealed class Telegram(HttpClient http, string token)
{
    /// <summary>A message's longest text; a longer one (a memo) goes in parts.</summary>
    private const int Longest = 4000;

    /// <summary>Telegram's Bot API. Dev: a stand-in server, so that testing never needs a real bot.</summary>
    public static string Server { get; set; } = "https://api.telegram.org";

    /// <summary>The bot's username (without the @), or null when Telegram doesn't know the token.</summary>
    public async Task<string?> Me(CancellationToken cancel = default)
    {
        try
        {
            return (string?)(await Call("getMe", new { }, cancel))["username"];
        }
        catch (TelegramException e) when (e.Code is 401 or 404)
        {
            return null;
        }
    }

    /// <summary>What came after <paramref name="offset"/> (asking confirms all before it), waiting up to <paramref name="wait"/> for something.</summary>
    public async Task<IReadOnlyList<Update>> Updates(long offset, TimeSpan wait, CancellationToken cancel = default)
    {
        var updates = await Call("getUpdates", new { offset, timeout = (int)wait.TotalSeconds, allowed_updates = new[] { "message", "callback_query" } }, cancel);
        return [.. updates.AsArray().Select(Read).OfType<Update>()];
    }

    private static Update? Read(JsonNode? update)
    {
        if (update?["update_id"] is not JsonValue number)
            return null;
        var id = number.GetValue<long>();
        if (update["callback_query"] is { } press)
            return new Update(id, press["message"]?["chat"]?["id"]?.ToString() ?? "", (string?)press["message"]?["chat"]?["type"] == "private",
                              Date(press["message"]), Pressed: (string?)press["data"], Press: (string?)press["id"], Message: (long?)press["message"]?["message_id"]);
        if (update["message"] is not { } message)
            return new Update(id, "", false, DateTimeOffset.UnixEpoch);     // something else: confirmed, and nothing to do
        var photo = message["photo"]?.AsArray().LastOrDefault();             // its sizes, smallest first
        var document = message["document"];
        return new Update(id, message["chat"]?["id"]?.ToString() ?? "", (string?)message["chat"]?["type"] == "private", Date(message),
                          Text: (string?)message["text"] ?? (string?)message["caption"],
                          File: (string?)photo?["file_id"] ?? (string?)document?["file_id"],
                          FileName: photo is not null ? "photo.jpg" : (string?)document?["file_name"]);
    }

    private static DateTimeOffset Date(JsonNode? message) => DateTimeOffset.FromUnixTimeSeconds((long?)message?["date"] ?? 0);

    /// <summary>A message to <paramref name="chat"/>, in parts if it is long; buttons go under the last (each sends back its data when pressed).</summary>
    public async Task Send(string chat, string text, IReadOnlyList<(string Text, string Data)>? buttons = null, CancellationToken cancel = default)
    {
        for (var at = 0; at < text.Length; at += Longest)
        {
            var part = text.Substring(at, Math.Min(Longest, text.Length - at));
            var last = at + Longest >= text.Length;
            await Call("sendMessage", buttons is null || !last ? new { chat_id = chat, text = part }
                : new { chat_id = chat, text = part, reply_markup = new { inline_keyboard = new[] { buttons.Select(b => new { text = b.Text, callback_data = b.Data }).ToArray() } } },
                cancel);
        }
    }

    /// <summary>"Typing…" in the player's chat while she writes (Telegram shows it for a few seconds).</summary>
    public Task Typing(string chat, CancellationToken cancel = default) => Call("sendChatAction", new { chat_id = chat, action = "typing" }, cancel);

    /// <summary>A press answered: its button stops spinning, and the buttons go, as one press is the answer.</summary>
    public async Task Answered(Update press, CancellationToken cancel = default)
    {
        await Call("answerCallbackQuery", new { callback_query_id = press.Press }, cancel);
        if (press.Message is { } message)
            await Call("editMessageReplyMarkup", new { chat_id = press.Chat, message_id = message }, cancel);
    }

    /// <summary>A photo's or a file's contents.</summary>
    public async Task<byte[]> File(string id, CancellationToken cancel = default)
    {
        var path = (string?)(await Call("getFile", new { file_id = id }, cancel))["file_path"] ?? throw new TelegramException(0, "Telegram gave no file to fetch");
        return await http.GetByteArrayAsync($"{Server}/file/bot{token}/{path}", cancel);
    }

    /// <summary>Gives the bot her name and a word on what it is, as the player sees it in Telegram.</summary>
    public async Task Name(string name, string description, CancellationToken cancel = default)
    {
        await Call("setMyName", new { name }, cancel);
        await Call("setMyDescription", new { description }, cancel);
    }

    private async Task<JsonNode> Call(string method, object body, CancellationToken cancel)
    {
        using var response = await http.PostAsJsonAsync($"{Server}/bot{token}/{method}", body, cancel);
        JsonNode? answer = null;
        try
        {
            answer = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancel));
        }
        catch (JsonException)
        {
            // not Telegram's JSON (a proxy's error page, say): its status says what went wrong
        }
        if (answer?["ok"] is JsonValue ok && ok.GetValue<bool>() && answer["result"] is { } result)
            return result;
        throw new TelegramException((int?)answer?["error_code"] ?? (int)response.StatusCode, (string?)answer?["description"] ?? response.ReasonPhrase ?? "no answer");
    }

    /// <summary>
    /// What the player texted in one go, as one message for her: each on a line of its own, with how long ago it was
    /// sent when it waited (the cabinet closed, the PC asleep), so she answers it all once, knowing.
    /// </summary>
    public static string Together(IEnumerable<(string Text, DateTimeOffset At)> texts, DateTimeOffset now) =>
        string.Join("\n", texts.OrderBy(t => t.At).Select(t => now - t.At > TimeSpan.FromMinutes(2) ? $"{t.Text} (sent {When.Ago(t.At, now)})" : t.Text));
}

/// <summary>
/// Her questions by text, as buttons under a message (her card: may she use a tool, does an activity count, her
/// suggested change to a skill), the first button for yes and the last for no, any "always" between. What ends one: a
/// press; a short yes, always or no typed instead, read as its button would be (see <see cref="SpokenAnswer"/>, in the
/// app's languages, whichever the player writes in); anything else they send, which leaves it unanswered and goes to her
/// as a new message; or <see cref="Waits"/> with none of these, as her card by the stool waits. Unanswered, nothing it
/// asked about is done, and the chat says so. So a question never holds her replies, or the player's texts, for long.
/// </summary>
public sealed class Questions
{
    /// <summary>How long a question waits for an answer (to tune, as the card by her stool's 120 s).</summary>
    public static readonly TimeSpan Waits = TimeSpan.FromSeconds(120);

    /// <summary>What the chat says when a question ends unanswered.</summary>
    public const string Unanswered = "(Left unanswered, so nothing was done.)";

    private readonly Dictionary<string, (TaskCompletionSource<string?> Answer, DateTimeOffset Until, IReadOnlyList<(string Text, string Data)> Buttons)> _open = [];

    /// <summary>A question waits for the player's answer.</summary>
    public bool Asking => _open.Count > 0;

    /// <summary>
    /// Sends <paramref name="text"/> with its buttons through <paramref name="send"/>, and waits: the data of the button
    /// its answer chose, or null when it went unanswered (or <paramref name="cancel"/> came first), the chat then told so.
    /// </summary>
    public async Task<string?> Ask(string text, IReadOnlyList<(string Text, string Data)> buttons,
                                   Func<string, IReadOnlyList<(string Text, string Data)>?, Task> send, DateTimeOffset now, CancellationToken cancel)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _open[id] = (answer, now + Waits, buttons);
        string? answered;
        try
        {
            await using (cancel.Register(() => answer.TrySetResult(null)))
            {
                await send(text, [.. buttons.Select(b => (b.Text, $"{id}:{b.Data}"))]);
                answered = await answer.Task;
            }
        }
        finally
        {
            _open.Remove(id);
        }
        if (answered is null)
            await send(Unanswered, null);
        return answered;
    }

    /// <summary>A button pressed, with the data it was sent with (see <see cref="Ask"/>); one of a question already ended does nothing.</summary>
    public void Pressed(string? pressed)
    {
        if (pressed?.Split(':', 2) is [var id, var data])
            End(id, data);
    }

    /// <summary>
    /// The player sent something while a question waits (<paramref name="typed"/> null for a photo or a file): true when
    /// it was a short yes, always or no, now the question's answer; otherwise the question ends unanswered, and false,
    /// as it is a new message.
    /// </summary>
    public bool Typed(string? typed)
    {
        if (_open.Count == 0)
            return false;
        var answer = typed is null ? null : SpokenAnswer.Of(typed);
        foreach (var (id, open) in _open.ToList())
            End(id, answer switch
            {
                Answer.No => open.Buttons[^1].Data,
                Answer.Always when open.Buttons.Any(b => b.Data == "always") => "always",
                Answer.Once or Answer.Always => open.Buttons[0].Data,
                _ => null,
            });
        return answer is not null;
    }

    /// <summary>What has waited its time by <paramref name="now"/> ends unanswered.</summary>
    public void Lapse(DateTimeOffset now)
    {
        foreach (var (id, open) in _open.ToList())
            if (now >= open.Until)
                End(id, null);
    }

    private void End(string id, string? data)
    {
        if (_open.Remove(id, out var open))
            open.Answer.TrySetResult(data);
    }
}

/// <summary>
/// Pairing her bot with the player's own chat: a code in the link they open, or the QR code they scan, which Telegram
/// sends back as "/start code" when they press Start. Used once and good for ten minutes; after it, only that chat is
/// listened to.
/// </summary>
public static class Pairing
{
    public static readonly TimeSpan Lasts = TimeSpan.FromMinutes(10);

    /// <summary>A new code: random, in the letters Telegram's links allow.</summary>
    public static string NewCode() => Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    public static string Link(string bot, string code) => $"https://t.me/{bot}?start={code}";

    /// <summary>The same in Telegram in a browser, for a PC without Telegram's app (t.me needs the app there to open).</summary>
    public static string WebLink(string bot, string code) =>
        "https://web.telegram.org/k/#?tgaddr=" + Uri.EscapeDataString($"tg://resolve?domain={bot}&start={code}");

    /// <summary>The player pressed Start through the link with this code, in a chat of their own, in time.</summary>
    public static bool Pairs(Update update, string code, DateTimeOffset issued, DateTimeOffset now) =>
        update.Private && update.Text?.Trim() == $"/start {code}" && now - issued <= Lasts;
}
