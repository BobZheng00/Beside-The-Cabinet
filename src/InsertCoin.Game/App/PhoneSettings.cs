using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;
using HttpClient = System.Net.Http.HttpClient;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Your phone: what reaches the player while they are away from the PC (see <see cref="Reaching"/>): how
/// long without input makes them away, and whether reminders reach their phone at the PC too; and where it goes,
/// through ntfy, Discord, Telegram or any webhook, each with its address or token (kept on this PC, encrypted, see
/// <see cref="KeyStore"/>), with a test that sends one through those ticked. The fields only show for what is ticked.
/// Each change is used at once.
/// </summary>
public partial class PhoneSettings : VBoxContainer
{
    /// <summary>Where her texts go, and the addresses and tokens for it, as they are now.</summary>
    public event Action<Elsewhere, ElsewhereSecrets>? Chosen;
    /// <summary>The away time, in minutes.</summary>
    public event Action<int>? AwayChosen;
    /// <summary>Reminders reach the phone while the player is at the PC too, or not.</summary>
    public event Action<bool>? AtThePcChosen;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private CheckBox _ntfy = null!, _discord = null!, _telegram = null!, _webhook = null!;
    private LineEdit _ntfyServer = null!, _ntfyTopic = null!, _ntfyToken = null!, _discordUrl = null!, _telegramToken = null!, _telegramChat = null!, _webhookUrl = null!;
    private Label _result = null!;

    public void Build(Elsewhere elsewhere, ElsewhereSecrets secrets, int awayMinutes, bool alsoAtThePc)
    {
        Form.Page(this, "Your phone");
        Form.Hint(this, "At the PC she tells you herself. When you're away from it, what matters reaches you here instead, in her words: " +
                        "reminders, a focus session's turns, and asking how something went. What only makes sense at the PC waits for you.");
        Form.Slider(Form.Grid(this), "Away after", Reaching.FewestAwayMinutes, Reaching.MostAwayMinutes, awayMinutes,
                    minutes => $"{minutes} minutes", minutes => AwayChosen?.Invoke(minutes));
        Form.Hint(this, "Without the mouse or keys; away at once when the screen is locked, or when you tell her you're heading out. In " +
                        "between she isn't sure: she calls you first, and texts if you don't come back.");
        Form.Tick(this, "Also while I'm at the PC", alsoAtThePc, on => AtThePcChosen?.Invoke(on));
        Form.Hint(this, "Reminders reach your phone even when you're at the PC, as a plain line, as they used to.");

        Form.Subheading(this, "Where it goes");
        Form.Hint(this, "Tick where, and fill in what it asks. Addresses and tokens stay on this PC, encrypted.");

        _ntfy = Channel("ntfy", elsewhere.Ntfy, "Free phone notifications, no account: install the ntfy app and subscribe to a topic name only you know.",
                        grid =>
                        {
                            _ntfyServer = Form.Field(grid, "Server", elsewhere.NtfyServer, "https://ntfy.sh", typed: _ => Changed());
                            _ntfyTopic = Form.Field(grid, "Topic", secrets.NtfyTopic, "a long name only you know", secret: true, typed: _ => Changed());
                            _ntfyToken = Form.Field(grid, "Token", secrets.NtfyToken, "only for a server that needs one", secret: true, typed: _ => Changed());
                        });
        _discord = Channel("Discord", elsewhere.Discord, "A message in a channel: Server settings → Integrations → Webhooks → New webhook → Copy URL.",
                           grid => _discordUrl = Form.Field(grid, "Webhook", secrets.DiscordWebhook, "https://discord.com/api/webhooks/…", secret: true, typed: _ => Changed()));
        _telegram = Channel("Telegram", elsewhere.Telegram,
                            "A message from a bot of your own. In Telegram, message @BotFather and send /newbot: it gives the bot a name and you its " +
                            "token. Send your new bot any message, then open api.telegram.org/bot<token>/getUpdates in a browser (your token in " +
                            "place of <token>): your chat id is the number after \"chat\":{\"id\":.",
                            grid =>
                            {
                                _telegramToken = Form.Field(grid, "Bot token", secrets.TelegramToken, "123456:ABC…", secret: true, typed: _ => Changed());
                                _telegramChat = Form.Field(grid, "Chat id", secrets.TelegramChat, "your chat's number", typed: _ => Changed());
                            });
        _webhook = Channel("Webhook", elsewhere.Webhook, "Any address that takes a JSON POST: {\"app\", \"title\", \"message\"}. For Home Assistant, IFTTT, your own scripts…",
                           grid => _webhookUrl = Form.Field(grid, "Address", secrets.WebhookUrl, "https://…", secret: true, typed: _ => Changed()));

        var test = Form.Buttons(this);
        Form.Button(test, "Send a test", () => _ = Test());
        _result = Form.Result(test);
    }

    /// <summary>A place reminders can go: its tick, what it is, and its fields (shown while it is ticked).</summary>
    private CheckBox Channel(string name, bool on, string hint, Action<GridContainer> fields)
    {
        var tick = Form.Tick(this, name, on, _ => Changed());
        Form.Hint(this, hint);
        var grid = Form.Grid(this);
        fields(grid);
        Form.ShowWhile(tick, grid);
        return tick;
    }

    private Elsewhere Where() => new()
    {
        Ntfy = _ntfy.ButtonPressed, NtfyServer = _ntfyServer.Text.Trim().Length > 0 ? _ntfyServer.Text.Trim() : "https://ntfy.sh",
        Discord = _discord.ButtonPressed, Telegram = _telegram.ButtonPressed, Webhook = _webhook.ButtonPressed,
    };

    private ElsewhereSecrets Secrets() => new()
    {
        NtfyTopic = Value(_ntfyTopic), NtfyToken = Value(_ntfyToken), DiscordWebhook = Value(_discordUrl),
        TelegramToken = Value(_telegramToken), TelegramChat = Value(_telegramChat), WebhookUrl = Value(_webhookUrl),
    };

    private void Changed() => Chosen?.Invoke(Where(), Secrets());

    /// <summary>A test reminder through each place ticked, saying how each went.</summary>
    private async Task Test()
    {
        var where = Where();
        var now = DateTimeOffset.Now;
        var notice = Reminders.NoticeFor([new Plan(0, "This is how a reminder reaches you.", now, false, TimeSpan.Zero, null, now, null)], now);
        var ticked = new[] { _ntfy, _discord, _telegram, _webhook }.Count(c => c.ButtonPressed);
        var parcels = Couriers.Parcels(where, Secrets(), notice).Count();
        if (ticked == 0)
        {
            Form.Say(_result, "Tick where reminders should go first.", Paper.Bad);
            return;
        }
        Form.Say(_result, "Sending…", Paper.Faint);
        var results = parcels > 0 ? await Couriers.Send(Http, where, Secrets(), notice) : [];
        if (!IsInstanceValid(this))
            return;
        var said = results.Select(r => r.Error is null ? $"sent to {r.Name}" : $"not to {r.Name} ({r.Error})").ToList();
        if (parcels < ticked)
            said.Add("fill in what each place you ticked asks for");
        var text = string.Join("; ", said);
        Form.Say(_result, char.ToUpperInvariant(text[0]) + text[1..] + ".", results.Any(r => r.Error is not null) || parcels < ticked ? Paper.Bad : Paper.Good);
    }

    private static string? Value(LineEdit field) => field.Text.Trim().Length > 0 ? field.Text.Trim() : null;
}
