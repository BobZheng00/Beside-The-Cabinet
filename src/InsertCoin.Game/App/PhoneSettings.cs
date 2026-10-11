using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;
using HttpClient = System.Net.Http.HttpClient;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Your phone: what reaches the player while they are away from the PC (see <see cref="Reaching"/>): how
/// long without input makes them away, and whether reminders reach their phone at the PC too; texting with her on
/// Telegram, both ways, set up a step at a time (a bot of their own, its token, then a code to scan, see
/// <see cref="Pairing"/>); and, for notifications only, ntfy, Discord or any webhook, each with its address or token
/// (kept on this PC, encrypted, see <see cref="KeyStore"/>), with a test that sends one through those ticked. The
/// fields only show for what is ticked. Each change is used at once.
/// </summary>
public partial class PhoneSettings : VBoxContainer
{
    /// <summary>Where her texts go, and the addresses and tokens for it, as they are now (the Telegram bot is the pairing's, not these).</summary>
    public event Action<Elsewhere, ElsewhereSecrets>? Chosen;
    /// <summary>The away time, in minutes.</summary>
    public event Action<int>? AwayChosen;
    /// <summary>Reminders reach the phone while the player is at the PC too, or not.</summary>
    public event Action<bool>? AtThePcChosen;
    /// <summary>How much she reaches out to the phone.</summary>
    public event Action<Reach>? ReachChosen;

    private static readonly (Reach Reach, string Label)[] Reaches =
        [(Reach.OnlyWhatYouSet, "Only what you set"), (Reach.LikeARoommate, "Like a roommate"), (Reach.KeepsInTouch, "Keeps in touch")];
    /// <summary>"Forget it": the paired bot is let go.</summary>
    public event Action? TelegramForgotten;

    private const string BotFather = "https://web.telegram.org/k/#@BotFather";      // in a browser: t.me needs Telegram's app on this PC
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private Elsewhere _elsewhere = new();
    private ElsewhereSecrets _secrets = new();
    private CheckBox _ntfy = null!, _discord = null!, _webhook = null!;
    private LineEdit _ntfyServer = null!, _ntfyTopic = null!, _ntfyToken = null!, _discordUrl = null!, _webhookUrl = null!;
    private Label _result = null!;

    /// <param name="texting">The texting with her, to pair a new bot through.</param>
    /// <param name="name">Her name, which the bot is given.</param>
    public void Build(Elsewhere elsewhere, ElsewhereSecrets secrets, int awayMinutes, bool alsoAtThePc, Reach reach, Texting texting, string name)
    {
        _elsewhere = elsewhere;
        _secrets = secrets;
        Form.Page(this, "Your phone");
        Form.Hint(this, "At the PC she tells you herself. When you're away from it, what matters reaches you here instead, in her words: " +
                        "reminders, a focus session's turns, and asking how something went. What only makes sense at the PC waits for you.");
        Form.Slider(Form.Grid(this), "Away after", Reaching.FewestAwayMinutes, Reaching.MostAwayMinutes, awayMinutes,
                    minutes => $"{minutes} minutes", minutes => AwayChosen?.Invoke(minutes));
        Form.Hint(this, "Without the mouse or keys; away at once when the screen is locked, or when you tell her you're heading out. In " +
                        "between she isn't sure: she calls you first, and texts if you don't come back.");
        Form.Choice(Form.Grid(this), "She reaches out", Reaches.Select(r => r.Label), Array.FindIndex(Reaches, r => r.Reach == reach),
                    index => ReachChosen?.Invoke(Reaches[index].Reach));
        Form.Hint(this, $"Only what you set: reminders and a focus session's turns. Like a roommate: those, and what of hers matters, such as " +
                        $"asking how something went. Keeps in touch: also, now and then, a text of {name}'s own when you've been away an hour " +
                        "or more: twice a day at most, three hours apart, never at night, never while her last one is unanswered. At the PC " +
                        "she is the same whichever you choose.");
        Form.Tick(this, "Also while I'm at the PC", alsoAtThePc, on => AtThePcChosen?.Invoke(on));
        Form.Hint(this, "Reminders reach your phone even when you're at the PC, as a plain line, as they used to.");

        BuildTelegram(texting, name, paired: elsewhere.Telegram && secrets.TelegramToken is not null && secrets.TelegramChat is not null);

        Form.Subheading(this, "Notifications only");
        Form.Hint(this, "Her texts can also go where you can read but not answer. Tick where, and fill in what it asks. Addresses and tokens " +
                        "stay on this PC, encrypted.");
        _ntfy = Channel("ntfy", elsewhere.Ntfy, "Free phone notifications, no account: install the ntfy app and subscribe to a topic name only you know.",
                        grid =>
                        {
                            _ntfyServer = Form.Field(grid, "Server", elsewhere.NtfyServer, "https://ntfy.sh", typed: _ => Changed());
                            _ntfyTopic = Form.Field(grid, "Topic", secrets.NtfyTopic, "a long name only you know", secret: true, typed: _ => Changed());
                            _ntfyToken = Form.Field(grid, "Token", secrets.NtfyToken, "only for a server that needs one", secret: true, typed: _ => Changed());
                        });
        _discord = Channel("Discord", elsewhere.Discord, "A message in a channel: Server settings → Integrations → Webhooks → New webhook → Copy URL.",
                           grid => _discordUrl = Form.Field(grid, "Webhook", secrets.DiscordWebhook, "https://discord.com/api/webhooks/…", secret: true, typed: _ => Changed()));
        _webhook = Channel("Webhook", elsewhere.Webhook, "Any address that takes a JSON POST: {\"app\", \"title\", \"message\"}. For Home Assistant, IFTTT, your own scripts…",
                           grid => _webhookUrl = Form.Field(grid, "Address", secrets.WebhookUrl, "https://…", secret: true, typed: _ => Changed()));

        var test = Form.Buttons(this);
        Form.Button(test, "Send a test", () => _ = Test());
        _result = Form.Result(test);
    }

    /// <summary>
    /// Telegram, both ways: paired, with "Pair again" and "Forget it"; or the steps to pair a bot of their own: make it
    /// with BotFather, paste its token (checked, and given her name), scan the code it makes and press Start.
    /// </summary>
    private void BuildTelegram(Texting texting, string name, bool paired)
    {
        Form.Subheading(this, "Text with her on Telegram");
        var done = Form.Buttons(this);
        var steps = new VBoxContainer();
        steps.AddThemeConstantOverride("separation", 10);
        if (paired)
        {
            var state = Form.Result(done);
            Form.Say(state, "Paired: you can text each other there.", Paper.Good);
            Form.Button(done, "Pair again", () =>
            {
                done.Visible = false;
                steps.Visible = true;
            });
            Form.Button(done, "Forget it", () => TelegramForgotten?.Invoke());
        }
        done.Visible = paired;
        steps.Visible = !paired;
        AddChild(steps);

        Form.Hint(steps, $"A bot of your own, for {name} alone: about two minutes, all but the last step in Telegram.");
        var first = Form.Buttons(steps);
        first.AddChild(Form.Hint($"1. Open BotFather (Telegram in your browser; log in there once with your phone) and send /newbot. Give " +
                                 $"it {name}'s name, then a username ending in \"bot\"."));
        Form.Button(first, "Open BotFather", () => OS.ShellOpen(BotFather));
        Form.Hint(steps, "2. Paste the token BotFather gives you, and Check it.");
        var token = Form.Field(Form.Grid(steps), "Bot token", paired ? _secrets.TelegramToken : null, "123456:ABC…", secret: true);
        var check = Form.Buttons(steps);
        var found = Form.Result(check);
        var scan = new VBoxContainer { Visible = false };
        scan.AddThemeConstantOverride("separation", 6);
        steps.AddChild(scan);
        Form.Hint(scan, "3. Scan this with your phone, or open it in this browser, and press Start. The code is good for ten minutes.");
        var code = new TextureRect
        {
            TextureFilter = TextureFilterEnum.Nearest, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspect, CustomMinimumSize = new Vector2(180, 180), SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
        };
        scan.AddChild(code);
        var open = Form.Buttons(scan);
        var link = new LineEdit { Editable = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        open.AddChild(link);
        var web = "";
        Form.Button(open, "Open it in this browser", () => OS.ShellOpen(web));
        Form.Say(Form.Result(scan), "Waiting for you to press Start…", Paper.Faint);
        Form.Button(check, "Check", async () =>
        {
            var given = token.Text.Trim();
            Form.Say(found, "Asking Telegram…", Paper.Faint);
            var bot = new Telegram(Http, given);
            string? username;
            try
            {
                username = given.Length == 0 ? null : await bot.Me();
            }
            catch (Exception e) when (e is TelegramException or HttpRequestException or TaskCanceledException)
            {
                if (IsInstanceValid(this))
                    Form.Say(found, $"Telegram can't be reached: {e.Message}", Paper.Bad);
                return;
            }
            if (!IsInstanceValid(this))
                return;
            if (username is null)
            {
                Form.Say(found, "Telegram doesn't know that token: copy it again from BotFather.", Paper.Bad);
                return;
            }
            try
            {
                await bot.Name(name, $"{name}, from your INSERT COIN cabinet. She talks with one chat only: the one that pairs with her.");
            }
            catch (Exception e) when (e is TelegramException or HttpRequestException or TaskCanceledException)
            {
                GD.PrintErr($"Texting: could not give the bot her name: {e.Message}");     // pairing doesn't need it
            }
            if (!IsInstanceValid(this))
                return;
            var pairing = texting.Pair(given);
            var address = Pairing.Link(username, pairing);
            web = Pairing.WebLink(username, pairing);
            Form.Say(found, $"Found @{username}.", Paper.Good);
            code.Texture = PhoneCard.Code(address);
            link.Text = address;
            scan.Visible = true;
        }).SizeFlagsVertical = SizeFlags.ShrinkCenter;
        check.MoveChild(found, -1);
        Form.Hint(steps, "Each profile needs a bot of its own: the demo too.");
        Form.Hint(this, "Her chat goes through Telegram's servers and isn't end-to-end encrypted: bots can't use Telegram's secret chats. " +
                        "What she texts can include what she knows of you: your agenda, how you've slept, what you told her.");
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

    private Elsewhere Where() => _elsewhere with
    {
        Ntfy = _ntfy.ButtonPressed, NtfyServer = _ntfyServer.Text.Trim().Length > 0 ? _ntfyServer.Text.Trim() : "https://ntfy.sh",
        Discord = _discord.ButtonPressed, Webhook = _webhook.ButtonPressed,
    };

    private ElsewhereSecrets Secrets() => _secrets with
    {
        NtfyTopic = Value(_ntfyTopic), NtfyToken = Value(_ntfyToken), DiscordWebhook = Value(_discordUrl), WebhookUrl = Value(_webhookUrl),
    };

    private void Changed() => Chosen?.Invoke(Where(), Secrets());

    /// <summary>A test reminder through each place on (Telegram too, once paired), saying how each went.</summary>
    private async Task Test()
    {
        var where = Where();
        var now = DateTimeOffset.Now;
        var notice = Reminders.NoticeFor([new Plan(0, "This is how a reminder reaches you.", now, false, TimeSpan.Zero, null, now, null)], now);
        var ticked = new[] { where.Ntfy, where.Discord, where.Telegram, where.Webhook }.Count(on => on);
        var parcels = Couriers.Parcels(where, Secrets(), notice).Count();
        if (ticked == 0)
        {
            Form.Say(_result, "Pair Telegram or tick a place first.", Paper.Bad);
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
