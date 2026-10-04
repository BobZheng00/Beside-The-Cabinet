using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Game.Desktop;
using InsertCoin.Mind;
using HttpClient = System.Net.Http.HttpClient;

namespace InsertCoin.Game.App;

/// <summary>
/// Where reminders reach the player. The ticket at the screen corner and her bubble always; a chime and a Windows
/// notification if chosen; and the player's phone or another app through ntfy, Discord, Telegram or any webhook.
/// Addresses and tokens are stored encrypted (see <see cref="KeyStore"/>). "Try it" sends a reminder through
/// everything as filled in, before saving.
/// </summary>
public partial class ReminderSettings : Window
{
    /// <summary>The settings and secrets after Save.</summary>
    public event Action<bool, bool, Elsewhere, ElsewhereSecrets>? Saved;

    /// <summary>"Try it": a reminder goes off in the cabinet now, chiming or not.</summary>
    public event Action<bool>? Tried;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private CheckBox _sound = null!, _windows = null!, _ntfy = null!, _discord = null!, _telegram = null!, _webhook = null!;
    private LineEdit _ntfyServer = null!, _ntfyTopic = null!, _ntfyToken = null!, _discordUrl = null!, _telegramToken = null!, _telegramChat = null!, _webhookUrl = null!;
    private Label _result = null!;

    public void Build(bool sound, bool windows, Elsewhere elsewhere, ElsewhereSecrets secrets)
    {
        Title = "Reminders";
        Visible = false;
        Transient = true;
        CloseRequested += QueueFree;

        var page = new PanelContainer();
        page.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(page);
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 8);
        page.AddChild(outer);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        outer.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(column);

        column.AddChild(new Label { Text = "Reminders", ThemeTypeVariation = "Heading" });
        column.AddChild(Hint("Ask her to remind you of something and it goes off at that time: always on a ticket at the corner of the screen " +
                             "and in her bubble. It can also:"));
        _sound = Check(column, "Chime", sound, "A soft two-note chime (unless the cabinet's sounds are muted).");
        _windows = Check(column, "Windows notification", windows, "Also kept in Windows' notification centre, so you see it when you come back.");

        column.AddChild(new Label { Text = "Send to your phone or another app", ThemeTypeVariation = "Subheading" });
        column.AddChild(Hint("Addresses and tokens stay on this PC, encrypted for your Windows account."));

        _ntfy = Check(column, "ntfy", elsewhere.Ntfy, "Free phone notifications, no account: install the ntfy app and subscribe to a topic name only you know.");
        var ntfy = Grid(column);
        _ntfyServer = Field(ntfy, "Server", elsewhere.NtfyServer, "https://ntfy.sh", secret: false);
        _ntfyTopic = Field(ntfy, "Topic", secrets.NtfyTopic, "a long name only you know", secret: true);
        _ntfyToken = Field(ntfy, "Token", secrets.NtfyToken, "only for a server that needs one", secret: true);

        _discord = Check(column, "Discord", elsewhere.Discord, "A message in a channel: Server settings → Integrations → Webhooks → New webhook → Copy URL.");
        _discordUrl = Field(Grid(column), "Webhook", secrets.DiscordWebhook, "https://discord.com/api/webhooks/…", secret: true);

        _telegram = Check(column, "Telegram", elsewhere.Telegram, "A message from your own bot: make one with @BotFather, send it a message, then find your chat id.");
        var telegram = Grid(column);
        _telegramToken = Field(telegram, "Bot token", secrets.TelegramToken, "123456:ABC…", secret: true);
        _telegramChat = Field(telegram, "Chat id", secrets.TelegramChat, "your chat's number", secret: false);

        _webhook = Check(column, "Webhook", elsewhere.Webhook, "Any address that takes a JSON POST: {\"app\", \"title\", \"message\"}. For Home Assistant, IFTTT, your own scripts…");
        _webhookUrl = Field(Grid(column), "Address", secrets.WebhookUrl, "https://…", secret: true);

        var test = new HBoxContainer();
        test.AddThemeConstantOverride("separation", 10);
        var tryIt = new Button { Text = "Try it", ThemeTypeVariation = "Quiet", SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        _result = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        tryIt.Pressed += () => _ = Try();
        test.AddChild(tryIt);
        test.AddChild(_result);
        column.AddChild(test);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var cancel = new Button { Text = "Cancel", ThemeTypeVariation = "Quiet" };
        var save = new Button { Text = "Save" };
        cancel.Pressed += QueueFree;
        save.Pressed += () =>
        {
            Saved?.Invoke(_sound.ButtonPressed, _windows.ButtonPressed, Where(), Secrets());
            QueueFree();
        };
        buttons.AddChild(cancel);
        buttons.AddChild(save);
        outer.AddChild(buttons);
    }

    public void Open() => Paper.Open(this, new Vector2(560, 700));

    /// <summary>Dev: as if "Try it" were clicked.</summary>
    public void DevTry() => _ = Try();

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

    private async Task Try()
    {
        Tried?.Invoke(_sound.ButtonPressed);
        var notice = Reminders.NoticeFor([new Plan(0, "This is how a reminder reaches you.", DateTimeOffset.Now, false, true, null, DateTimeOffset.Now, null)],
                                         DateTimeOffset.Now);
        var said = new List<string> { "Sent to the ticket and her bubble" };
        if (_windows.ButtonPressed)
            said.Add(WindowsNotice.Show(notice.Title, notice.Text) ? "a Windows notification" : "not to Windows (it refused)");
        var where = Where();
        var parcels = Couriers.Parcels(where, Secrets(), notice).Count();
        var missing = new[] { (_ntfy, "ntfy"), (_discord, "Discord"), (_telegram, "Telegram"), (_webhook, "the webhook") }
            .Count(c => c.Item1.ButtonPressed) - parcels;
        if (parcels > 0)
            Show(string.Join(", ", said) + "; sending…", Paper.Faint);
        var results = parcels > 0 ? await Couriers.Send(Http, where, Secrets(), notice) : [];
        if (!IsInstanceValid(this))
            return;
        said.AddRange(results.Select(r => r.Error is null ? r.Name : $"not {r.Name} ({r.Error})"));
        var text = string.Join(", ", said) + ".";
        if (missing > 0)
            text += " Fill in the address of each place you ticked.";
        Show(text, results.Any(r => r.Error is not null) || missing > 0 ? Paper.Bad : Paper.Good);
    }

    private void Show(string text, Color colour)
    {
        _result.Text = text;
        _result.AddThemeColorOverride("font_color", colour);
    }

    private static string? Value(LineEdit field) => field.Text.Trim().Length > 0 ? field.Text.Trim() : null;

    private static CheckBox Check(VBoxContainer column, string label, bool on, string hint)
    {
        var check = new CheckBox { Text = label, ButtonPressed = on };
        column.AddChild(check);
        column.AddChild(Hint(hint));
        return check;
    }

    private static GridContainer Grid(VBoxContainer column)
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 6);
        column.AddChild(grid);
        return grid;
    }

    private static LineEdit Field(GridContainer grid, string label, string? value, string placeholder, bool secret)
    {
        grid.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(90, 0) });
        var field = new LineEdit { Text = value ?? "", PlaceholderText = placeholder, Secret = secret, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddChild(field);
        return field;
    }

    private static Label Hint(string text) =>
        new() { Text = text, ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };
}
