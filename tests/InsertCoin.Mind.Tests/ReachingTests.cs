namespace InsertCoin.Mind.Tests;

/// <summary>Reaching the player as a roommate would: where they are, the one table, and how it comes at the PC (spec 2026-10-10-reaching-the-player.md).</summary>
public class ReachingTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 10, 12, 0, 0, TimeSpan.FromHours(-4));
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    private static Whereabouts Where(double idleMinutes, bool locked = false, bool busy = false, bool quiet = false, Leaving? said = null) =>
        Reaching.Where(new Watched(TimeSpan.FromMinutes(idleMinutes), locked, busy, quiet, said), FiveMinutes, Noon);

    [Fact]
    public void Input_in_the_last_minute_is_here_a_minute_without_is_unsure_and_the_away_time_is_away()
    {
        Assert.Equal(Whereabouts.Here, Where(0.5));
        Assert.Equal(Whereabouts.Unsure, Where(1));
        Assert.Equal(Whereabouts.Unsure, Where(4.9));
        Assert.Equal(Whereabouts.Away, Where(5));
        Assert.Equal(Whereabouts.Away, Reaching.Where(new Watched(TimeSpan.FromMinutes(3), false, false, false), TimeSpan.FromMinutes(2), Noon));   // the player's own away time
    }

    [Fact]
    public void A_locked_screen_is_away_at_once_and_away_in_the_quiet_hours_is_asleep()
    {
        Assert.Equal(Whereabouts.Away, Where(0.2, locked: true));
        Assert.Equal(Whereabouts.Asleep, Where(0.2, locked: true, quiet: true));
        Assert.Equal(Whereabouts.Asleep, Where(30, quiet: true));
        Assert.Equal(Whereabouts.Unsure, Where(2, quiet: true));      // a minute or two without input is no sleep
    }

    [Fact]
    public void Playing_or_talking_with_her_is_here_without_input_but_not_behind_a_locked_screen()
    {
        Assert.Equal(Whereabouts.Here, Where(8, busy: true));
        Assert.Equal(Whereabouts.Away, Where(8, busy: true, locked: true));        // a game left running as they went out
    }

    [Fact]
    public void Saying_they_are_off_holds_until_they_use_the_pc_again_beyond_the_minute_it_takes_to_leave()
    {
        var out10Ago = new Leaving(Asleep: false, Noon.AddMinutes(-10));
        Assert.Equal(Whereabouts.Away, Where(10, said: out10Ago));                  // no input since
        Assert.Equal(Whereabouts.Away, Where(9.5, said: out10Ago));                 // closing things down after saying it
        Assert.Equal(Whereabouts.Here, Where(0.2, said: out10Ago));                 // back
        Assert.True(Reaching.Back(new Watched(TimeSpan.FromMinutes(0.2), false, false, false, out10Ago), Noon));
        Assert.False(Reaching.Back(new Watched(TimeSpan.FromMinutes(9.5), false, false, false, out10Ago), Noon));

        var goodnight = new Leaving(Asleep: true, Noon.AddSeconds(-20));
        Assert.Equal(Whereabouts.Asleep, Where(0, said: goodnight));                // said just now, typing the last words
        Assert.Equal(Whereabouts.Asleep, Where(0, busy: true, said: goodnight));    // still talking with her as they go
    }

    [Fact]
    public void What_they_said_is_kept_and_read_back()
    {
        Assert.Equal(new Leaving(true, Noon), Reaching.Said(Reaching.Marked(true, Noon)));
        Assert.Equal(new Leaving(false, Noon), Reaching.Said(Reaching.Marked(false, Noon)));
        Assert.Null(Reaching.Said(null));
        Assert.Null(Reaching.Said(""));
        Assert.Null(Reaching.Said("gym|yesterday"));
    }

    [Theory]
    // at the PC: a must at once, a should after a word first with its ticket, a could after a word first and no more
    [InlineData(Whereabouts.Here, Matters.Must, true, Way.Say, false, false)]
    [InlineData(Whereabouts.Here, Matters.Should, true, Way.Call, true, true)]
    [InlineData(Whereabouts.Here, Matters.Should, false, Way.Call, true, false)]
    [InlineData(Whereabouts.Here, Matters.Could, false, Way.Call, false, false)]
    // unsure: the same, and a text if they turn out to be gone
    [InlineData(Whereabouts.Unsure, Matters.Must, true, Way.Say, false, true)]
    [InlineData(Whereabouts.Unsure, Matters.Should, true, Way.Call, true, true)]
    [InlineData(Whereabouts.Unsure, Matters.Could, false, Way.Call, false, false)]
    // away: what matters is texted; what only makes sense at the PC waits; a could lets go
    [InlineData(Whereabouts.Away, Matters.Must, true, Way.Text, false, false)]
    [InlineData(Whereabouts.Away, Matters.Must, false, Way.Wait, false, false)]
    [InlineData(Whereabouts.Away, Matters.Should, true, Way.Text, false, false)]
    [InlineData(Whereabouts.Away, Matters.Should, false, Way.Wait, false, false)]
    [InlineData(Whereabouts.Away, Matters.Could, true, Way.Lapse, false, false)]
    // asleep: only a must is texted (they set it for then); a should waits for the morning
    [InlineData(Whereabouts.Asleep, Matters.Must, true, Way.Text, false, false)]
    [InlineData(Whereabouts.Asleep, Matters.Should, true, Way.Wait, false, false)]
    [InlineData(Whereabouts.Asleep, Matters.Could, false, Way.Lapse, false, false)]
    public void The_table(Whereabouts where, Matters matters, bool anywhere, Way way, bool ticket, bool textIfGone) =>
        Assert.Equal(new Route(way, ticket, textIfGone), Reaching.Route(matters, anywhere, where));

    [Fact]
    public void With_nowhere_to_text_them_what_would_be_texted_waits_for_them()
    {
        Assert.Equal(new Route(Way.Wait), Reaching.Route(Matters.Must, true, Whereabouts.Away, canText: false));
        Assert.Equal(new Route(Way.Say), Reaching.Route(Matters.Must, true, Whereabouts.Unsure, canText: false));
        Assert.Equal(new Route(Way.Call, Ticket: true), Reaching.Route(Matters.Should, true, Whereabouts.Here, canText: false));
        Assert.Equal(new Route(Way.Lapse), Reaching.Route(Matters.Could, true, Whereabouts.Away, canText: false));
    }

    [Fact]
    public void How_much_she_reaches_out_changes_only_what_reaches_the_phone()
    {
        // only what you set: what is hers waits for them, what they set still goes
        Assert.Equal(new Route(Way.Wait), Reaching.Route(Matters.Should, true, Whereabouts.Away, reach: Reach.OnlyWhatYouSet));
        Assert.Equal(new Route(Way.Call, Ticket: true), Reaching.Route(Matters.Should, true, Whereabouts.Unsure, reach: Reach.OnlyWhatYouSet));
        Assert.Equal(new Route(Way.Text), Reaching.Route(Matters.Must, true, Whereabouts.Away, reach: Reach.OnlyWhatYouSet));
        // keeps in touch: a could that makes sense anywhere is texted while away, never at night
        Assert.Equal(new Route(Way.Text), Reaching.Route(Matters.Could, true, Whereabouts.Away, reach: Reach.KeepsInTouch));
        Assert.Equal(new Route(Way.Lapse), Reaching.Route(Matters.Could, false, Whereabouts.Away, reach: Reach.KeepsInTouch));
        Assert.Equal(new Route(Way.Lapse), Reaching.Route(Matters.Could, true, Whereabouts.Asleep, reach: Reach.KeepsInTouch));
        Assert.Equal(new Route(Way.Call), Reaching.Route(Matters.Could, true, Whereabouts.Here, reach: Reach.KeepsInTouch));
    }

    [Fact]
    public void Her_first_chance_comes_half_an_hour_into_an_absence_never_in_the_quiet_hours()
    {
        var half = TimeSpan.FromMinutes(30);

        Assert.True(Reaching.MayTextOwn(Reach.KeepsInTouch, Whereabouts.Away, half, quiet: false));
        Assert.False(Reaching.MayTextOwn(Reach.KeepsInTouch, Whereabouts.Away, TimeSpan.FromMinutes(25), quiet: false));
        Assert.False(Reaching.MayTextOwn(Reach.LikeARoommate, Whereabouts.Away, half, quiet: false));
        Assert.False(Reaching.MayTextOwn(Reach.KeepsInTouch, Whereabouts.Asleep, half, quiet: false));
        Assert.False(Reaching.MayTextOwn(Reach.KeepsInTouch, Whereabouts.Away, half, quiet: true));                         // out late, by their word
        Assert.Contains("1 h 20 min", Reaching.OwnText(TimeSpan.FromMinutes(80)));
        Assert.Contains("[later: HH:MM | what about]", Reaching.OwnText(half));
    }

    [Fact]
    public void Her_plan_is_the_next_such_time_from_half_an_hour_to_twelve_hours_ahead()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test", Noon.Offset, "test", "test");
        Later? Read(string payload, DateTimeOffset? now = null) => Later.Read(payload, now ?? Noon, zone);

        Assert.Equal(new Later(Noon.AddMinutes(90), "ask how the gym went"), Read("13:30 | ask how the gym went"));
        Assert.Equal(new Later(Noon.AddHours(6), "dinner"), Read("2026-10-10 18:00 | dinner"));                        // a date before it, as in [plan: …]
        Assert.Equal(new Later(Noon.AddHours(2), "lunch"), Read("14:00 lunch"));                                      // no bar
        Assert.Equal(new Later(Noon.AddHours(19), "good morning"), Read("07:00 | good morning", Noon.AddHours(10)));  // past midnight
        Assert.Null(Read("12:15 | too soon"));
        Assert.Null(Read("11:00 | tomorrow, too far ahead"));
        Assert.Null(Read("25:00 | no such time"));
        Assert.Null(Read("soon | no time"));
        Assert.Null(Read("13:30 |"));
    }

    [Fact]
    public void Her_plan_is_kept_in_her_memory_and_told_back_to_her_when_due()
    {
        var plan = new Later(Noon.AddMinutes(90), "ask how the gym went | and lunch");

        Assert.Equal(plan, Later.FromMark(plan.Mark));
        Assert.Null(Later.FromMark(""));
        Assert.Null(Later.FromMark(null));
        Assert.StartsWith("Earlier you meant to bring this up around 13:30: ask how the gym went | and lunch.", Reaching.LaterNote(plan, back: false));
        Assert.StartsWith("The player is back at the PC. Earlier you meant", Reaching.LaterNote(plan, back: true));
    }

    [Fact]
    public async Task Her_plan_to_reach_them_later_is_kept_and_never_shown()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", new Persona("Shino", "Shino is cheerful."), new FakeClock(Noon));
        var shown = new List<string>();

        await conversation.Answer(new FakeModel("Have fun at the gym! [out] [later: 13:30 | ask how the gym went]"), "I'm off to the gym", "", shown.Add, (_, _) => { });

        Assert.DoesNotContain("[", string.Concat(shown));
        Assert.Equal(new Later(Noon.AddMinutes(90), "ask how the gym went"), Later.FromMark(temp.Store.Mark(Reaching.LaterMark)));
    }

    [Fact]
    public void She_is_told_she_may_plan_to_reach_them_only_when_keeping_in_touch()
    {
        var persona = new Persona("Shino", "Shino is cheerful.");

        Assert.Contains("add [later: HH:MM | what about] to plan when you'd next like to reach them", Prompt.Build(persona, "", "", null, [], "", "hi", keepsInTouch: true)[0].Text);
        Assert.DoesNotContain("[later:", Prompt.Build(persona, "", "", null, [], "", "hi")[0].Text);
    }

    private static Telling Lasting(TimeSpan fresh) => new("CHECK-IN", "How did it go?", "", Matters.Should, true, Noon, fresh);

    [Fact]
    public void At_the_pc_what_can_wait_waits_for_a_settled_and_free_moment_and_the_rest_comes_as_it_can()
    {
        var checkIn = Lasting(TimeSpan.FromHours(12));
        var bedtime = Lasting(TimeSpan.FromMinutes(20));

        Assert.Equal(Saying.Aloud, Reaching.AtThePc(checkIn, Whereabouts.Here, settled: true, fullScreen: false, inCall: false));
        Assert.Equal(Saying.Later, Reaching.AtThePc(checkIn, Whereabouts.Here, settled: false, fullScreen: false, inCall: false));   // just sat down
        Assert.Equal(Saying.Aloud, Reaching.AtThePc(checkIn, Whereabouts.Unsure, settled: false, fullScreen: false, inCall: false)); // unsure: she reaches out
        Assert.Equal(Saying.Later, Reaching.AtThePc(checkIn, Whereabouts.Here, settled: true, fullScreen: true, inCall: false));
        Assert.Equal(Saying.Later, Reaching.AtThePc(checkIn, Whereabouts.Here, settled: true, fullScreen: false, inCall: true));

        Assert.Equal(Saying.Aloud, Reaching.AtThePc(bedtime, Whereabouts.Here, settled: false, fullScreen: false, inCall: false));
        Assert.Equal(Saying.Ticket, Reaching.AtThePc(bedtime, Whereabouts.Here, settled: true, fullScreen: true, inCall: false));
        Assert.Equal(Saying.Bubble, Reaching.AtThePc(bedtime, Whereabouts.Here, settled: true, fullScreen: false, inCall: true));
    }

    [Fact]
    public void Something_to_tell_stays_fresh_for_its_time_and_heads_a_text_in_plain_words()
    {
        var reminder = new Telling("REMINDER", "note", "Reminder: call Mum.", Matters.Must, true, Noon, TimeSpan.FromMinutes(10));

        Assert.True(reminder.FreshAt(Noon.AddMinutes(10)));
        Assert.False(reminder.FreshAt(Noon.AddMinutes(11)));
        Assert.Equal("Reminder", reminder.Heading);
        Assert.Equal("Stand up", (reminder with { Title = "STAND UP" }).Heading);
    }

    [Fact]
    public void A_text_of_hers_comes_as_a_message_from_her_not_with_an_alarm_clock()
    {
        var where = new Elsewhere { Ntfy = true, Discord = true, Telegram = true };
        var secrets = new ElsewhereSecrets { NtfyTopic = "t", DiscordWebhook = "https://discord.com/api/webhooks/1/x", TelegramToken = "1:A", TelegramChat = "42" };

        var hers = Couriers.Parcels(where, secrets, new Notice("Hana", "How did the dentist go?", Hers: true)).ToDictionary(p => p.Name);
        var plain = Couriers.Parcels(where, secrets, new Notice("Reminder", "14:30  Call Mum")).ToDictionary(p => p.Name);

        Assert.Contains("\"tags\":[\"speech_balloon\"]", hers["ntfy"].BodyJson);
        Assert.Contains("\"title\":\"Hana\"", hers["ntfy"].BodyJson);
        Assert.Contains("\"content\":\"**Hana**: How did the dentist go?\"", hers["Discord"].BodyJson);
        Assert.Contains("\"text\":\"How did the dentist go?\"", hers["Telegram"].BodyJson);      // her own bot: no name in front
        Assert.Contains("\"tags\":[\"alarm_clock\"]", plain["ntfy"].BodyJson);
        Assert.Contains("⏰", System.Text.RegularExpressions.Regex.Unescape(plain["Telegram"].BodyJson));
    }

    [Theory]
    [InlineData("Have fun at the gym! [out]", "out")]
    [InlineData("[sleep] Sleep well, see you tomorrow.", "sleep")]
    public async Task Her_word_that_they_are_off_is_kept_and_never_shown(string reply, string kept)
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", new Persona("Shino", "Shino is cheerful."), new FakeClock(Noon));
        var shown = new List<string>();

        await conversation.Answer(new FakeModel(reply), "I'm off now", "", shown.Add, (_, _) => { });

        Assert.DoesNotContain("[", string.Concat(shown));
        Assert.Equal(new Leaving(kept == "sleep", Noon), Reaching.Said(temp.Store.Mark(Reaching.Mark)));
    }

    [Fact]
    public void She_is_told_to_mark_the_player_leaving_only_when_they_go_now()
    {
        var sent = Prompt.Build(new Persona("Shino", "Shino is cheerful."), "", "", null, [], "", "hi");

        Assert.Contains("when the player says they are leaving it now (going out, to class, to the gym), add [out] to your reply; when they are going to sleep, [sleep].",
                        sent[0].Text);
        Assert.Contains("Only when they are going now, not for plans later on.", sent[0].Text);
    }
}
