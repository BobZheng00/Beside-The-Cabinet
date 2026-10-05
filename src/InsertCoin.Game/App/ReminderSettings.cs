using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Game.Desktop;
using InsertCoin.Mind;
using HttpClient = System.Net.Http.HttpClient;

namespace InsertCoin.Game.App;

/// <summary>
/// How reminders reach the player. In the cabinet: the ticket at the screen corner (how long it stays, how long a
/// snooze lasts), her telling them in her own words, a chime (even when muted), a Windows notification; a reminder
/// before every timed thing on the agenda; and the player's phone or another app through ntfy, Discord, Telegram or
/// any webhook. Addresses and tokens are stored encrypted (see <see cref="KeyStore"/>). "Try it" sends a reminder
/// through everything as filled in, before saving.
/// </summary>
public partial class ReminderSettings : Window
{
    private static readonly (string Label, int Seconds)[] Stays = [("15 seconds", 15), ("1 minute", 60), ("5 minutes", 300), ("Until I close it", 0)];
    private static readonly int[] Snoozes = [5, 10, 15, 30, 60];
    private static readonly (string Label, int? Minutes)[] Leads =
        [("Only when I ask", null), ("5 min before", 5), ("10 min before", 10), ("15 min before", 15), ("30 min before", 30), ("1 hour before", 60)];

    /// <summary>The choices and secrets after Save.</summary>
    public event Action<ReminderChoices, ElsewhereSecrets>? Saved;

    /// <summary>"Try it": a reminder goes off in the cabinet now, chiming or not.</summary>
    public event Action<bool>? Tried;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private CheckBox _spoken = null!, _sound = null!, _evenMuted = null!, _windows = null!, _ntfy = null!, _discord = null!, _telegram = null!, _webhook = null!;
    private OptionButton _stays = null!, _snooze = null!, _lead = null!;
    private LineEdit _ntfyServer = null!, _ntfyTopic = null!, _ntfyToken = null!, _discordUrl = null!, _telegramToken = null!, _telegramChat = null!, _webhookUrl = null!;
    private Label _result = null!;

    public void Build(ReminderChoices choices, ElsewhereSecrets secrets)
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
        column.AddChild(Hint("Ask her to remind you of something and it goes off at that time, always on a ticket at the corner of the screen, " +
                             "with Snooze and OK."));
        var ticket = Grid(column);
        _stays = Choice(ticket, "Ticket stays", Stays.Select(s => s.Label), Array.FindIndex(Stays, s => s.Seconds == choices.TicketSeconds));
        _snooze = Choice(ticket, "Snooze for", Snoozes.Select(m => $"{m} minutes"), Array.IndexOf(Snoozes, choices.SnoozeMinutes));

        _spoken = Check(column, "She tells you, in her own words", choices.Spoken,
                        "Once she has finished what she is saying, and in your language, when her mind is connected. Otherwise a plain line in her bubble.");
        _sound = Check(column, "Chime", choices.Sound, "A soft two-note chime, like a doorbell.");
        _evenMuted = new CheckBox { Text = "Even when the cabinet's sounds are muted", ButtonPressed = choices.ChimeWhenMuted };
        var indent = new MarginContainer();
        indent.AddThemeConstantOverride("margin_left", 28);
        indent.AddChild(_evenMuted);
        column.AddChild(indent);
        _sound.Toggled += on => _evenMuted.Disabled = !on;
        _evenMuted.Disabled = !choices.Sound;
        _windows = Check(column, "Windows notification", choices.Windows, "Also kept in Windows' notification centre, so you see it when you come back.");

        column.AddChild(new Label { Text = "Before things on your agenda", ThemeTypeVariation = "Subheading" });
        _lead = Choice(Grid(column), "Remind me", Leads.Select(l => l.Label), Math.Max(0, Array.FindIndex(Leads, l => l.Minutes == choices.LeadMinutes)));
        column.AddChild(Hint("For every timed item on your agenda without a reminder of its own: an interview, a call, a class."));

        column.AddChild(new Label { Text = "Send to your phone or another app", ThemeTypeVariation = "Subheading" });
        column.AddChild(Hint("Addresses and tokens stay on this PC, encrypted for your Windows account."));
        var elsewhere = choices.Elsewhere;
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
            Saved?.Invoke(Choices(), Secrets());
            QueueFree();
        };
        buttons.AddChild(cancel);
        buttons.AddChild(save);
        outer.AddChild(buttons);
    }

    public void Open() => Paper.Open(this, new Vector2(560, 720));

    /// <summary>Dev: as if "Try it" were clicked.</summary>
    public void DevTry() => _ = Try();

    private ReminderChoices Choices() => new(
        _spoken.ButtonPressed, _sound.ButtonPressed, _evenMuted.ButtonPressed, _windows.ButtonPressed, Stays[_stays.Selected].Seconds,
        Snoozes[_snooze.Selected], Leads[_lead.Selected].Minutes, Where());

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
        var now = DateTimeOffset.Now;
        var notice = Reminders.NoticeFor([new Plan(0, "This is how a reminder reaches you.", now, false, TimeSpan.Zero, null, now, null)], now);
        var said = new List<string> { "Sent to the ticket and her bubble" };
        if (_windows.ButtonPressed)
            said.Add(WindowsNotice.Show(notice.Title, notice.Text) ? "a Windows notification" : "not to Windows (it refused)");
        var where = Where();
        var parcels = Couriers.Parcels(where, Secrets(), notice).Count();
        var missing = new[] { _ntfy, _discord, _telegram, _webhook }.Count(c => c.ButtonPressed) - parcels;
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

    private static OptionButton Choice(GridContainer grid, string label, IEnumerable<string> options, int selected)
    {
        grid.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(90, 0) });
        var choice = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        foreach (var option in options)
            choice.AddItem(option);
        choice.Selected = Math.Max(0, selected);
        grid.AddChild(choice);
        return choice;
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
