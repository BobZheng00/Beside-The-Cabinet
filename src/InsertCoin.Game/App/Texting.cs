using Godot;
using InsertCoin.Mind;
using HttpClient = System.Net.Http.HttpClient;

namespace InsertCoin.Game.App;

/// <summary>
/// Texting with her on Telegram, through the player's own bot (see <see cref="Telegram"/>). While a bot is set up it
/// asks Telegram for new messages, one long wait at a time, and hands the player's to her (their text, photos and
/// files; what came at once, or while the cabinet was closed, together), and their presses to whatever put the buttons
/// up. A new bot is paired with the player's chat by a code (see <see cref="Pairing"/>). Only that chat is listened to:
/// anyone else who finds the bot is ignored, never answered, and nothing of theirs reaches her.
/// </summary>
public partial class Texting : Node
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(70) };     // longer than a long wait
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(50);

    /// <summary>What the player texted, all that came at once as one, and the photos and files they sent with it.</summary>
    public event Action<string, IReadOnlyList<Attachment>>? Texted;

    /// <summary>The player pressed Start through the pairing link: the bot's token and their chat, to keep.</summary>
    public event Action<string, string>? Paired;

    /// <summary>Dev: the next pairing code, so that a stand-in Telegram can send it back.</summary>
    public static string? DevCode { get; set; }

    private Telegram? _bot;
    private string? _token, _chat;
    private (string Code, DateTimeOffset Issued)? _pairing;
    private CancellationTokenSource? _stop;
    private readonly Questions _questions = new();

    /// <summary>A chat to text with: a bot set up and paired.</summary>
    public bool Linked => _bot is not null && _chat is not null;

    /// <summary>Listens through this bot to this chat (with none yet, only for a pairing code); no token stops listening.</summary>
    public void Use(string? token, string? chat)
    {
        _stop?.Cancel();
        _token = token;
        _chat = chat;
        _bot = token is { Length: > 0 } ? new Telegram(Http, token) : null;
        if (_bot is null)
            return;
        _stop = new CancellationTokenSource();
        _ = Listen(_bot, _stop.Token);
    }

    /// <summary>A new pairing code for this bot, for its links (see <see cref="Pairing"/>), good for ten minutes. A chat paired before is let go.</summary>
    public string Pair(string token)
    {
        var code = DevCode ?? Pairing.NewCode();
        Use(token, null);
        _pairing = (code, DateTimeOffset.Now);
        return code;
    }

    /// <summary>A message to the player; if Telegram can't be reached, the log says so.</summary>
    public async Task Send(string text, IReadOnlyList<(string Text, string Data)>? buttons = null)
    {
        if (_bot is not { } bot || _chat is not { } chat || text.Length == 0)
            return;
        try
        {
            await bot.Send(chat, text, buttons);
        }
        catch (Exception e) when (e is TelegramException or HttpRequestException or TaskCanceledException)
        {
            GD.PrintErr($"Texting: could not send: {e.Message}");
        }
    }

    /// <summary>"Typing…" in their chat while she writes.</summary>
    public void Typing()
    {
        if (_bot is { } bot && _chat is { } chat)
            _ = bot.Typing(chat).ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// A question with buttons under it, the first for yes and the last for no (see <see cref="Questions"/>): the data
    /// of the one its answer chose, or null when it went unanswered.
    /// </summary>
    public async Task<string?> Ask(string text, IReadOnlyList<(string Text, string Data)> buttons, CancellationToken cancel) =>
        Linked ? await _questions.Ask(text, buttons, Send, DateTimeOffset.Now, cancel) : null;

    /// <summary>A question of hers waits for the player's answer by text.</summary>
    public bool Asking => _questions.Asking;

    public override void _Process(double delta) => _questions.Lapse(DateTimeOffset.Now);

    private async Task Listen(Telegram bot, CancellationToken stop)
    {
        long offset = 0;
        var wait = TimeSpan.Zero;                           // first, what came while the cabinet was closed
        var failures = 0;
        while (!stop.IsCancellationRequested)
        {
            IReadOnlyList<Update> updates;
            try
            {
                updates = await bot.Updates(offset, wait, stop);
                failures = 0;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (e is TelegramException or HttpRequestException or TaskCanceledException)
            {
                GD.PrintErr(e is TelegramException { Code: 409 }
                    ? "Texting: another copy of the cabinet reads this bot's messages: each profile needs a bot of its own"
                    : $"Texting: Telegram can't be reached ({e.Message}); trying again");
                if (e is TelegramException { Code: 401 })
                    return;                                 // a token Telegram doesn't know: nothing to try again
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(300, 5 << Math.Min(failures++, 6))), stop);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                continue;
            }
            if (!IsInstanceValid(this) || stop.IsCancellationRequested)
                return;
            foreach (var update in updates)
                offset = Math.Max(offset, update.Id + 1);   // asking after it confirms it
            await Handle(bot, updates);
            wait = Wait;
        }
    }

    private async Task Handle(Telegram bot, IReadOnlyList<Update> updates)
    {
        var texts = new List<(string Text, DateTimeOffset At)>();
        var handed = new List<Attachment>();
        foreach (var update in updates)
        {
            if (_chat is null)
            {
                if (_pairing is { } pairing && Pairing.Pairs(update, pairing.Code, pairing.Issued, DateTimeOffset.Now))
                {
                    _chat = update.Chat;
                    _pairing = null;
                    GD.Print("Texting: paired with the player's chat");
                    Paired?.Invoke(_token!, update.Chat);
                }
                continue;                                   // until then, nothing else is listened to
            }
            if (update.Chat != _chat)
            {
                if (update.Chat.Length > 0)
                    GD.Print("Texting: a message from someone else was ignored");
                continue;
            }
            if (update.Press is not null)
            {
                try
                {
                    await bot.Answered(update);
                }
                catch (Exception e) when (e is TelegramException or HttpRequestException or TaskCanceledException)
                {
                    GD.PrintErr($"Texting: could not answer a press: {e.Message}");
                }
                _questions.Pressed(update.Pressed);
                continue;
            }
            if (update.Text?.StartsWith("/start", StringComparison.Ordinal) == true)
                continue;                                   // Start pressed again
            if (_questions.Typed(update.File is null ? update.Text : null))
            {
                GD.Print("Texting: the player typed their answer to her question");
                continue;                                   // anything else leaves her question unanswered, and is a new message
            }
            if (update.File is { } file)
                try
                {
                    var (thing, _, why) = Handing.From(update.FileName ?? "file", await bot.File(file));
                    if (thing is not null)
                        handed.Add(thing);
                    else
                        await Send($"That one didn't get through: {why!.ToLowerInvariant()}.");
                }
                catch (Exception e) when (e is TelegramException or HttpRequestException or TaskCanceledException)
                {
                    GD.PrintErr($"Texting: could not fetch what they sent: {e.Message}");
                }
            if (update.Text is { Length: > 0 } text)
                texts.Add((text, update.At));
        }
        if (texts.Count > 0 || handed.Count > 0)
            Texted?.Invoke(Telegram.Together(texts, DateTimeOffset.Now), handed);
    }

    public override void _ExitTree() => _stop?.Cancel();
}
